# GitHub Release Workflow Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automate building and packaging the pmview Godot app for macOS and Linux, triggered by GitHub Release creation.

**Architecture:** Single `release.yml` workflow with a matrix strategy across three platform targets. Version derived from git tag. Godot headless export produces platform binaries, packaged as `.dmg` (macOS) or `.tar.gz` (Linux) and uploaded to the GitHub Release.

**Tech Stack:** GitHub Actions, Godot 4.6.1 (.NET), chickensoft-games/setup-godot@v2, hdiutil (macOS), gh CLI

**Spec:** `docs/superpowers/specs/2026-03-19-github-release-workflow-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| Create | `.github/workflows/release.yml` | Release workflow |
| Modify | `src/pmview-app/export_presets.cfg` | Add Linux ARM64 preset |
| Modify | `README.md` | Add Download/Installation section |
| N/A | GitHub Issues (3) | Track deferred work |

---

## Chunk 1: Export Presets & Release Workflow

### Task 1: Add Linux ARM64 export preset

**Files:**
- Modify: `src/pmview-app/export_presets.cfg` (append after line 313)

- [ ] **Step 1: Add the Linux ARM64 preset**

Append a new `[preset.2]` section to `export_presets.cfg`. This is a copy of the existing `Linux` preset (`preset.1`) with two changes: `name="Linux ARM64"` and `binary_format/architecture="arm64"`.

```ini
[preset.2]

name="Linux ARM64"
platform="Linux"
runnable=true
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path=""
patches=PackedStringArray()
patch_delta_encoding=false
patch_delta_compression_level_zstd=19
patch_delta_min_reduction=0.1
patch_delta_include_filters="*"
patch_delta_exclude_filters=""
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.2.options]

custom_template/debug=""
custom_template/release=""
debug/export_console_wrapper=1
binary_format/embed_pck=false
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
shader_baker/enabled=false
binary_format/architecture="arm64"
ssh_remote_deploy/enabled=false

ssh_remote_deploy/host="user@host_ip"
ssh_remote_deploy/port="22"
ssh_remote_deploy/extra_args_ssh=""
ssh_remote_deploy/extra_args_scp=""
ssh_remote_deploy/run_script="#!/usr/bin/env bash
export DISPLAY=:0
unzip -o -q \"{temp_dir}/{archive_name}\" -d \"{temp_dir}\"
\"{temp_dir}/{exe_name}\" {cmd_args}"
ssh_remote_deploy/cleanup_script="#!/usr/bin/env bash
pkill -x -f \"{temp_dir}/{exe_name} {cmd_args}\"
rm -rf \"{temp_dir}\""
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=true
dotnet/embed_build_outputs=false
```

- [ ] **Step 2: Verify the preset file is valid**

Open `src/pmview-app/` in Godot on your Mac. Check Project → Export shows three presets: `macOS`, `Linux`, `Linux ARM64`. The ARM64 preset should show `arm64` architecture. Close Godot.

> **Note:** This step requires the host Godot editor — cannot be verified in CI/headless. If Godot rewrites the file with additional fields, commit those changes too.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/export_presets.cfg
git commit -m "Add Linux ARM64 export preset for release workflow"
```

---

### Task 2: Create the release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create the release workflow file**

