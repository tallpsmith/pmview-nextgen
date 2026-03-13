using PmviewHostProjector.Models;

namespace PmviewHostProjector.Profiles;

/// <summary>
/// macOS host profile. PCP metric names are identical to Linux for all
/// metrics used in v1. Delegates to LinuxProfile — will diverge when
/// macOS-specific metrics are needed.
/// </summary>
public static class MacOsProfile
{
    public static IReadOnlyList<ZoneDefinition> GetZones() => LinuxProfile.GetZones();
}
