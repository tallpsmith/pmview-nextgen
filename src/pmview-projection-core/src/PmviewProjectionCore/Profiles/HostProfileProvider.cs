using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Profiles;

public class HostProfileProvider : IHostProfileProvider
{
    public IReadOnlyList<ZoneDefinition> GetProfile(HostOs os) => os switch
    {
        HostOs.Linux => LinuxProfile.GetZones(),
        HostOs.MacOs => MacOsProfile.GetZones(),
        _ => throw new ArgumentException($"No profile available for OS: {os}")
    };
}
