# pmview-nextgen Dev Environment

Local development stack providing PCP with synthetic metric data. A pmlogsynth pipeline generates 7-day SaaS workload archives and seeds them into Valkey via pmseries, so PCP serves realistic historical data out of the box.

Modelled after [pmmcp's docker-compose.yml](https://github.com/tallpsmith/pmmcp/blob/main/docker-compose.yml).

## Services

| Service                    | Port  | Purpose                                                      |
|----------------------------|-------|--------------------------------------------------------------|
| **pmlogsynth-generator**   | —     | Builds PCP archives from YAML profiles in `profiles/`        |
| **pmlogsynth-seeder**      | —     | Loads archives into pmseries via Valkey                      |
| **pcp**                    | 44322 | Full PCP stack (pmcd, pmproxy, pmlogger) via init.d          |
| **valkey**                 | 6379  | Redis-compatible time-series backend for pmseries            |

### Pipeline order

```
pmlogsynth-generator (build archives)
        ↓
pmlogsynth-seeder (load into pmseries) ← waits for valkey healthy
        ↓
pcp (serve metrics) ← waits for seeder to complete
```

## Quick Start

```bash
cd dev-environment

# Start the stack (detached)
docker compose up -d

# Watch the generator + seeder pipeline
docker compose logs -f pmlogsynth-generator pmlogsynth-seeder

# Follow PCP logs once seeded
docker compose logs -f pcp

# Stop everything
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v
```

## Adding Profiles

Drop YAML profile files into `profiles/`. The generator iterates all `*.yml` and `*.yaml` files. See [pmlogsynth](https://github.com/tallpsmith/pmlogsynth) for the profile format.

The included `saas-diurnal-week.yml` generates a 7-day SaaS workload with realistic diurnal patterns — overnight lows, morning ramps, peak hours, lunch lulls, afternoon spikes, and evening tail-offs.

## Verifying the Stack

### 1. Check services are running

```bash
docker compose ps
```

### 2. Query pmseries for historical data

```bash
docker compose exec pcp pmseries 'kernel.all.cpu.user'
```

### 3. Create a pmproxy context

```bash
curl -s http://localhost:44322/pmapi/context | python3 -m json.tool
```

### 4. Fetch a metric value

```bash
CTX=$(curl -s http://localhost:44322/pmapi/context | python3 -c "import sys,json; print(json.load(sys.stdin)['context'])")
curl -s "http://localhost:44322/pmapi/fetch?context=${CTX}&names=kernel.all.load" | python3 -m json.tool
```

### 5. Verify Valkey is responding

```bash
docker compose exec valkey valkey-cli ping
# Expected: PONG
```

## Troubleshooting

### "Connection refused" on port 44322

The PCP container waits for the seeder to finish before starting. Watch progress with:

```bash
docker compose logs -f pmlogsynth-generator pmlogsynth-seeder
```

### PCP container won't start

The PCP container needs `privileged: true` to access kernel metrics. This is set in the compose file.

### Valkey data persistence

Valkey data is stored in the `valkey-data` named volume. Use `docker compose down -v` to wipe it for a fresh start.

### Port conflicts

If ports 44322 or 6379 are already in use on your host, either stop the conflicting service or edit the host-side port mappings in `docker-compose.yml`.

## Running Integration Tests

The PcpClient integration tests exercise the full API against a live pmproxy instance.
They require the dev-environment stack to be running.

### Quick start

```bash
# 1. Start the dev stack
cd dev-environment
docker compose up -d

# 2. Wait for healthy status (seeder must complete)
docker compose ps

# 3. Run integration tests only
cd ..
dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "Category=Integration" -v n

# 4. Run ALL tests (unit + integration)
dotnet test src/pcp-client-dotnet/PcpClient.sln -v n
```

### Skipping in CI

Integration tests skip gracefully when pmproxy is unreachable. No special
configuration is needed — just run `dotnet test` and they'll appear as `[Skipped]`
in the output. Series query tests additionally skip when Valkey is not available.

### Running unit tests only (no pmproxy needed)

```bash
dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "Category!=Integration" -v n
```