```yaml
name: Release

# TODO: Version is not yet injected into the Godot app's internal
# application/version fields (macOS Info.plist will show empty version).
# See spec for details on this known v1 limitation.

on:
  release:
    types: [published]

permissions:
  contents: write

jobs:
  validate-tag:
    name: Validate Tag
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.version.outputs.version }}
    steps:
      - name: Extract and validate version from tag
        id: version
        run: |
          TAG="${GITHUB_REF_NAME}"
          VERSION="${TAG#v}"
          if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
            echo "::error::Tag '$TAG' is not valid semver (expected v1.2.3 or v1.2.3-beta.1)"
            exit 1
          fi
          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "Validated version: $VERSION"

  build-and-upload:
    name: Build ${{ matrix.platform }}
    needs: validate-tag
    runs-on: ${{ matrix.runner }}
    strategy:
      fail-fast: false
      matrix:
        include:
          - runner: macos-latest
            platform: macOS
            preset: macOS
            artifact_suffix: macos-universal.dmg
          - runner: ubuntu-latest
            platform: Linux x64
            preset: Linux
            artifact_suffix: linux-x64.tar.gz
          - runner: ubuntu-latest
            platform: Linux ARM64
            preset: Linux ARM64
            artifact_suffix: linux-arm64.tar.gz
    env:
      VERSION: ${{ needs.validate-tag.outputs.version }}

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Setup Godot
        uses: chickensoft-games/setup-godot@v2
        with:
          version: 4.6.1
          use-dotnet: true
          include-templates: true

      - name: Compute artifact name
        run: echo "ARTIFACT=pmview-${VERSION}-${{ matrix.artifact_suffix }}" >> "$GITHUB_ENV"

      - name: Restore .NET dependencies
        run: dotnet restore src/pmview-app/pmview-app.csproj

      - name: Build .NET assemblies
        run: dotnet build src/pmview-app/pmview-app.csproj --no-restore -c Release

      - name: Export Godot project
        run: |
          mkdir -p build
          godot --headless --path src/pmview-app --export-release "${{ matrix.preset }}" "../../build/pmview"
          echo "--- Exported files ---"
          ls -la build/

      - name: Package macOS (.dmg)
        if: matrix.preset == 'macOS'
        run: |
          if [ ! -d build/pmview.app ]; then
            echo "::error::Expected build/pmview.app but it does not exist"
            ls -la build/
            exit 1
          fi
          hdiutil create \
            -volname "pmview" \
            -srcfolder build/pmview.app \
            -ov -format UDZO \
            "build/${ARTIFACT}"

      - name: Package Linux (.tar.gz)
        if: matrix.preset != 'macOS'
        run: |
          cd build
          # Find the exported binary (name varies by arch suffix)
          BINARY=$(find . -maxdepth 1 -type f -executable ! -name "*.pck" | head -1)
          if [ -z "$BINARY" ]; then
            echo "::error::No exported binary found in build/"
            ls -la
            exit 1
          fi
          tar czf "${ARTIFACT}" \
            "$(basename "$BINARY")" \
            pmview.pck

      - name: Upload to GitHub Release
        run: |
          gh release upload "${{ github.event.release.tag_name }}" \
            "build/${ARTIFACT}" \
            --clobber
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

- [ ] **Step 2: Validate the workflow YAML syntax**

```bash
# Requires actionlint installed, or use: npx yaml-lint
cd /Volumes/My\ Shared\ Files/pmview-nextgen
cat .github/workflows/release.yml | python3 -c "import sys,yaml; yaml.safe_load(sys.stdin.read()); print('YAML valid')"
```

Expected: `YAML valid`

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add GitHub Release workflow for automated Godot app packaging

Builds macOS universal (.dmg) and Linux x64/arm64 (.tar.gz)
artifacts on Release publish. Version derived from git tag."
```

---

## Chunk 2: Documentation & GitHub Issues

### Task 3: Update README with download instructions

**Files:**
- Modify: `README.md` (insert new section after "Overview", before "The Vision")

- [ ] **Step 1: Add Download section to README**

Insert a new `## Download` section between `## Overview` and `## The Vision`:

```markdown
## Download

Pre-built binaries are available from the [latest GitHub Release](https://github.com/tallpsmith/pmview-nextgen/releases/latest).

| Platform | Architecture | Download |
|----------|-------------|----------|
| macOS | Universal (Intel + Apple Silicon) | `pmview-<version>-macos-universal.dmg` |
| Linux | x86_64 | `pmview-<version>-linux-x64.tar.gz` |
| Linux | ARM64 | `pmview-<version>-linux-arm64.tar.gz` |

### macOS

1. Download the `.dmg` file
2. Mount it (double-click)
3. Drag `pmview.app` to your Applications folder (or wherever you like)
4. **First launch:** macOS will show an "unidentified developer" warning. Right-click the app → Open, then click Open in the dialog. This is only needed once.

### Linux

1. Download the `.tar.gz` for your architecture
2. Extract: `tar xzf pmview-<version>-linux-<arch>.tar.gz`
3. Run the binary:
   - **x86_64:** `chmod +x pmview.x86_64 && ./pmview.x86_64`
   - **ARM64:** `chmod +x pmview.arm64 && ./pmview.arm64`

### Build from source

If you have the .NET 10+ SDK and Godot 4.6+ installed, you can build from source — see [Quick Start](#quick-start) below.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "Add download instructions to README

Documents how to get pre-built binaries from GitHub Releases
with platform-specific installation steps."
```

---

### Task 4: Create GitHub issues for deferred work

No files modified — these are created via `gh` CLI.

- [ ] **Step 1: Create CLI binary distribution issue**

