using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Profiles;

public interface IHostProfileProvider
{
    IReadOnlyList<ZoneDefinition> GetProfile(HostOs os);
}
