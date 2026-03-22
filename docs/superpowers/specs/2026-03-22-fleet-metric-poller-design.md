# Fleet MetricPoller — Design Spec

## Overview

A fleet-wide metric polling system that fetches aggregate health metrics for multiple hosts and drives the CompactHost bars in the fleet view with real data. Replaces the static mock heights with live/archive metric values.

## Architecture

```
FleetViewController (GDScript)
    │
    ▼
FleetMetricPoller (C#, Godot Node)
    │  owns + manages 1–10 shards
    ▼
MetricPoller[] (existing C#, one per shard)
    │
    ▼
pmproxy series API
```

### FleetMetricPoller

New C# Node (`src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs`).

**Responsibilities:**
- Accepts a list of hostnames, an endpoint URL, and poll interval
- Shards hostnames across 1–10 internal MetricPoller instances (default max 25 hosts/shard)
- Hard cap: 250 hosts. Excess hosts are dropped with a warning log naming each dropped host
- Performs one-time discovery scrape of `hinv.ncpu` per host at startup (for CPU normalisation)
- Aggregates `MetricsUpdated` signals from all shards into a unified per-host dictionary
- Normalises raw metric values to 0.0–1.0
- Tracks scrape budget — if total scrape time exceeds poll interval, skips next tick and emits signal
- Supports both live and archive mode (delegates to underlying MetricPoller capabilities)

**Exports:**
- `Endpoint`: pmproxy URL
- `PollIntervalMs`: poll tick interval (default 2000ms for fleet)
- `MaxHostsPerShard`: default 25
- `MaxShards`: default 10
- `DiskMaxBytesPerSec`: configurable ceiling for disk rate normalisation (default 500MB/s)
- `NetworkMaxBytesPerSec`: configurable ceiling for network rate normalisation (default 125MB/s — ~1Gbps)
- `PagingMaxPagesPerSec`: configurable ceiling for paging rate normalisation (default 10000 pages/s)

**Signals:**
- `FleetMetricsUpdated(metrics: Dictionary)` — per-host normalised values
- `HostsDropped(count: int, hostnames: String[])` — hosts exceeding cap
- `ScrapeBudgetExceeded` — total scrape time exceeded poll interval
- `ConnectionStateChanged(state: String)` — delegates from first shard

### Signal Shape

```csharp
// FleetMetricsUpdated dictionary structure:
{
  "host-01": { "cpu": 0.72, "memory": 0.0, "disk": 0.12, "network": 0.03 },
  "host-02": { "cpu": 0.91, "memory": 0.45, "disk": 0.55, "network": 0.21 },
  // ...
}
```

## Metrics

### One-Time Discovery (startup)

| Metric | Purpose | Cached As |
|--------|---------|-----------|
| `hinv.ncpu` | CPU core count per host | `Dictionary<string, int>` hostname → ncpu |

### Per-Tick Polling (7 metrics across 4 bars)

| Bar | Metric(s) | Type | Normalisation |
|-----|-----------|------|---------------|
| CPU | `kernel.all.cpu.idle` | Counter → rate | `1.0 - (idle_rate / (ncpu * 1000))` clamped to 0.0–1.0 |
| Memory | `mem.vmstat.pgpgin` + `mem.vmstat.pgpgout` | Counter → rate | Combined pages/s, scaled against `PagingMaxPagesPerSec` |
| Disk | `disk.all.read_bytes` + `disk.all.write_bytes` | Counter → rate | Combined bytes/s, scaled against `DiskMaxBytesPerSec` |
| Network | `network.interface.in.bytes` + `network.interface.out.bytes` | Counter → rate | Combined bytes/s, scaled against `NetworkMaxBytesPerSec` |

### Normalisation Notes

