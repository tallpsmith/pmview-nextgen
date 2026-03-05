<!--
  SYNC IMPACT REPORT
  ==================
  Version change: [template] → 1.0.0 (initial ratification)

  Modified principles:
    - All principles are new (initialised from blank template)

  Added sections:
    - Core Principles (I–V)
    - Development Workflow
    - Technical Governance
    - Governance

  Removed sections:
    - None (first version)

  Template propagation status:
    ✅ .specify/templates/plan-template.md  — Constitution Check section compatible
    ✅ .specify/templates/spec-template.md  — User story independence aligns with Principle I
    ✅ .specify/templates/tasks-template.md — TDD task ordering aligns with Principle II
    ⚠ .specify/templates/checklist-template.md — review manually for performance & UX gates

  Deferred TODOs:
    - None. All fields resolved.
-->

# pmview-nextgen Constitution

## Core Principles

### I. Prototype-First Iteration

Every non-trivial capability MUST begin as a disposable, runnable prototype before
any production-quality implementation commences. Prototypes exist to prove or
disprove feasibility, flush out requirements, and surface unexpected constraints
— not to ship.

Rules:
- MUST identify the smallest independently runnable slice that produces tangible,
  demonstrable output before proceeding to full implementation.
- Prototypes MUST be clearly separated from production code (e.g., `prototype/`,
  `spike/` directories or dedicated branches).
- Findings from every prototype MUST feed back into the feature spec before the
  implementation plan is finalised.
- Big-vision features MUST be decomposed until each increment can be demo'd and
  validated independently.
- Velocity of validation beats elegance of implementation — move fast to learn,
  then build right.

**Rationale**: The vision is ambitious and feasibility is uncertain. Committing
to full implementation before validation wastes time and locks in the wrong
decisions. Prototypes keep the cost of being wrong low.

### II. Test-Driven Development (NON-NEGOTIABLE)

All production implementation MUST follow the Red-Green-Refactor cycle. No
exceptions, no "I'll add tests later", no "it's just a small change".

Rules:
- Tests MUST be written and confirmed-failing BEFORE any implementation code is
  authored.
- Unit tests MUST cover every public interface and every non-trivial branch.
- Integration tests MUST cover the full data path from metric ingestion to
  rendered output for each user story.
- The test suite MUST pass in CI before any merge to `main`.
- Existing tests MUST NEVER be deleted to make a failing build pass — fix the
  code or discuss the design.
- Prototypes are exempt from TDD; any code promoted from prototype to production
  MUST be test-driven from scratch.

**Rationale**: Continuous delivery without a comprehensive test suite is just
continuous chaos. TDD also forces design clarity — if a unit is hard to test, it
is hard to understand. That's the metric we care about.

### III. Code Quality

Code is written once and read many times. Optimise for the reader, not the
writer.

Rules:
- Methods MUST do exactly one thing and be named to make that thing obvious
  without reading the body.
- Methods MUST be kept short enough to comprehend without scrolling.
- Abstractions MUST be introduced only when they remove genuine duplication or
  manage genuine complexity — YAGNI applies.
- No speculative generality: do not design for hypothetical future requirements.
- Every public API surface MUST have a clear, documented contract.
- Complex or non-obvious logic MUST include an inline comment explaining *why*,
  not *what*.

**Rationale**: pmview-nextgen is a community-facing project. Contributors from
game dev, systems engineering, and data science backgrounds MUST all be able to
reason about the code. Clever code that nobody else understands is a liability.

### IV. User Experience Consistency

The visual language and interaction model MUST feel cohesive across all scenes,
bindings, and configurations — a user who learns one visualisation MUST be able
to intuit the next.

Rules:
- All metric-to-visual bindings MUST follow a documented, ratified mapping
  vocabulary (e.g., speed → utilisation, colour temperature → saturation, scale
  → capacity).
- New scenes MUST reuse established vocabulary before introducing new visual
  conventions.
