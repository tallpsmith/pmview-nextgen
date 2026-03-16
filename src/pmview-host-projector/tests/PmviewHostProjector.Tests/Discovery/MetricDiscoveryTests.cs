using Xunit;
using PmviewHostProjector.Discovery;
using PmviewHostProjector.Models;
using PmviewHostProjector.Tests.TestHelpers;

namespace PmviewHostProjector.Tests.Discovery;

public class MetricDiscoveryTests
{
    private StubPcpClient CreateLinuxStub()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Linux");
        stub.SetFetchStringResult("kernel.uname.nodename", "webserver01");
        stub.SetFetchDoubleResult("mem.physmem", 16_000_000);  // KB
        stub.SetInstanceDomain("kernel.percpu.cpu.user", "cpu0", "cpu1", "cpu2", "cpu3");
        stub.SetInstanceDomain("disk.dev.read_bytes", "sda", "sdb");
        stub.SetInstanceDomain("network.interface.in.bytes", "eth0", "lo");
        return stub;
    }

    [Fact]
    public async Task DiscoverAsync_DetectsLinux()
    {
        var stub = CreateLinuxStub();
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(HostOs.Linux, topology.Os);
    }

    [Fact]
    public async Task DiscoverAsync_DetectsHostname()
    {
        var stub = CreateLinuxStub();
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal("webserver01", topology.Hostname);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversCpuInstances()
    {
        var stub = CreateLinuxStub();
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(4, topology.CpuInstances.Count);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversDiskDevices()
    {
        var stub = CreateLinuxStub();
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(2, topology.DiskDevices.Count);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversPhysicalMemory()
    {
        var stub = CreateLinuxStub();
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(16_000_000L * 1024, topology.PhysicalMemoryBytes);
    }

    [Fact]
    public async Task DiscoverAsync_Darwin_DetectsMacOs()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Darwin");
        stub.SetFetchStringResult("kernel.uname.nodename", "macbook");
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(HostOs.MacOs, topology.Os);
    }

    [Fact]
    public async Task DiscoverAsync_UnknownOs_Throws()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Windows_NT");
        stub.SetFetchStringResult("kernel.uname.nodename", "winbox");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MetricDiscovery.DiscoverAsync(stub));
    }

    [Fact]
    public async Task DiscoverAsync_MissingInstances_ReturnsEmptyLists()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Linux");
        stub.SetFetchStringResult("kernel.uname.nodename", "minimal");
        // no instance domains configured — stub returns empty InstanceDomain
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Empty(topology.DiskDevices);
    }

    [Fact]
    public async Task DiscoverAsync_DiskDevices_ExcludesLoopDevices()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Linux");
        stub.SetFetchStringResult("kernel.uname.nodename", "host");
        stub.SetInstanceDomain("disk.dev.read_bytes", "sda", "loop0", "loop1", "loop2");
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(["sda"], topology.DiskDevices);
    }

    [Fact]
    public async Task DiscoverAsync_DiskDevices_ExcludesDmDevices()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Linux");
        stub.SetFetchStringResult("kernel.uname.nodename", "host");
        stub.SetInstanceDomain("disk.dev.read_bytes", "sda", "sdb", "dm-0", "dm-1");
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(["sda", "sdb"], topology.DiskDevices);
    }

    [Fact]
    public async Task DiscoverAsync_NetworkInterfaces_ExcludesLoopback()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Linux");
        stub.SetFetchStringResult("kernel.uname.nodename", "host");
        stub.SetInstanceDomain("network.interface.in.bytes", "eth0", "lo");
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(["eth0"], topology.NetworkInterfaces);
    }

    [Fact]
    public async Task DiscoverAsync_NetworkInterfaces_ExcludesContainerInterfaces()
    {
        var stub = new StubPcpClient();
        stub.SetFetchStringResult("kernel.uname.sysname", "Linux");
        stub.SetFetchStringResult("kernel.uname.nodename", "host");
        stub.SetInstanceDomain("network.interface.in.bytes",
            "eth0", "lo", "veth1a2b3c", "cni-podman0", "br-abc123");
        var topology = await MetricDiscovery.DiscoverAsync(stub);
        Assert.Equal(["eth0"], topology.NetworkInterfaces);
    }
}
