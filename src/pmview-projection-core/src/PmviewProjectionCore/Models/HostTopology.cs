namespace PmviewProjectionCore.Models;

/// <summary>
/// Named HostOs to avoid collision with System.OperatingSystem under ImplicitUsings.
/// </summary>
public enum HostOs { Linux, MacOs, Unknown }

public record HostTopology(
    HostOs Os,
    string Hostname,
    IReadOnlyList<string> CpuInstances,
    IReadOnlyList<string> DiskDevices,
    IReadOnlyList<string> NetworkInterfaces,
    long? PhysicalMemoryBytes = null);