- **CPU**: Inverted idle metric. `kernel.all.cpu.idle` is a counter in milliseconds. Rate gives idle-ms/s. Dividing by `ncpu * 1000` gives idle fraction. `1.0 - fraction` = utilisation.
- **Memory (paging)**: Most of the time near zero. A spike means real trouble — the one host with a tall orange bar is thrashing. This is the desired behaviour for fleet-at-a-glance.
- **Disk/Network**: Scaled against configurable ceilings since there's no single "total capacity" metric in PCP. Sensible defaults, tunable.
- **Counter-to-rate**: Handled by existing `MetricRateConverter` in MetricPoller — no new rate logic needed.

## Sharding

- **Default shard size**: 25 hosts per MetricPoller
- **Min shards**: 1
- **Max shards**: 10 (hard cap)
- **Max hosts**: 250 (10 × 25). Excess dropped with per-host warning log.
- Host assignment: round-robin across shards for even distribution

## Scrape Budget Tracking

- FleetMetricPoller timestamps when each poll tick starts
- When all shards complete (or timeout), checks `elapsed > poll_interval`
- If exceeded: sets `_skipNextTick = true`, emits `ScrapeBudgetExceeded`, logs warning
- On next tick: if `_skipNextTick`, clears it, skips poll, logs "skipping tick — previous scrape overran"

## Warning Toast System

### Generic Building Block

New addon UI component: `warning_toast.gd` + `warning_toast.tscn` in `src/pmview-bridge-addon/addons/pmview-bridge/ui/`.

Reusable across any scene (fleet view, host view, future scenes).

**Severity levels:**
- `WARNING` — `⚠` prefix, translucent orange background (`Color(0.9, 0.5, 0.1, 0.4)`)
- `ERROR` — `✖` prefix, translucent red background (`Color(0.9, 0.15, 0.1, 0.4)`)

**API:**
```gdscript
enum Severity { WARNING, ERROR }

func show_toast(message: String, severity: Severity = WARNING,
                duration: float = 5.0, cooldown_key: String = "") -> void
func clear_all() -> void
```

**Behaviour:**
- Top-left corner of HUD, stacks vertically if multiple active
- PressStart2P font, small size
- Tween fade-out at end of duration
- `cooldown_key` prevents same toast re-appearing within 30s (for recurring conditions)

### Fleet View Toasts

| Warning | Signal | Text | Duration | Cooldown |
|---------|--------|------|----------|----------|
| Host cap | `HostsDropped` | `⚠ 250 HOST LIMIT - {N} HOSTS DROPPED (SEE LOGS)` | 10s | None (once) |
| Scrape lag | `ScrapeBudgetExceeded` | `⚠ METRIC POLLING LAGGING` | 5s | 30s |

## FleetViewController Wiring

```gdscript
# In _ready(), after building grid:
fleet_poller = FleetMetricPoller.new()
fleet_poller.Endpoint = config.get("endpoint", "http://localhost:44322")
fleet_poller.PollIntervalMs = 2000
add_child(fleet_poller)
fleet_poller.StartPolling(hostnames)

fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)
fleet_poller.ScrapeBudgetExceeded.connect(_on_scrape_lagging)
fleet_poller.HostsDropped.connect(_on_hosts_dropped)

func _on_fleet_metrics_updated(metrics: Dictionary) -> void:
    for host in _hosts:
        var data: Dictionary = metrics.get(host.hostname, {})
        for metric_name: String in data:
            host.set_metric_value(metric_name, data[metric_name])
```

## Testing Strategy

### C# / xUnit (TDD)

- **Sharding**: given N hostnames, verify shard count, host distribution, >250 drops with names
- **Normalisation**: given raw values + ncpu, verify CPU/memory/disk/network produce correct 0.0–1.0. Edge cases: ncpu=0, zero rates, counter wraps
- **Scrape budget**: mock elapsed time, verify skip-next-tick and signal emission
- **Metric partitioning**: given series API response with mixed hostnames, verify per-host dictionary

### GDScript (visual verification)

- Warning toast appearance and fade
- CompactHost bars animating with real metric data
- End-to-end with dev-environment stack running

See also: GitHub issue #65 for comprehensive GDScript scene-level e2e testing strategy.
