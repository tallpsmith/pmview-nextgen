using System.Text;

namespace PmviewHostProjector.Scaffolding;

/// <summary>
/// Emits main.tscn — the project's entry scene with camera, lighting,
/// environment, and a SceneRoot node for loading host-view scenes.
/// </summary>
public static class MainSceneWriter
{
    public static string Write()
    {
        var sb = new StringBuilder();

        // Header: 1 ext_resource (camera script) + 1 sub_resource (environment)
        sb.AppendLine("[gd_scene load_steps=2 format=3]");
        sb.AppendLine();

        sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/pmview-bridge/building_blocks/fly_orbit_camera.gd\" id=\"fly_camera_script\"]");
        sb.AppendLine();

        // WorldEnvironment sub_resource
        sb.AppendLine("[sub_resource type=\"Environment\" id=\"world_env\"]");
        sb.AppendLine("background_mode = 1");
        sb.AppendLine("background_color = Color(0.02, 0.02, 0.06, 1)");
        sb.AppendLine("ambient_light_source = 1");
        sb.AppendLine("ambient_light_color = Color(0.4, 0.4, 0.5, 1)");
        sb.AppendLine("ambient_light_energy = 0.5");
        sb.AppendLine();

        // Root node
        sb.AppendLine("[node name=\"Main\" type=\"Node3D\"]");
        sb.AppendLine();

        // Camera
        sb.AppendLine("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]");
        sb.AppendLine("script = ExtResource(\"fly_camera_script\")");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0.94, -0.34, 0, 0.34, 0.94, 0, 8, 15)");
        sb.AppendLine();

        // WorldEnvironment
        sb.AppendLine("[node name=\"WorldEnvironment\" type=\"WorldEnvironment\" parent=\".\"]");
        sb.AppendLine("environment = SubResource(\"world_env\")");
        sb.AppendLine();

        // Lights
        sb.AppendLine("[node name=\"KeyLight\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0.707, -0.707, 0, 0.707, 0.707, 0, 0, 0)");
        sb.AppendLine("light_energy = 1.2");
        sb.AppendLine("shadow_enabled = true");
        sb.AppendLine();

        sb.AppendLine("[node name=\"FillLight\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(-1, 0, 0, 0, 0.866, 0.5, 0, 0.5, -0.866, 0, 0, 0)");
        sb.AppendLine("light_energy = 0.5");
        sb.AppendLine();

        // SceneRoot — host-view scenes loaded as children
        sb.AppendLine("[node name=\"SceneRoot\" type=\"Node3D\" parent=\".\"]");
        sb.AppendLine();

        return sb.ToString();
    }
}
