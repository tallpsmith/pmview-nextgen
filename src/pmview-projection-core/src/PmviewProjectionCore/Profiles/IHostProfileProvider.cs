using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Profiles;

public interface IHostProfileProvider
{
    IReadOnlyList<ZoneDefinition> GetProfile(HostOs os);
}
