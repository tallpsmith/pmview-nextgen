# pmview-nextgen Dev Environment

Local development stack providing PCP (with pmcd, pmproxy, pmlogger, and all services) plus Valkey for time-series storage.

Modelled after [pmmcp's docker-compose.yml](https://github.com/tallpsmith/pmmcp/blob/main/docker-compose.yml).

## Services

| Service   | Port  | Purpose                                                      |
|-----------|-------|--------------------------------------------------------------|
| **pcp**   | 44322 | Full PCP stack (pmcd, pmproxy, pmlogger) via init.d          |
| **valkey**| 6379  | Redis-compatible time-series backend for pmseries            |

The PCP container runs **all** PCP services internally — pmcd (44321 inside the container), pmproxy (44322 exposed to host), pmlogger, etc. No need for separate containers.

## Quick Start

```bash
cd dev-environment

# Start the stack (detached)
docker compose up -d

# Follow logs
docker compose logs -f

# Stop everything
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v
```

## Verifying the Stack

### 1. Check services are running

```bash
docker compose ps
```

### 2. Create a pmproxy context

```bash
curl -s http://localhost:44322/pmapi/context | python3 -m json.tool
```

You should get a JSON response with a `context` number.

### 3. Fetch a metric value

```bash
# Grab a context first
CTX=$(curl -s http://localhost:44322/pmapi/context | python3 -c "import sys,json; print(json.load(sys.stdin)['context'])")

# Fetch load average
curl -s "http://localhost:44322/pmapi/fetch?context=${CTX}&names=kernel.all.load" | python3 -m json.tool
```

### 4. Browse the metric namespace

```bash
curl -s "http://localhost:44322/pmapi/children?prefix=kernel" | python3 -m json.tool
```

### 5. Verify Valkey is responding

```bash
docker compose exec valkey valkey-cli ping
# Expected: PONG
```

## Troubleshooting

### "Connection refused" on port 44322

Give the stack a moment — the PCP container needs time to start all services via init.d. Watch progress with:

```bash
docker compose logs -f pcp
```

### PCP container won't start

The PCP container needs `privileged: true` to access kernel metrics. This is set in the compose file.

### Valkey data persistence

Valkey data is stored in the `valkey-data` named volume. Use `docker compose down -v` to wipe it for a fresh start.

### Port conflicts

If ports 44322 or 6379 are already in use on your host, either stop the conflicting service or edit the host-side port mappings in `docker-compose.yml`.
