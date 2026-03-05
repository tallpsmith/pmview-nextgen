# pmview Classic Scene

## What I Built

This scene recreates the original PCP pmview visualization with all major components:

### Components Created:

1. **Disk** (1 yellow cylinder)
   - Position: Left side
   - Animates: Height and color based on disk usage

2. **Disk Controllers** (16 green cylinders in 4x4 grid)
   - Position: Back-left
   - Animates: Height and color based on controller activity

3. **CPU** (4 vertical bars for multi-core)
   - Position: Right side
   - Animates: Each core independently - height and color

4. **Memory** (1 cyan block)
   - Position: Bottom-center-right
   - Animates: Height and color based on memory usage

5. **Load Average** (3 blue cubes)
   - Position: Left-center
   - Animates: 1min, 5min, 15min load averages

6. **Network Matrix** (7 rows × 3 columns = 21 cubes)
   - Position: Center
   - Rows: 7 network interfaces (eth0, eth1, et11, et12, et13, xpi1, xpi0)
   - Columns: 3 metrics (Bytes, Packets, Errors)
   - Animates: Height and color for each cell

### Color System:

- **Green**: Low utilization (< 33%)
- **Blue**: Medium utilization (33-66%)
- **Red**: High utilization (> 66%)
- **Yellow**: Disk-specific
- **Cyan**: Memory-specific

### Camera Setup:

- Angled 3D perspective view (similar to original)
- Two-light setup for proper depth perception
- Black background

## How to Run:

1. Open project in Godot (already set as main scene)
2. Press F5 to run
3. Watch all metrics animate with simulated data

## API for Real Metrics:

The script exposes these functions for connecting real data:

```gdscript
# Set disk usage (0.0 - 1.0)
set_disk_usage(0.75)

# Set CPU core usage (core 0-3, value 0.0-1.0)
set_cpu_usage(0, 0.85)  # Core 0 at 85%

# Set memory usage (0.0 - 1.0)
set_memory_usage(0.65)

# Set load average (index 0-2, normalized value)
set_load_average(0, 0.5)  # 1-min load

# Set network metric (interface 0-6, metric 0-2, value 0.0-1.0)
set_network_metric(0, 0, 0.9)  # eth0 bytes at 90%
```

## Current State:

- ✅ All objects created programmatically
- ✅ Animated with simulated metrics (sine waves)
- ✅ Color changes based on utilization levels
- ✅ Height scaling works
- ✅ Smooth interpolation
- ⚠️ 3D text labels not yet added (need Label3D nodes)
- ⚠️ Layout positions may need tweaking

## Next Steps:

1. Run and validate visual layout
2. Adjust positions/sizes based on feedback
3. Add 3D text labels
4. Connect to real PCP metrics