- Every UX decision that deviates from the established vocabulary MUST be
  justified and documented.
- Accessibility considerations (contrast, motion sensitivity) MUST be evaluated
  for every new visual element.
- The "oh wow" moment — the emotional response when a user first sees their
  system alive — MUST be preserved and never traded away for data density.

**Rationale**: Consistency is what turns a collection of cool demos into a
product. Users should never have to re-learn the system when switching scenes.

### V. Performance Standards

The system MUST feel alive and responsive at all times. Sluggish rendering
destroys the core promise.

Rules:
- The rendering loop MUST sustain a minimum of 30 FPS under nominal metric load
  on the target hardware tier; 60 FPS is the goal.
- Metric ingestion latency MUST NOT cause visible visual jitter or stutter.
- Memory usage MUST remain stable during extended monitoring sessions (no leaks
  over a 1-hour run).
- Performance regressions MUST be caught by automated benchmarks in CI before
  merging.
- Any feature that cannot meet performance requirements at acceptable complexity
  MUST be descoped or re-designed — performance is not optional.

**Rationale**: A visualisation tool that stutters is worse than a spreadsheet.
Performance is a first-class feature of pmview-nextgen, not an afterthought.

## Development Workflow

The delivery model is continuous, iterative, and validation-driven.

- **Build early, ship often**: Prefer small, independently demonstrable increments
  over large batches. Each increment MUST be deployable and testable in isolation.
- **Validation cadence**: Each user story MUST be validated (demo'd, tested, or
  user-reviewed) before the next story begins.
- **Feature flags over feature branches**: Long-running branches accumulate merge
  debt. Prefer feature flags or iterative in-branch delivery where possible.
- **Commit hygiene**: Every commit MUST be atomic (one logical change), have a
  concise message focused on *why*, and leave the test suite green.
- **CI as a gate**: No code merges to `main` unless CI passes: lint, unit tests,
  integration tests, and performance benchmarks.
- **Prototype lifecycle**: Prototypes live in isolated directories or branches.
  Once a prototype validates feasibility, implementation starts from scratch
  under TDD — prototype code does not graduate to production.

## Technical Governance

Technical decisions MUST be traceable and principle-aligned.

- **Constitution supersedes convention**: When team convention conflicts with a
  constitution principle, the constitution wins. Conventions can be changed by
  pull request; principles require a constitution amendment.
- **Principle compliance in every review**: All code reviews MUST verify
  compliance with Principles I–V. A review that approves a principle violation
  without justification is itself a violation.
- **Complexity must be justified**: Any design that adds a layer of abstraction,
  a new dependency, or a new pattern MUST document why simpler alternatives were
  insufficient. Use the Complexity Tracking table in `plan.md`.
- **Decision log**: Architectural decisions with significant trade-offs MUST be
  recorded as Architecture Decision Records (ADRs) in `docs/decisions/`.
- **Amendment procedure**: Any change to this constitution requires a dedicated
  pull request, a clear rationale for the version bump type, and explicit
  acknowledgement from the project owner before merge.

## Governance

This constitution supersedes all other project practices and conventions.
Compliance is mandatory, not aspirational.

- All pull requests MUST verify principle compliance as part of the review
  checklist.
- Complexity violations MUST be explicitly justified in the plan's Complexity
  Tracking table — not hand-waved in review comments.
- The constitution is reviewed after every major milestone and amended if
  principles have proven unworkable or incomplete.
- The project's runtime development guidance lives in `.specify/` templates and
  agent configuration files; those files MUST stay consistent with this
  constitution after every amendment.
- Versioning policy follows semantic versioning:
  - MAJOR: principle removed, redefined, or governance restructured incompatibly.
  - MINOR: new principle or section added; material expansion of guidance.
  - PATCH: clarifications, wording refinement, non-semantic fixes.

**Version**: 1.0.0 | **Ratified**: 2026-03-05 | **Last Amended**: 2026-03-05
