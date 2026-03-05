# pmview-nextgen Godot Prototype

## Quick Start

1. **Open in Godot:**
   ```bash
   open -a Godot godot-prototype/project.godot
   ```
   Or: Launch Godot.app and open this project

2. **Run the test scene:**
   - Press F5 or click the "Play" button in Godot
   - You should see an animated cube that:
     - Rotates continuously
     - Scales vertically based on a simulated metric
     - Changes color from blue (low) to red (high)

## What This Demonstrates

This test scene proves:
- ✅ Claude Code can generate working Godot scenes
- ✅ Programmatic animation works (rotation, scaling, color)
- ✅ Smooth interpolation/tweening (lerp)
- ✅ We can control 3D objects via code
- ✅ The `set_metric_value()` function shows how external data could drive animations

## Project Structure

```
godot-prototype/
├── project.godot          # Main project configuration
├── scenes/
│   └── test_cube.tscn     # Test scene with animated cube
├── scripts/
│   └── test_cube.gd       # Animation script
└── assets/                # For future assets (textures, models, etc.)
```

## Next Steps

Once this test works:
1. Create pmview-style scene (cylinders for disks, cubes for CPU/Mem/Load)
2. Add more sophisticated animations
3. Connect to real PCP metrics

## Notes

- This is a throwaway prototype (v0.1)
- Built with Godot 4.5.1
- Designed to validate Claude Code + Godot collaboration
