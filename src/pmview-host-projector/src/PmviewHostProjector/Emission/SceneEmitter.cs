using System.Globalization;
using System.Text;
using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Emission;

/// <summary>
/// Orchestrates TscnWriter to emit a complete .tscn scene, then appends
/// Camera3D and DirectionalLight3D nodes computed from the scene bounds.
/// </summary>
public static class SceneEmitter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Emit(SceneLayout layout,
        string pmproxyEndpoint = "http://localhost:44322")
    {
        return TscnWriter.Write(layout, pmproxyEndpoint);
    }
}
