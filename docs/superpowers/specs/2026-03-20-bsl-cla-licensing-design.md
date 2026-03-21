# BSL 1.1 + CLA Licensing Design

**Date:** 2026-03-20
**Status:** Approved

## Problem

pmview-nextgen has no license. The project needs a license that:

- Allows free internal and non-commercial use
- Prevents commercial repackaging (e.g., a vendor bundling it into a sold product or offering it as a service)
- Preserves the owner's ability to relicense, dual-license, or transfer IP in future
- Eventually releases old versions as true open source to build community trust

## Decision

**License:** Business Source License 1.1 (BSL 1.1)
**Change Date:** 3 years from each version's release date
**Change License:** Apache License 2.0
**Licensor:** Paul Smith

### Additional Use Grant

Non-production and internal non-commercial use is permitted. Users may use, copy, modify, and create derivative works for internal, non-commercial purposes. Users may NOT provide the software as a commercial service or embed it in a commercial product without a separate commercial license from the Licensor.

### Contributor License Agreement

Individual CLA enforced via GitHub Actions (CLA Assistant Lite pattern):

- Contributors grant a perpetual, worldwide, irrevocable license to their contributions
- Licensor retains the right to relicense, sublicense, and transfer
- Contributors certify they have the right to contribute (DCO-style)
- Contributors retain their own copyright (license grant, not assignment)
- Signatures collected via a pinned GitHub issue; a GitHub Action checks PRs

## Why BSL 1.1

- **Matches intent:** Free for internal use, protected against commercial strip-mining
- **Time-bomb builds trust:** Old versions automatically become Apache 2.0 after 3 years
- **Battle-tested:** Used by HashiCorp (Terraform), Sentry, MariaDB, CockroachDB
- **CLA-compatible:** Pairs naturally with the contributor agreement for future flexibility
- **Dual-licensing ready:** Commercial licenses can be offered alongside BSL
- **Clean dependency story:** All current dependencies (MIT, Apache 2.0, SIL OFL 1.1) are fully compatible

## Why not alternatives

- **MIT/Apache 2.0:** No protection against commercial repackaging
- **AGPL v3:** Doesn't actually prevent commercial use, just makes it inconvenient via copyleft
- **SSPL:** Too aggressive, designed for the database-as-a-service problem specifically
- **Commons Clause:** Legally ambiguous definition of "sell"

## File Changes

| File | Action |
|------|--------|
| `LICENSE.md` | Create — BSL 1.1 full text with parameters |
| `CLA.md` | Create — Individual Contributor License Agreement |
| `.github/workflows/cla.yml` | Create — GitHub Action enforcing CLA on PRs |
| `README.md` | Update — Replace "TBD" license section with BSL summary |

No source file license headers (root LICENSE.md is sufficient). No dependency or attribution changes needed.