```bash
gh issue create \
  --title "Add CLI (pmview-host-projector) binaries to release workflow" \
  --body "$(cat <<'EOF'
## Context

The release workflow currently ships the Godot app (pmview-app) only. The Host Projector CLI (`pmview-host-projector`) is a standalone .NET tool that generates Godot scenes from pmproxy topology.

## Task

Add self-contained `dotnet publish` builds of pmview-host-projector to the release workflow as additional artifacts:

- `pmview-cli-<ver>-macos-arm64.tar.gz`
- `pmview-cli-<ver>-linux-x64.tar.gz`
- `pmview-cli-<ver>-linux-arm64.tar.gz`

This is a separate matrix job (or additional matrix entries) using `dotnet publish -c Release -r <rid> --self-contained`.

## Reference

- Design spec: `docs/superpowers/specs/2026-03-19-github-release-workflow-design.md`
EOF
)" \
  --label "enhancement"
```

- [ ] **Step 2: Create macOS code signing issue**

```bash
gh issue create \
  --title "macOS code signing and notarization for release builds" \
  --body "$(cat <<'EOF'
## Context

The macOS release build is currently ad-hoc signed (Godot export preset `codesign/codesign=3`). Users see a Gatekeeper "unidentified developer" warning on first launch and must right-click → Open to bypass.

## Task

Properly sign and notarize the macOS `.dmg` and `.app` bundle:

1. Obtain an Apple Developer account ($99/year)
2. Create a Developer ID Application certificate
3. Store certificate + password as GitHub Actions secrets
4. Add codesign + notarization steps to the release workflow
5. Update the export preset `codesign/codesign` value as needed

## Reference

- Design spec: `docs/superpowers/specs/2026-03-19-github-release-workflow-design.md`
- Apple notarization docs: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution
EOF
)" \
  --label "enhancement"
```

- [ ] **Step 3: Create .NET framework unification issue**

```bash
gh issue create \
  --title "Evaluate unifying .NET target frameworks to net8.0" \
  --body "$(cat <<'EOF'
## Context

Libraries consumed by Godot (PcpClient, PcpGodotBridge, PmviewProjectionCore) target `net8.0` because Godot.NET.Sdk 4.6.1 pins to it. The CLI (PmviewHostProjector) and all test projects target `net10.0`, but analysis shows no .NET 10-specific APIs or C# 13+ language features are used — only C# 12 collection expressions which are available in net8.0.

## Task

Evaluate and implement moving all projects to a consistent `net8.0` target:

1. Verify no net10.0-specific APIs are used (initial analysis says no)
2. Update PmviewHostProjector.csproj to target net8.0
3. Update all test .csproj files to target net8.0
4. Update CI workflows (.NET SDK version)
5. Update CLAUDE.md framework documentation
6. Ensure all tests pass

## Trade-offs

- **Pro:** Consistent framework, simpler CI, single SDK requirement
- **Con:** Forgo access to newer C# language features and .NET APIs
- **Alternative:** Wait for Godot to support a newer .NET version, then unify upward

## Reference

- Design spec: `docs/superpowers/specs/2026-03-19-github-release-workflow-design.md`
EOF
)" \
  --label "enhancement"
```

- [ ] **Step 4: Commit (nothing to commit — issues are in GitHub, not files)**

No commit needed. Note the issue numbers for reference.

---

## Chunk 3: Smoke Test

### Task 5: Dry-run the release workflow

This task cannot be fully automated — it requires creating a GitHub Release. However, the workflow can be partially validated.

- [ ] **Step 1: Push the branch with the workflow**

Ensure all commits from Tasks 1-3 are pushed to the branch.

- [ ] **Step 2: Create a pre-release to test**

In the GitHub UI:
1. Go to Releases → "Draft a new release"
2. Create tag `v0.2.0-rc.1` (or appropriate pre-release tag)
3. Check "Set as a pre-release"
4. Publish

- [ ] **Step 3: Monitor the workflow run**

```bash
gh run list --workflow=release.yml --limit 1
# Get the run ID, then watch it:
gh run watch <run-id>
```

- [ ] **Step 4: Verify artifacts on the release**

```bash
gh release view v0.2.0-rc.1 --json assets --jq '.assets[].name'
```

Expected output (3 artifacts):
```
pmview-0.2.0-rc.1-macos-universal.dmg
pmview-0.2.0-rc.1-linux-x64.tar.gz
pmview-0.2.0-rc.1-linux-arm64.tar.gz
```

- [ ] **Step 5: Download and test the macOS artifact**

Download the `.dmg`, mount it, verify the `.app` launches on your Mac.

- [ ] **Step 6: If all good, delete the pre-release tag**

```bash
gh release delete v0.2.0-rc.1 --yes
git push origin --delete v0.2.0-rc.1
```

Or keep it — your call. Pre-releases don't show as "latest".
