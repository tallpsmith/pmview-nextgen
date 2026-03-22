# Dev-Environment Fleet Profile Support Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the dev-environment pipeline to support pmlogsynth fleet profiles alongside regular single-host profiles, so dropping a `fleet-*.yaml` file into `profiles/` Just Works.

**Architecture:** The generator entrypoint detects fleet profiles by filename prefix (`fleet-*`) and dispatches them via `pmlogsynth fleet` instead of bare `pmlogsynth`. Fleet archives land in a nested directory structure (one archive triplet per host), so the seeder must walk deeper to find all archives. No changes to the Dockerfile beyond ensuring the installed pmlogsynth version has fleet support.

**Tech Stack:** Bash (entrypoint scripts), Docker Compose YAML, pmlogsynth CLI

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `dev-environment/generator/entrypoint.sh` | Modify | Detect `fleet-*` prefix, dispatch to correct pmlogsynth subcommand |
| `dev-environment/docker-compose.yml` | Modify | Update seeder command to walk nested fleet archive directories |
| `dev-environment/profiles/fleet-5-node-cpu-disk-smashed.yaml` | Create | Sample fleet profile (already exists in main, copy to this branch) |
| `dev-environment/README.md` | Modify | Document fleet profile support and naming convention |

---

## Chunk 1: Generator — Fleet-Aware Dispatch

### Task 1: Update generator entrypoint to handle fleet profiles

The key change: files matching `fleet-*.yml` or `fleet-*.yaml` get routed to `pmlogsynth fleet`, everything else uses the existing bare `pmlogsynth` path.

**Files:**
- Modify: `dev-environment/generator/entrypoint.sh`

- [ ] **Step 1: Read the current entrypoint.sh**

Familiarise with the existing loop structure (lines 1-18).

- [ ] **Step 2: Rewrite entrypoint.sh with fleet dispatch**

Replace the contents with:

```bash
#!/usr/bin/env bash
set -euo pipefail

for profile in /profiles/*.yml /profiles/*.yaml; do
    [[ -f "${profile}" ]] || continue
    stem=$(basename "${profile}")
    stem="${stem%.yml}"
    stem="${stem%.yaml}"

    # Fleet profiles use the `fleet` subcommand and produce a directory of archives
    if [[ "${stem}" == fleet-* ]]; then
        echo "INFO: generating fleet archives for ${profile} → /archives/${stem}/"
        if ! pmlogsynth fleet --seed 42 -o "/archives/${stem}" "${profile}"; then
            echo "ERROR: pmlogsynth fleet failed for ${profile}"
            exit 1
        fi
        echo "INFO: fleet ${stem} complete"
    else
        mkdir -p "/archives/${stem}"
        echo "INFO: generating archive for ${profile} → /archives/${stem}/${stem}"
        if ! pmlogsynth -o "/archives/${stem}/${stem}" "${profile}"; then
            echo "ERROR: pmlogsynth failed for ${profile}"
            exit 1
        fi
        echo "INFO: archive ${stem} complete"
    fi
done

echo "INFO: all profiles generated successfully"
```

Key decisions:
- `--seed 42` for deterministic host assignment across rebuilds
- Fleet output dir is `/archives/${stem}/` — pmlogsynth fleet creates the archive triplets directly inside it
- Regular profiles keep the existing `mkdir -p` + nested path pattern

- [ ] **Step 3: Verify entrypoint.sh is syntactically valid**

Run: `bash -n dev-environment/generator/entrypoint.sh`
Expected: no output (clean parse)

- [ ] **Step 4: Commit**

```bash
git add dev-environment/generator/entrypoint.sh
git commit -m "Support fleet profiles in generator entrypoint

Detect fleet-* prefix and dispatch to 'pmlogsynth fleet' subcommand.
Regular profiles continue through the existing path unchanged."
```

---

### Task 2: Add the sample fleet profile

**Files:**
- Create: `dev-environment/profiles/fleet-5-node-cpu-disk-smashed.yaml`

- [ ] **Step 1: Copy the fleet profile from main worktree**

The file already exists in the main worktree at `dev-environment/profiles/fleet-5-node-cpu-disk-smashed.yaml`. Copy it to this branch.

- [ ] **Step 2: Verify the YAML is valid**

Run: `python3 -c "import yaml; yaml.safe_load(open('dev-environment/profiles/fleet-5-node-cpu-disk-smashed.yaml'))"`
Expected: no errors

- [ ] **Step 3: Commit**

```bash
git add dev-environment/profiles/fleet-5-node-cpu-disk-smashed.yaml
git commit -m "Add sample fleet profile for 5-node CPU/disk stress scenario"
```

---

## Chunk 2: Seeder — Walk Fleet Archive Directories

### Task 3: Update the seeder to handle nested fleet archives

The current seeder assumes each `/archives/*/` directory contains a single archive named `${stem}/${stem}`. Fleet directories contain multiple archive triplets (one per host) at the top level. The seeder needs to discover and load each archive individually.

The strategy: use the same `fleet-*` prefix detection as the generator for consistency. Fleet directories contain multiple `.0` data files (one per host); iterate each and load individually. Non-fleet directories use the existing single-archive path.

**Files:**
- Modify: `dev-environment/docker-compose.yml` (seeder command block, lines 43-66)

- [ ] **Step 1: Read the current seeder command block**

Review lines 43-66 of `docker-compose.yml`.

