using Xunit;
using PmviewHostProjector.Models;
using PmviewHostProjector.Profiles;

namespace PmviewHostProjector.Tests.Profiles;

public class HostProfileProviderTests
{
    private readonly HostProfileProvider _provider = new();

    [Fact]
    public void GetProfile_Linux_ReturnsTenZones()
    {
        // 6 foreground (CPU, Load, Memory, Disk, Net-In, Net-Out) + 4 background = 10
        var zones = _provider.GetProfile(HostOs.Linux);
        Assert.Equal(10, zones.Count);
    }

    [Fact]
    public void GetProfile_MacOs_ReturnsTenZones()
    {
        var zones = _provider.GetProfile(HostOs.MacOs);
        Assert.Equal(10, zones.Count);
    }

    [Fact]
    public void GetProfile_MacOs_HasSameZoneNamesAsLinux()
    {
        var linux = _provider.GetProfile(HostOs.Linux).Select(z => z.Name);
        var macOs = _provider.GetProfile(HostOs.MacOs).Select(z => z.Name);
        Assert.Equal(linux, macOs);
    }

    [Fact]
    public void GetProfile_Unknown_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _provider.GetProfile(HostOs.Unknown));
    }
}
