using Xunit;
using PmviewHostProjector.Models;
using PmviewHostProjector.Profiles;

namespace PmviewHostProjector.Tests.Profiles;

public class HostProfileProviderTests
{
    private readonly HostProfileProvider _provider = new();

    [Fact]
    public void GetProfile_Linux_ReturnsEightZones()
    {
        var zones = _provider.GetProfile(HostOs.Linux);
        Assert.Equal(8, zones.Count);
    }

    [Fact]
    public void GetProfile_MacOs_ReturnsEightZones()
    {
        var zones = _provider.GetProfile(HostOs.MacOs);
        Assert.Equal(8, zones.Count);
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