- [ ] **Step 2: Rewrite the seeder command**

Replace the seeder `command` block with:

```yaml
    command:
      - bash
      - -c
      - |
        set -euo pipefail;

        load_archive() {
            local archive_path=$$1;
            local label=$$2;

            echo "INFO: --- $${label} ---";
            echo "INFO: archive contents:";
            ls -lh "$$(dirname "$${archive_path}")/" | head -20;

            echo "INFO: archive bounds:";
            pmdumplog -l "$${archive_path}" 2>&1;

            echo "INFO: loading $${label} into pmseries";
            if ! pmseries --load "$${archive_path}"; then
                echo "ERROR: pmseries --load failed for $${label}";
                return 1;
            fi;

            series_count=$$(pmseries 'kernel.all.cpu.user' 2>/dev/null | wc -l || echo 0);
            echo "INFO: $${label} loaded — $${series_count} kernel.all.cpu.user series now in index";
        };

        for archive_dir in /archives/*/; do
            stem=$$(basename "$$archive_dir");

            if [[ "$${stem}" == fleet-* ]]; then
                echo "INFO: === Fleet: $${stem} ===";
                for data_file in "$${archive_dir}"*.0; do
                    [[ -f "$${data_file}" ]] || continue;
                    archive_path="$${data_file%.0}";
                    host_name=$$(basename "$${archive_path}");
                    load_archive "$${archive_path}" "$${stem}/$${host_name}" || exit 1;
                done;
            else
                load_archive "/archives/$${stem}/$${stem}" "$${stem}" || exit 1;
            fi;
        done;

        echo "INFO: all archives seeded";
```

Key decisions:
- Fleet detection via `fleet-*` prefix on the stem name — consistent with the generator's detection logic
- `*.0` glob finds PCP archive data files; strip `.0` suffix to get the archive basename that `pmseries --load` expects
- Extracted `load_archive` function avoids duplicating the dump/load/verify logic
- Per-archive `ls -lh` and series count preserved for debuggability

- [ ] **Step 3: Validate the compose file**

Run: `docker compose -f dev-environment/docker-compose.yml config --quiet` (or `podman compose`)
Expected: no errors

- [ ] **Step 4: Commit**

```bash
git add dev-environment/docker-compose.yml
git commit -m "Seeder walks fleet archive directories

Detect fleet-* prefix, iterate per-host .0 archives within.
Single-host archives continue through existing path unchanged."
```

---

## Chunk 3: Documentation

### Task 4: Update README with fleet profile documentation

**Files:**
- Modify: `dev-environment/README.md`

- [ ] **Step 1: Read the current README**

Review existing sections, especially "Adding Profiles" (line 47-51).

- [ ] **Step 2: Update the "Adding Profiles" section**

Replace the "Adding Profiles" section (lines 47-51 of README.md) and everything up to the next `##` heading with expanded content documenting the two profile types. The replacement text should contain:

1. The existing intro paragraph about dropping YAML files into `profiles/`
2. A new `### Single-Host Profiles` subsection explaining non-`fleet-` files use `pmlogsynth -o /archives/<stem>/<stem> <profile>`, with the existing saas-diurnal-week description
3. A new `### Fleet Profiles` subsection explaining `fleet-` prefixed files use `pmlogsynth fleet --seed 42 -o /archives/<stem> <profile>`, that they produce multiple PCP archives per host plus a `fleet.manifest`, and referencing the included `fleet-5-node-cpu-disk-smashed.yaml` example

Include inline code spans for the commands (not fenced code blocks, to avoid nesting issues in this plan document). The actual README should use fenced code blocks.

- [ ] **Step 3: Commit**

```bash
git add dev-environment/README.md
git commit -m "Document fleet profile support in dev-environment README"
```

---

## Chunk 4: Smoke Test

### Task 5: End-to-end smoke test with podman compose

This task is manual — run it on the host where podman is available.

- [ ] **Step 1: Tear down any existing stack**

```bash
cd dev-environment
podman compose down -v
```

- [ ] **Step 2: Rebuild and start the stack**

```bash
podman compose build --no-cache pmlogsynth-generator
podman compose up -d
```

- [ ] **Step 3: Watch the generator logs**

```bash
podman compose logs -f pmlogsynth-generator
```

Expected output should show both profile types:
- `INFO: generating archive for /profiles/saas-diurnal-week.yml → /archives/saas-diurnal-week/saas-diurnal-week`
- `INFO: generating fleet archives for /profiles/fleet-5-node-cpu-disk-smashed.yaml → /archives/fleet-5-node-cpu-disk-smashed/`

- [ ] **Step 4: Watch the seeder logs**

```bash
podman compose logs -f pmlogsynth-seeder
```

Expected: should load 1 single-host archive + 5 fleet host archives (6 total), each showing archive bounds and successful pmseries load.

- [ ] **Step 5: Verify pmseries has multiple hosts**

```bash
podman compose exec pcp pmseries 'kernel.all.cpu.user'
```

Expected: should return 6+ series IDs (1 from saas-diurnal-week + 5 from fleet).

- [ ] **Step 6: Query a fleet host by hostname**

```bash
podman compose exec pcp pmseries -l 'kernel.all.cpu.user{hostname=="prod-01"}'
```

Expected: should return series metadata for host `prod-01` from the fleet profile.

- [ ] **Step 7: Commit any final adjustments**

If the smoke test reveals issues, fix and commit before declaring victory.
