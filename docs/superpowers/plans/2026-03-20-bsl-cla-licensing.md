# BSL 1.1 + CLA Licensing Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add BSL 1.1 license and CLA enforcement to pmview-nextgen.

**Architecture:** Four files: LICENSE.md (BSL 1.1 text with parameters), CLA.md (individual contributor agreement), .github/workflows/cla.yml (GitHub Action), and README.md update. No code changes.

**Tech Stack:** MariaDB BSL 1.1 template, contributor-assistant/github-action v2.6.1

---

## Chunk 1: License and CLA Files

### Task 1: Create LICENSE.md

**Files:**
- Create: `LICENSE.md`

- [ ] **Step 1: Create LICENSE.md with BSL 1.1 text**

Write the full BSL 1.1 template with these parameters filled in:

```
Licensor:             Paul Smith
Licensed Work:        pmview-nextgen
                      The Licensed Work is (c) 2025-2026 Paul Smith
Additional Use Grant: You may use the Licensed Work for non-production and
                      internal non-commercial purposes. "Non-commercial" means
                      you may not provide the Licensed Work as a commercial
                      service or embed it in a commercial product offered to
                      third parties.
Change Date:          2029-03-20
Change License:       Apache License, Version 2.0
```

The rest of the file is the unmodified BSL 1.1 legal text (Terms, Covenants of Licensor). Do not modify the fixed legal text — that's a BSL requirement (covenant #4).

**Note on Change Date:** The date `2029-03-20` is 3 years from today. For future tagged releases, update the Change Date in the release's LICENSE.md to be 3 years from that release date. The BSL also has a built-in backstop: "the fourth anniversary of the first publicly available distribution of a specific version" — whichever comes first.

- [ ] **Step 2: Verify LICENSE.md content**

Read the file back, confirm:
- Parameters are correctly filled in
- Fixed legal text is unmodified from the BSL 1.1 template
- No typos in licensor name or dates

- [ ] **Step 3: Commit**

```bash
git add LICENSE.md
git commit -m "Add BSL 1.1 license — protects against commercial repackaging, converts to Apache 2.0 after 3 years"
```

---

### Task 2: Create CLA.md

**Files:**
- Create: `CLA.md`

- [ ] **Step 1: Create CLA.md with individual contributor agreement**

The CLA must include these sections:
1. **Definitions** — "You", "Contribution"
2. **Grant of Copyright License** — perpetual, worldwide, irrevocable license to the Maintainer
3. **Grant of Patent License** — standard defensive patent clause
4. **Licensing of the Project** — acknowledges BSL 1.1 and future relicensing rights (critical clause)
5. **Representations** — contributor certifies original work, employer permission
6. **No Warranty** — AS IS
7. **No Support Obligation**

The signing mechanism is posting "I have read the CLA Document and I hereby sign the CLA" as a PR comment. Reference this exact phrase in the preamble.

- [ ] **Step 2: Verify CLA.md content**

Read the file back, confirm:
- All 7 sections present
- Section 4 explicitly mentions BSL 1.1 and future relicensing
- Signing phrase matches what the GitHub Action will expect

- [ ] **Step 3: Commit**

```bash
git add CLA.md
git commit -m "Add Individual CLA — preserves relicensing flexibility for BSL project"
```

---

### Task 3: Create CLA GitHub Action

**Files:**
- Create: `.github/workflows/cla.yml`

- [ ] **Step 1: Create .github/workflows/ directory if needed**

```bash
ls .github/workflows/ 2>/dev/null || mkdir -p .github/workflows
```

- [ ] **Step 2: Create cla.yml workflow**

The workflow must:
- Trigger on `issue_comment` (created) and `pull_request_target` (opened, closed, synchronize)
- Set permissions: actions write, contents write, pull-requests write, statuses write
- Use `contributor-assistant/github-action@v2.6.1`
- Configure:
  - `path-to-signatures`: `signatures/version1/cla.json`
  - `path-to-document`: `https://github.com/tallpsmith/pmview-nextgen/blob/main/CLA.md`
  - `branch`: `main`
  - `allowlist`: `tallpsmith,bot*,dependabot*,github-actions*`
  - `lock-pullrequest-aftermerge`: true
  - `custom-pr-sign-comment`: `I have read the CLA Document and I hereby sign the CLA`
  - `custom-notsigned-prcomment`: Friendly message linking to CLA.md with signing instructions
  - `custom-allsigned-prcomment`: Thank you confirmation

- [ ] **Step 3: Verify cla.yml content**

Read the file back, confirm:
- YAML syntax is valid
- Trigger events are correct
- Permissions block is present
- `path-to-document` URL points to the correct repo
- Allowlist includes repo owner

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/cla.yml
git commit -m "Add CLA enforcement via GitHub Action — contributors sign by commenting on PRs"
```

---

### Task 4: Update README.md License Section

**Files:**
- Modify: `README.md:269-271` (the License section)

- [ ] **Step 1: Replace the License section**

Replace:
```
## License

TBD - Exploring open source and potential dual licensing options
```

With a section that includes:
- License name: Business Source License 1.1
- Brief plain-English summary: free for internal/non-commercial use, no commercial repackaging
- Change date and conversion: converts to Apache 2.0 after 3 years
- Link to LICENSE.md for full terms
- Note about CLA requirement for contributors, linking to CLA.md

Keep it concise — 5-8 lines max.

- [ ] **Step 2: Verify README.md changes**

Read the License section back, confirm:
- No mention of "TBD" remains
- Links to LICENSE.md and CLA.md are correct (relative paths)
- Summary accurately reflects the BSL parameters

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Update README license section — BSL 1.1 with Apache 2.0 conversion"
```
