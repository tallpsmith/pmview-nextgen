using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Profiles;

public class HostProfileProvider : IHostProfileProvider
{
    public IReadOnlyList<ZoneDefinition> GetProfile(HostOs os) => os switch
    {
        HostOs.Linux => LinuxProfile.GetZones(),
        HostOs.MacOs => MacOsProfile.GetZones(),
        // Archives may lack kernel.uname.sysname — fall back to Linux
        // since PCP archives are overwhelmingly from Linux hosts.
        HostOs.Unknown => LinuxProfile.GetZones(),
    };
}
