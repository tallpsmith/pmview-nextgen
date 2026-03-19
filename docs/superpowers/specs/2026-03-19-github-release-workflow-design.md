# GitHub Release Workflow Design

**Date:** 2026-03-19
**Status:** Approved

## Overview

Automate building and packaging the pmview Godot application for macOS and Linux, triggered by GitHub Release creation. Self-contained executables — no runtime prerequisites for end users.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| What to ship | Godot app (`pmview-app`) only | CLI distribution deferred (tracked via GitHub issue) |
| Target platforms | `osx-universal`, `linux-x64`, `linux-arm64` | Universal binary covers both Intel and Apple Silicon Macs; Linux covers x64 and ARM |
| Packaging | `.dmg` (macOS), `.tar.gz` (Linux) | Native format for each platform |
| Versioning | Git tag is single source of truth | Tag `v1.2.3` → version `1.2.3` injected at build time. No version stored in repo files |
| Code signing | Ad-hoc for v1 (Godot preset default) | Users get Gatekeeper "unidentified developer" warning. Proper signing deferred (GitHub issue) |
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
2. Workflow extracts version from tag: `VERSION=${GITHUB_REF_NAME#v}`
3. Validates semver format via regex — fails fast on malformed tags
4. Version injected into artifact filenames
5. **Known limitation (v1):** Version is not injected into the Godot app's internal `application/version` fields (Info.plist on macOS will show empty version). Addressing this is a future improvement — requires pre-processing `export_presets.cfg` or `project.godot` before export.

## Workflow Structure

```
on: release published (tag matches v*)
    │
    ├─ validate-tag job
    │   └─ Extract version, fail if not valid semver
    │
    └─ build-and-upload job (matrix, needs: validate-tag)
        ├─ macos-latest / macOS / osx-universal
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
| `macos-latest` | macOS Universal | `macOS` | `.dmg` | `pmview-<ver>-macos-universal.dmg` |
| `ubuntu-latest` | Linux x64 | `Linux` | `.tar.gz` | `pmview-<ver>-linux-x64.tar.gz` |
| `ubuntu-latest` | Linux ARM64 | `Linux ARM64` | `.tar.gz` | `pmview-<ver>-linux-arm64.tar.gz` |

## Packaging Details

### macOS (`.dmg`)

Godot exports a universal `.app` bundle (x86_64 + arm64). Wrapped in a `.dmg` via `hdiutil`:

```bash
hdiutil create -volname "pmview" -srcfolder pmview.app -ov -format UDZO pmview-<ver>-macos-universal.dmg
```

Simple disk image — no background image or Applications symlink for v1. Users mount, drag, run.

**Ad-hoc signed** (Godot preset `codesign/codesign=3`) — users will see Gatekeeper "unidentified developer" warning on first launch. Bypass via right-click → Open or `xattr -cr pmview.app`. The ad-hoc codesign setting should work in headless CI on macOS runners; if it causes issues, override to disabled (`codesign=0`) as a fallback.

### Linux (`.tar.gz`)

Godot exports a binary + `.pck` data file (`embed_pck=false` in preset). The workflow explicitly sets the output path to control filenames:

```bash
# x64
godot --headless --path src/pmview-app --export-release "Linux" build/pmview.x86_64
tar czf pmview-<ver>-linux-x64.tar.gz -C build pmview.x86_64 pmview.pck

# arm64
godot --headless --path src/pmview-app --export-release "Linux ARM64" build/pmview.arm64
tar czf pmview-<ver>-linux-arm64.tar.gz -C build pmview.arm64 pmview.pck
```

Users extract, `chmod +x`, run.

## Export Presets

Presets configured in `src/pmview-app/export_presets.cfg`:

- `macOS` — universal architecture (x86_64 + arm64), ad-hoc codesign **(exists)**
- `Linux` — x86_64 architecture **(exists)**
- `Linux ARM64` — arm64 architecture, cross-exported from x64 runner **(needs to be added)**

**Action required:** Add a `Linux ARM64` preset to `export_presets.cfg` before implementing the workflow. This is a copy of the `Linux` preset with `binary_format/architecture` changed to `arm64`.

## Permissions

Workflow needs `contents: write` to upload release assets.

## Prerequisites

- Export presets committed to `src/pmview-app/export_presets.cfg` (macOS + Linux x64 done; Linux ARM64 needs adding)
- Export templates installed in CI via `chickensoft-games/setup-godot`

## Documentation Deliverables

Per project guidelines, documentation ships with the code:

- **README.md** — Add a "Download" or "Installation" section with links to the latest GitHub Release, platform-specific download instructions, and macOS Gatekeeper bypass instructions
- **Release page** — GitHub auto-generated release notes from PR titles/commits (no custom tooling needed)

## Deferred Work (GitHub Issues)

1. **CLI binary distribution** — Add `pmview-host-projector` self-contained executables to the release workflow
2. **macOS code signing & notarization** — Sign and notarize the `.dmg` with Apple Developer certificate ($99/year)
3. **Unify .NET target frameworks** — Evaluate consolidating CLI/test projects from `net10.0` to `net8.0`
