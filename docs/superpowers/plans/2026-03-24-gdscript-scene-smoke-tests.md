# GDScript Scene Smoke Tests — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Catch GDScript parser errors and broken scene wiring in CI before they reach manual testing, starting with a headless scene loader and building the DLL pipeline into the E2E tier.

**Architecture:** A standalone GDScript script runs under `godot --headless`, loading and instantiating every pmview-app scene. This forces GDScript parsing and resource resolution. CI builds the addon DLLs from source and copies them into the app before running the smoke tests — validating the real artifact chain. The smoke tests run as an early gate within Tier 3 (E2E), before the PCP stack spins up.

**Tech Stack:** GDScript (Godot 4.6), GitHub Actions, chickensoft-games/setup-godot, dotnet build

**Design note:** Issue #65 suggests a separate "Tier 2.5" CI job. This plan deliberately places scene smoke tests as an early gate *within* the existing Tier 3 job instead — avoids duplicating checkout/setup overhead, and naturally fast-fails before the PCP stack spins up.

**Issue:** [#65 — Add GDScript scene-level e2e tests to catch parser/wiring errors](https://github.com/tallpsmith/pmview-nextgen/issues/65)

---

## File Map

| Action | Path | Purpose |
|--------|------|---------|
| Create | `src/pmview-app/tests/smoke_test_scenes.gd` | Headless scene loader — loads + instantiates every .tscn |
| Modify | `.github/workflows/ci.yml` | Add addon build, addon install, and scene smoke test steps to Tier 3 |

---

## Chunk 1: Headless Scene Smoke Test Script

### Task 1: Create the headless scene loader script

The script extends `SceneTree` so that autoloads from `project.godot` are registered (e.g. `SceneManager`). It loads and instantiates each scene, catching parser errors, missing class_name references, and broken resource paths.

**Important lifecycle note:** `_init()` runs during construction — before the tree is ready. We use `_initialize()` instead, which `SceneTree` calls after the tree and autoloads are fully set up. This gives us access to `root` and allows `await`.

**Files:**
- Create: `src/pmview-app/tests/smoke_test_scenes.gd`

- [ ] **Step 1: Create the smoke test script**

```gdscript
## Headless scene smoke test — validates every app scene can load and instantiate.
##
## Run locally:
##   godot --headless --quit --path src/pmview-app --script tests/smoke_test_scenes.gd
##
## Run in CI: see .github/workflows/ci.yml (Tier 3, scene smoke test step)
##
## Extends SceneTree so that project.godot autoloads (SceneManager, PmviewLogger)
## are registered before scenes are instantiated. Uses _initialize() (not _init())
## because the tree and autoloads aren't ready during construction.
##
## This catches the same class of errors you'd hit when launching the app: parser
## errors, missing class_name references, broken resource paths, and autoload
## dependency issues in _ready().

extends SceneTree

## Scenes to validate. Every .tscn in scenes/ should be listed here.
## If you add a new scene, add it to this list — the test will remind you if you forget.
const SCENE_PATHS: Array[String] = [
	"res://scenes/main_menu.tscn",
	"res://scenes/loading.tscn",
	"res://scenes/fleet_view.tscn",
	"res://scenes/host_view.tscn",
	"res://scenes/time_control.tscn",
]

## Directory to scan for unlisted scenes (top-level only).
const SCENES_DIR := "res://scenes/"


func _initialize() -> void:
	var exit_code := 0
	var tested := 0
	var failed := 0

	print("\n=== Scene Smoke Tests ===\n")

	# Phase 1: Load and instantiate each listed scene
	for scene_path in SCENE_PATHS:
		tested += 1
		var result := await _test_scene(scene_path)
		if not result:
			failed += 1
			exit_code = 1

	# Phase 2: Check for unlisted scenes (new scenes someone forgot to add)
	var unlisted := _find_unlisted_scenes()
	if unlisted.size() > 0:
		print("\nWARNING: Found scenes not in SCENE_PATHS — add them:")
		for path in unlisted:
			print("  - %s" % path)
		# Treat unlisted scenes as a failure so CI catches missing entries
		exit_code = 1

	# Summary
	print("\n=== Results: %d tested, %d failed ===" % [tested, failed])
	if unlisted.size() > 0:
		print("=== %d unlisted scenes (add to SCENE_PATHS) ===" % unlisted.size())

	if exit_code == 0:
		print("=== ALL PASSED ===\n")
	else:
		print("=== FAILURES DETECTED ===\n")

	quit(exit_code)


func _test_scene(scene_path: String) -> bool:
	# Step 1: Load the packed scene (triggers GDScript parsing for attached scripts)
	var packed: PackedScene = load(scene_path) as PackedScene
	if packed == null:
		printerr("FAIL [load]: %s — could not load scene resource" % scene_path)
		return false

	# Step 2: Instantiate (creates nodes, resolves class_name refs, wires scripts)
	var instance: Node = packed.instantiate()
	if instance == null:
		printerr("FAIL [instantiate]: %s — instantiate() returned null" % scene_path)
		return false

	# Step 3: Add to tree to trigger _ready() — catches autoload and wiring errors
	root.add_child(instance)

	# Give one frame for deferred calls to settle
	await process_frame

	# Cleanup
	instance.queue_free()
	await process_frame

	print("PASS: %s" % scene_path)
	return true


func _find_unlisted_scenes() -> Array[String]:
	var unlisted: Array[String] = []
	var dir := DirAccess.open(SCENES_DIR)
	if dir == null:
		printerr("WARNING: Could not open %s to scan for unlisted scenes" % SCENES_DIR)
		return unlisted

	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if file_name.ends_with(".tscn"):
			var full_path := SCENES_DIR + file_name
			if full_path not in SCENE_PATHS:
				unlisted.append(full_path)
		file_name = dir.get_next()
	dir.list_dir_end()
	return unlisted
```

- [ ] **Step 2: Verify the script parses cleanly (local, requires Godot)**

Run locally to confirm no syntax errors:
```bash
cd src/pmview-app
godot --headless --quit --script tests/smoke_test_scenes.gd
```

Expected: All 5 scenes print `PASS`, exit code 0. If any fail, the scene itself has a real problem — fix it before proceeding.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/tests/smoke_test_scenes.gd
git commit -m "Add headless GDScript scene smoke test script

Loads and instantiates every pmview-app scene under godot --headless,
catching parser errors, missing class_name refs, and broken wiring
that previously only surfaced during manual testing. (issue #65)"
```

---

## Chunk 2: CI Pipeline — Build Addon DLLs and Run Scene Smoke Tests

### Task 2: Add addon build + install steps to Tier 3

The release workflow already copies the addon into pmview-app. We need the same in CI, but with an improvement: **build the DLLs from source** rather than using pre-built copies. This validates the real artifact chain.

**Files:**
- Modify: `.github/workflows/ci.yml` (e2e-tests job)

- [ ] **Step 1: Add setup-godot, addon copy, and DLL build steps**

Insert these steps in the `e2e-tests` job, **after** checkout and **before** the PCP stack startup. The scene smoke tests don't need pmproxy, so they run as an early fast-fail gate.

Add to `.github/workflows/ci.yml` in the `e2e-tests` job, after the `actions/checkout@v4` step:

```yaml
      - name: Setup Godot (for scene smoke tests)
        uses: chickensoft-games/setup-godot@v2
        with:
          version: 4.6.1
          use-dotnet: true

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      # Mirror the release workflow's addon install — but build DLLs from source
      # to validate the real artifact chain (source → DLL → addon → app scenes).
      - name: Build addon DLLs from source
        run: |
          dotnet build src/pcp-godot-bridge/src/PcpGodotBridge/PcpGodotBridge.csproj -c Release

      - name: Install addon into pmview-app
        run: |
          mkdir -p src/pmview-app/addons
          cp -r src/pmview-bridge-addon/addons/pmview-bridge src/pmview-app/addons/
          # Replace pre-built DLLs with freshly-built ones from bin output.
          # PcpGodotBridge depends on PcpClient + Tomlyn, so building it
          # produces all three DLLs in its output directory.
          BRIDGE_BIN=src/pcp-godot-bridge/src/PcpGodotBridge/bin/Release/net8.0
          cp "$BRIDGE_BIN"/PcpGodotBridge.dll src/pmview-app/addons/pmview-bridge/lib/
          cp "$BRIDGE_BIN"/Tomlyn.dll src/pmview-app/addons/pmview-bridge/lib/
          # Remove PcpClient.dll — it conflicts with the copy resolved via
          # PmviewProjectionCore's transitive ProjectReference in pmview-app.csproj.
          # (Same approach as release.yml)
          rm -f src/pmview-app/addons/pmview-bridge/lib/PcpClient.dll
          echo "Addon installed with freshly-built DLLs:"
          ls -la src/pmview-app/addons/pmview-bridge/lib/

      - name: Build pmview-app .NET assemblies
        run: dotnet build src/pmview-app/pmview-app.csproj -c Release
```

- [ ] **Step 2: Review — does setup-dotnet need to move?**

The existing `e2e-tests` job already has `setup-dotnet` further down (before PcpClient integration tests). We're adding it earlier because the DLL build needs it before the PCP stack. The second `setup-dotnet` call is a no-op (already installed), so it's harmless — but we should remove the duplicate to keep things clean.

Remove the second `setup-dotnet` step that currently sits between "Wait for PCP" and "PcpClient integration tests".

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "Build addon DLLs from source in CI Tier 3

Validates the real artifact chain (source → DLL → addon → app) rather
than relying on pre-built DLLs. Mirrors the release workflow's addon
install but with freshly-compiled binaries. (issue #65)"
```

---

### Task 3: Add scene smoke test step to Tier 3

**Files:**
- Modify: `.github/workflows/ci.yml` (e2e-tests job)

- [ ] **Step 1: Add the scene smoke test step**

Insert this step after "Build pmview-app .NET assemblies" and before "Start PCP stack":

```yaml
      - name: Scene smoke tests (GDScript parser + wiring validation)
        run: |
          godot --headless --quit --path src/pmview-app \
            --script tests/smoke_test_scenes.gd
        timeout-minutes: 3
```

- [ ] **Step 2: Verify the full Tier 3 job ordering makes sense**

The final step order for `e2e-tests` should be:

1. `actions/checkout@v4`
2. `chickensoft-games/setup-godot@v2` (Godot 4.6.1 + .NET)
3. `actions/setup-dotnet@v4` (dotnet 8.0.x)
4. Build addon DLLs from source
5. Install addon into pmview-app
6. Build pmview-app .NET assemblies
7. **Scene smoke tests** ← fast-fail gate, no PCP needed
8. Start PCP stack
9. Wait for PCP
10. PcpClient integration tests
11. Godot E2E tests (gdUnit4)
12. Dump logs on failure
13. Tear down PCP stack

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "Add GDScript scene smoke tests to CI Tier 3

Runs headless Godot to load+instantiate every pmview-app scene before
the PCP stack starts. Fast-fail gate catches parser errors, missing
class_name refs, and broken resource paths. (issue #65)"
```

---

## Chunk 3: Housekeeping and Issue Tracking

### Task 4: Update GitHub issue #65 with progress

- [ ] **Step 1: Comment on issue #65 with completed work**

```bash
gh issue comment 65 --repo tallpsmith/pmview-nextgen --body "$(cat <<'EOF'
## Progress Update

### Shipped (Phase 1 — Headless Scene Loader)

- **Headless scene smoke test script** (`src/pmview-app/tests/smoke_test_scenes.gd`)
  - Loads + instantiates all 5 app scenes under `godot --headless`
  - Catches GDScript parser errors, missing `class_name` refs, broken resource paths
  - Auto-detects unlisted scenes (fails if new .tscn not added to test list)
  - Adds to scene tree to trigger `_ready()` — catches autoload/wiring errors

- **CI addon DLL build pipeline** (`.github/workflows/ci.yml`)
  - Builds PcpGodotBridge + Tomlyn DLLs from source in Tier 3
  - Copies addon + fresh DLLs into pmview-app (validates real artifact chain)
  - Builds pmview-app .NET assemblies

- **Scene smoke tests in CI Tier 3**
  - Runs as early fast-fail gate before PCP stack startup
  - `--quit` flag ensures Godot exits even on script crash
  - 3-minute timeout

### Remaining (Future Phases)

- [ ] **gdUnit4 GDScript scene tests** — richer assertions beyond "loads without error":
  - Verify key nodes exist and are the expected type
  - Verify signal connections are wired
  - Test scene-specific setup (e.g. CompactHosts spawn in fleet_view, patrol camera setup)
- [ ] **C#↔GDScript boundary tests** (Chickensoft GoDotTest + GodotTestDriver):
  - MetricPoller signals reaching GDScript handlers
  - SceneBinder wiring under live scene tree
  - FleetMetricPoller → CompactHost data flow
- [ ] **Local dev workflow** — document running smoke tests locally with `godot --headless`
EOF
)"
```

- [ ] **Step 2: Commit any remaining changes**

Verify no uncommitted changes remain. If the `.gitignore` needed updating for the `tests/` directory, it should already be fine — the existing `.gitignore` only ignores `addons/pmview-bridge/` and `.godot/`, so `tests/` is already tracked.

---

## Notes for Future Phases

These are explicitly **not in scope** for this plan, but documented for when we come back:

1. **gdUnit4 GDScript scene tests**: Would replace or augment the headless loader with proper assertions. Use `scene_runner()` to load scenes and query node properties. Lives in `src/pmview-app/tests/` alongside the smoke test.

2. **Chickensoft C# integration tests**: For testing the C#↔GDScript boundary. Requires GoDotTest + GodotTestDriver NuGet packages in a test project. Would catch signal propagation failures between the C# addon nodes and GDScript scene controllers.

3. **Scene-specific behavioural tests**: e.g. "fleet_view spawns N CompactHost nodes when given N hostnames", "host_view accepts a built_scene from SceneManager". These need mock data or the PCP stack running.
