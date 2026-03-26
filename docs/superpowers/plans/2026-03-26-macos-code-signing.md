# macOS Code Signing & Notarization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sign and notarize the macOS release build so users never see a Gatekeeper warning.

**Architecture:** Disable Godot's built-in signing, export an unsigned `.app`, then sign/notarize explicitly in the GitHub Actions workflow using `codesign` and `xcrun notarytool`. Secrets are scoped to a `release` environment.

**Tech Stack:** GitHub Actions, macOS `codesign`, `xcrun notarytool`, `xcrun stapler`, `hdiutil`

**Spec:** `docs/superpowers/specs/2026-03-26-macos-code-signing-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `src/pmview-app/export_presets.cfg` | Modify | Disable Godot ad-hoc signing (`codesign=0`) |
| `src/pmview-app/macos-entitlements.plist` | Create | Hardened Runtime entitlements for .NET/Godot |
| `.github/workflows/release.yml` | Modify | Add environment, signing, notarization, cleanup steps |

---

### Task 1: Create entitlements plist

**Files:**
- Create: `src/pmview-app/macos-entitlements.plist`

- [ ] **Step 1: Create the entitlements file**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.disable-library-validation</key>
    <true/>
</dict>
</plist>
```

- [ ] **Step 2: Validate the plist is well-formed**

Run: `plutil -lint src/pmview-app/macos-entitlements.plist`
Expected: `src/pmview-app/macos-entitlements.plist: OK`

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/macos-entitlements.plist
git commit -m "Add macOS hardened runtime entitlements for .NET/Godot (#45)"
```

---

### Task 2: Disable Godot ad-hoc signing in export preset

**Files:**
- Modify: `src/pmview-app/export_presets.cfg` (line with `codesign/codesign=3`)

- [ ] **Step 1: Change codesign preset value from 3 to 0**

In `src/pmview-app/export_presets.cfg`, under `[preset.0.options]`, change:

```
codesign/codesign=3
```

to:

```
codesign/codesign=0
```

This disables Godot's built-in signing so we handle it post-export in the workflow.

Note: `notarization/notarization=0` is already set — no change needed.

- [ ] **Step 2: Verify the change**

Run: `grep 'codesign/codesign=' src/pmview-app/export_presets.cfg`
Expected: `codesign/codesign=0`

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/export_presets.cfg
git commit -m "Disable Godot ad-hoc signing — handled post-export in CI (#45)"
```

---

### Task 3: Add release environment to workflow

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Add environment to build-and-upload job**

In `.github/workflows/release.yml`, add `environment: release` to the `build-and-upload` job, right after the `runs-on` line:

```yaml
  build-and-upload:
    name: Build ${{ matrix.platform }}
    needs: validate-tag
    runs-on: ${{ matrix.runner }}
    environment: release
    strategy:
```

This scopes the Apple signing secrets to the `release` environment only.

