# GitHub Release Workflow Design

**Date:** 2026-03-19
**Status:** Approved

## Overview

Automate building and packaging the pmview Godot application for macOS and Linux, triggered by GitHub Release creation. Self-contained executables — no runtime prerequisites for end users.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| What to ship | Godot app (`pmview-app`) only | CLI distribution deferred (tracked via GitHub issue) |
| Target platforms | `osx-arm64`, `linux-x64`, `linux-arm64` | Covers Apple Silicon, common Linux, and ARM Linux |
| Packaging | `.dmg` (macOS), `.tar.gz` (Linux) | Native format for each platform |
| Versioning | Git tag is single source of truth | Tag `v1.2.3` → version `1.2.3` injected at build time. No version stored in repo files |
| Code signing | Unsigned for v1 | Technical users can bypass Gatekeeper. Proper signing deferred (tracked via GitHub issue) |
| Self-contained vs framework-dependent | Self-contained | Users download and run — no .NET SDK needed |
| Workflow structure | Single `release.yml` with matrix strategy | One file, matrix handles platform parallelism |
| Godot CI setup | `chickensoft-games/setup-godot` action | Handles Godot + export template installation |
| .NET SDK | 8.0 only | All Godot-consumed projects target `net8.0`. CLI/tests use `net10.0` but aren't shipped here |
| Tests in release workflow | No | CI already gates on push/PR — YAGNI |

## Trigger

```yaml
on:
  release:
    types: [published]
```

Workflow fires when a Release is **published** in GitHub UI (not just tag push). This gives control over release notes, draft/pre-release status.

## Version Flow

1. Create Release in GitHub UI, set tag (e.g., `v0.2.0`)
2. Workflow extracts version from tag, strips `v` prefix
3. Validates semver format — fails fast on malformed tags
4. Version injected into artifact filenames

## Workflow Structure

```
on: release published (tag matches v*)
    │
    ├─ validate-tag job
    │   └─ Extract version, fail if not valid semver
    │
    └─ build-and-upload job (matrix, needs: validate-tag)
        ├─ macos-latest / macOS / osx-arm64
        ├─ ubuntu-latest / Linux x64 / linux-x64
        └─ ubuntu-latest / Linux ARM64 / linux-arm64

        Each leg:
        1. Checkout
        2. Setup .NET 8.0
        3. Setup Godot 4.6.1 (chickensoft-games/setup-godot)
        4. dotnet restore src/pmview-app
        5. godot --headless --path src/pmview-app --export-release "<preset>" <output>
        6. Package (.dmg or .tar.gz)
        7. Upload artifact to GitHub Release (gh release upload)
```

## Build Matrix

| Runner | Platform | Export Preset | Package Format | Artifact Name |
|--------|----------|---------------|----------------|---------------|
| `macos-latest` | macOS ARM64 | `macOS` | `.dmg` | `pmview-<ver>-macos-arm64.dmg` |
| `ubuntu-latest` | Linux x64 | `Linux x64` | `.tar.gz` | `pmview-<ver>-linux-x64.tar.gz` |
| `ubuntu-latest` | Linux ARM64 | `Linux ARM64` | `.tar.gz` | `pmview-<ver>-linux-arm64.tar.gz` |

## Packaging Details

### macOS (`.dmg`)

Godot exports a `.app` bundle. Wrapped in a `.dmg` via `hdiutil`:

```bash
hdiutil create -volname "pmview" -srcfolder pmview.app -ov -format UDZO pmview-<ver>-macos-arm64.dmg
```

Simple disk image — no background image or Applications symlink for v1. Users mount, drag, run.

**Unsigned** — users will see Gatekeeper "unidentified developer" warning. Bypass via right-click → Open or `xattr -cr pmview.app`.

### Linux (`.tar.gz`)

Godot exports a binary + `.pck` data file:

```bash
tar czf pmview-<ver>-linux-x64.tar.gz pmview.x86_64 pmview.pck
```

Users extract, `chmod +x`, run.

## Export Presets

Three presets configured in `src/pmview-app/export_presets.cfg` (already created):

- `macOS` — ARM64 architecture
- `Linux x64` — x86_64 architecture
- `Linux ARM64` — ARM64 architecture (cross-exported from x64 runner)

## Permissions

Workflow needs `contents: write` to upload release assets.

## Prerequisites

- Export presets committed to `src/pmview-app/export_presets.cfg` (done)
- Export templates installed in CI via `chickensoft-games/setup-godot`

## Deferred Work (GitHub Issues)

1. **CLI binary distribution** — Add `pmview-host-projector` self-contained executables to the release workflow
2. **macOS code signing & notarization** — Sign and notarize the `.dmg` with Apple Developer certificate ($99/year)
3. **Unify .NET target frameworks** — Evaluate consolidating CLI/test projects from `net10.0` to `net8.0`
