# macOS Code Signing & Notarization Design

**Date:** 2026-03-26
**Status:** Approved
**Issue:** #45

## Overview

Replace ad-hoc signing of the macOS release build with proper Developer ID signing and Apple notarization, eliminating the Gatekeeper "unidentified developer" warning for end users.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Signing approach | Post-export, explicit `codesign`/`notarytool` in workflow | Full control, visible diagnostics, no third-party action trust |
| Godot export signing | Disabled (`codesign=0`) | We sign ourselves after export |
| Secret scoping | GitHub `release` environment | Secrets only available to release workflow, not PR builds |
| Entitlements | Minimal plist with two .NET runtime exceptions | Mono/CoreCLR requires unsigned executable memory and disabled library validation |
| `.app` signing | Developer ID Application certificate | Required for Gatekeeper approval |
| `.dmg` signing | Developer ID Application certificate | Prevents separate Gatekeeper warning on disk image open |
| Hardened Runtime | Enabled (`--options runtime`) | Required for notarization |
| Third-party actions | None for signing/notarization | Certificate material stays out of third-party code |

## Prerequisites (Completed)

- Apple Developer account with Developer ID Application + Installer certificates
- App-specific password generated for notarization
- GitHub `release` environment created with secrets:

| Secret | Purpose |
|--------|---------|
| `APPLE_CERTIFICATE_P12` | Base64-encoded `.p12` containing both Developer ID certs |
| `APPLE_CERTIFICATE_PASSWORD` | Password to unlock the `.p12` |
| `APPLE_ID` | Apple Developer account email |
| `APPLE_APP_SPECIFIC_PASSWORD` | App-specific password for `notarytool` |
| `APPLE_TEAM_ID` | 10-character Apple Developer Team ID |

## Export Preset Changes

In `src/pmview-app/export_presets.cfg`, the macOS preset changes:

```
codesign/codesign=0          # was 3 (ad-hoc) — signing handled post-export
notarization/notarization=0  # unchanged — notarization handled post-export
```

## Entitlements Plist

New file: `src/pmview-app/macos-entitlements.plist`

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

These two entitlements are standard for apps embedding a managed runtime (Mono/CoreCLR). Can be tightened later if testing shows they're not needed.

## Workflow Changes

### Environment

The `build-and-upload` job gains `environment: release` to access scoped secrets.

### Modified macOS Flow

```
export (unsigned .app)
    │
    ├─ Import certificates into temporary keychain
    │   - Create temp keychain with random password
    │   - Decode base64 .p12 from secret
    │   - Import into temp keychain
    │   - Set keychain search list
    │
    ├─ Sign the .app bundle
    │   - codesign --deep --force --options runtime
    │       --sign "Developer ID Application: ..."
    │       --entitlements macos-entitlements.plist
    │       build/pmview.app
    │   - codesign --verify --deep --strict (verification gate)
    │
    ├─ Package .dmg (existing hdiutil step, unchanged)
    │
    ├─ Sign the .dmg
    │   - codesign --force
    │       --sign "Developer ID Application: ..."
    │       build/pmview-<ver>-macos-universal.dmg
    │
    ├─ Notarize the .dmg
    │   - xcrun notarytool submit --wait
    │       --apple-id / --password / --team-id
    │   - xcrun stapler staple (embeds ticket for offline verification)
    │
    ├─ Upload to release (existing step, unchanged)
    │
    └─ Cleanup temporary keychain (if: always())
```

### Signing Identity Resolution

The `codesign --sign` argument uses the Team ID from secrets to match the certificate by substring: `"Developer ID Application: ($APPLE_TEAM_ID)"`. This avoids hardcoding the full identity name.

### Error Handling

- **Keychain cleanup**: Runs with `if: always()` — temp keychain deleted even on failure
- **Notarization diagnostics**: On `notarytool submit` failure, run `xcrun notarytool log` to fetch Apple's detailed failure report
- **Verification gate**: `codesign --verify --deep --strict` after `.app` signing catches issues before `.dmg` packaging

### What Doesn't Change

- Linux matrix legs — zero modifications
- Trigger — `on: release: types: [published]`
- Artifact naming convention
- `validate-tag` job
- Workflow permissions (`contents: write`)
- Build steps (restore, build, Godot export)

## New Files

| File | Purpose |
|------|---------|
| `src/pmview-app/macos-entitlements.plist` | Hardened Runtime entitlements for .NET/Godot |

## Modified Files

| File | Change |
|------|--------|
| `src/pmview-app/export_presets.cfg` | `codesign/codesign` from `3` → `0` |
| `.github/workflows/release.yml` | Add environment, signing, notarization, cleanup steps |

## Future Tightening

Once the pipeline is proven working:
1. Remove entitlements one at a time to find the minimal set needed
2. Consider adding environment protection rules (manual approval before release)