- [ ] **Step 2: Verify YAML is valid**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
Expected: No output (no errors)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Scope release workflow to 'release' environment for secret isolation (#45)"
```

---

### Task 4: Add certificate import step to workflow

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Add the certificate import step**

Insert this step immediately after the "Compute artifact name" step and before the "Copy addon into Godot project for export" step:

```yaml
      - name: Import Apple signing certificate
        if: matrix.preset == 'macOS'
        env:
          APPLE_CERTIFICATE_P12: ${{ secrets.APPLE_CERTIFICATE_P12 }}
          APPLE_CERTIFICATE_PASSWORD: ${{ secrets.APPLE_CERTIFICATE_PASSWORD }}
        run: |
          # Create a temporary keychain
          KEYCHAIN_PATH="$RUNNER_TEMP/signing.keychain-db"
          KEYCHAIN_PASSWORD="$(openssl rand -hex 24)"
          security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"
          security set-keychain-settings -lut 21600 "$KEYCHAIN_PATH"
          security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"

          # Import certificate
          CERT_PATH="$RUNNER_TEMP/certificate.p12"
          echo "$APPLE_CERTIFICATE_P12" | base64 --decode > "$CERT_PATH"
          security import "$CERT_PATH" \
            -k "$KEYCHAIN_PATH" \
            -P "$APPLE_CERTIFICATE_PASSWORD" \
            -T /usr/bin/codesign \
            -T /usr/bin/security
          rm -f "$CERT_PATH"

          # Allow codesign to access the keychain without UI prompt
          security set-key-partition-list -S apple-tool:,apple: \
            -s -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"

          # Add temp keychain to search list (prepend so it's found first)
          security list-keychains -d user -s "$KEYCHAIN_PATH" $(security list-keychains -d user | tr -d '"')

          # Export path for cleanup step
          echo "KEYCHAIN_PATH=$KEYCHAIN_PATH" >> "$GITHUB_ENV"
```

- [ ] **Step 2: Verify YAML is valid**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
Expected: No output (no errors)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add certificate import into temporary keychain for macOS signing (#45)"
```

---

### Task 5: Add app signing and verification steps

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Add signing and verification steps**

Insert these two steps immediately after the "Export Godot project" step and before the "Package macOS (.dmg)" step:

```yaml
      - name: Sign macOS app bundle
        if: matrix.preset == 'macOS'
        env:
          APPLE_TEAM_ID: ${{ secrets.APPLE_TEAM_ID }}
        run: |
          codesign --deep --force --options runtime \
            --sign "Developer ID Application: ($APPLE_TEAM_ID)" \
            --entitlements src/pmview-app/macos-entitlements.plist \
            build/pmview.app
          echo "App bundle signed successfully"

      - name: Verify app signature
        if: matrix.preset == 'macOS'
        run: |
          codesign --verify --deep --strict --verbose=2 build/pmview.app
          echo "Signature verification passed"
```

- [ ] **Step 2: Verify YAML is valid**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
Expected: No output (no errors)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add codesign and verification for .app bundle (#45)"
```

---

### Task 6: Add DMG signing, notarization, and stapling steps

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Add DMG signing, notarization, and stapling**

Insert these three steps after the "Package macOS (.dmg)" step and before the "Package Linux (.tar.gz)" step:

```yaml
      - name: Sign macOS DMG
        if: matrix.preset == 'macOS'
        env:
          APPLE_TEAM_ID: ${{ secrets.APPLE_TEAM_ID }}
        run: |
          codesign --force \
            --sign "Developer ID Application: ($APPLE_TEAM_ID)" \
            "build/${ARTIFACT}"
          echo "DMG signed successfully"

      - name: Notarize macOS DMG
        if: matrix.preset == 'macOS'
        env:
          APPLE_ID: ${{ secrets.APPLE_ID }}
          APPLE_APP_SPECIFIC_PASSWORD: ${{ secrets.APPLE_APP_SPECIFIC_PASSWORD }}
          APPLE_TEAM_ID: ${{ secrets.APPLE_TEAM_ID }}
        run: |
          xcrun notarytool submit "build/${ARTIFACT}" \
            --apple-id "$APPLE_ID" \
            --password "$APPLE_APP_SPECIFIC_PASSWORD" \
            --team-id "$APPLE_TEAM_ID" \
            --wait
          echo "Notarization succeeded"

      - name: Staple notarization ticket
        if: matrix.preset == 'macOS'
        run: |
          xcrun stapler staple "build/${ARTIFACT}"
          echo "Notarization ticket stapled to DMG"
```

- [ ] **Step 2: Verify YAML is valid**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
Expected: No output (no errors)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add DMG signing, notarization, and stapling (#45)"
```

---

### Task 7: Add keychain cleanup and notarization failure diagnostics

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Add cleanup step at the end of all steps**

Add this as the very last step in the `build-and-upload` job, after the "Upload to GitHub Release" step:

```yaml
      - name: Cleanup signing keychain
        if: always() && matrix.preset == 'macOS'
        run: |
          if [ -n "$KEYCHAIN_PATH" ] && security list-keychains | grep -q "$(basename "$KEYCHAIN_PATH")"; then
            security delete-keychain "$KEYCHAIN_PATH"
            echo "Temporary keychain removed"
          else
            echo "No temporary keychain to clean up"
          fi
```

- [ ] **Step 2: Add notarization log fetch on failure**

Update the "Notarize macOS DMG" step to capture the submission ID and fetch logs on failure. Replace the notarization step from Task 6 with:

```yaml
      - name: Notarize macOS DMG
        if: matrix.preset == 'macOS'
        env:
          APPLE_ID: ${{ secrets.APPLE_ID }}
          APPLE_APP_SPECIFIC_PASSWORD: ${{ secrets.APPLE_APP_SPECIFIC_PASSWORD }}
          APPLE_TEAM_ID: ${{ secrets.APPLE_TEAM_ID }}
        run: |
          SUBMIT_OUTPUT=$(xcrun notarytool submit "build/${ARTIFACT}" \
            --apple-id "$APPLE_ID" \
            --password "$APPLE_APP_SPECIFIC_PASSWORD" \
            --team-id "$APPLE_TEAM_ID" \
            --wait 2>&1) || {
            echo "$SUBMIT_OUTPUT"
            # Extract submission ID and fetch detailed log
            SUB_ID=$(echo "$SUBMIT_OUTPUT" | grep -oE '[0-9a-f-]{36}' | head -1)
            if [ -n "$SUB_ID" ]; then
              echo "::group::Notarization failure log"
              xcrun notarytool log "$SUB_ID" \
                --apple-id "$APPLE_ID" \
                --password "$APPLE_APP_SPECIFIC_PASSWORD" \
                --team-id "$APPLE_TEAM_ID" || true
              echo "::endgroup::"
            fi
            exit 1
          }
          echo "$SUBMIT_OUTPUT"
          echo "Notarization succeeded"
```

- [ ] **Step 3: Verify YAML is valid**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
Expected: No output (no errors)

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add keychain cleanup and notarization failure diagnostics (#45)"
```

---

### Task 8: Update documentation

**Files:**
- Modify: `docs/superpowers/specs/2026-03-19-github-release-workflow-design.md`

- [ ] **Step 1: Update the original release workflow spec**

In `docs/superpowers/specs/2026-03-19-github-release-workflow-design.md`, update the decisions table row for code signing:

Change:

```
| Code signing | Ad-hoc for v1 (Godot preset default) | Users get Gatekeeper "unidentified developer" warning. Proper signing deferred (GitHub issue) |
```

to:

```
| Code signing | Developer ID signed + notarized | Implemented in #45 — see `docs/superpowers/specs/2026-03-26-macos-code-signing-design.md` |
```

Also update the macOS packaging section (line 86) — change:

```
**Ad-hoc signed** (Godot preset `codesign/codesign=3`) — users will see Gatekeeper "unidentified developer" warning on first launch. Bypass via right-click → Open or `xattr -cr pmview.app`. The ad-hoc codesign setting should work in headless CI on macOS runners; if it causes issues, override to disabled (`codesign=0`) as a fallback.
```

to:

```
**Developer ID signed and notarized** — the `.app` bundle is signed with a Developer ID Application certificate and the `.dmg` is signed and notarized via `xcrun notarytool`. No Gatekeeper warnings for end users.
```

Also in the "Deferred Work" section at the bottom, remove item 2 ("macOS code signing & notarization") since it's now implemented.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-03-19-github-release-workflow-design.md
git commit -m "Update release workflow spec to reflect completed code signing (#45)"
```

---

### Task 9: Final review and squash into feature branch

- [ ] **Step 1: Review the complete workflow file**

Read through the entire `.github/workflows/release.yml` and verify:
- `environment: release` is present on the `build-and-upload` job
- Certificate import step exists with `if: matrix.preset == 'macOS'`
- App signing step uses `--deep --force --options runtime --entitlements`
- Verification step uses `--verify --deep --strict`
- DMG signing step exists after `hdiutil create`
- Notarization step uses `--wait` and has failure diagnostics
- Stapling step runs after notarization
- Keychain cleanup step uses `if: always() && matrix.preset == 'macOS'`
- Linux steps are completely untouched
- All secrets reference `secrets.APPLE_*` (available via the `release` environment)

- [ ] **Step 2: Verify YAML is valid one final time**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
Expected: No output (no errors)

- [ ] **Step 3: Verify export preset change**

Run: `grep 'codesign/codesign=' src/pmview-app/export_presets.cfg`
Expected: `codesign/codesign=0`

- [ ] **Step 4: Verify entitlements plist**

Run: `plutil -lint src/pmview-app/macos-entitlements.plist`
Expected: OK
