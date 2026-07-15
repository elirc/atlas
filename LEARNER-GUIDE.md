# Learner Guide

You are the audience of this repository. Nobody expects you to write Atlas — you are here to
**read it the way a senior engineer would**, one sprint at a time. This guide tells you how.

## The unit of study: one merged PR per sprint

Each sprint is a single squash-merged Pull Request. Study them **in order** (PR list, oldest
first). For every sprint:

1. **Read the stakeholder issue** attached to the sprint milestone. It states the business need in
   a persona's voice. Before reading any code, sketch how *you* would build it.
2. **Read the sprint playbook** (`docs/sprints/sprint-NN.md`) — goal, scope, teaching focus.
3. **Walk the commits in order** (the PR's Commits tab). The commit sequence is a story:
   domain model → use cases → adapters → endpoints → docs. Some sprints deliberately commit a
   naive implementation, then a RED failing test, then the fix — that arc is the lesson.
   Commit bodies have `Why this way:` / `Notice:` / `See also:` sections; read them.
4. **Read the inline review comments** on the diff. They are tagged:
   - 📘 **concept** — an idea worth studying on its own
   - 🔍 **review-lens** — how a senior reads this code; what they check first
   - ⚠️ **pitfall** — a trap this code avoids (or once contained)
   - 🔗 **connects-to** — links to an ADR or an earlier sprint
   Comments are authored under persona prefixes: `[Author]`, `[Reviewer]`, `[Stakeholder]`.
   When `[Reviewer]` and `[Author]` argue, follow the whole thread — sometimes the Reviewer wins
   and a later commit changes course.
5. **Read the design-debate comment** on the PR (one per sprint). It is the closest thing to
   sitting in a real architecture review.
6. **Do the exercises** at the bottom of the PR body. They are small and pointed
   ("delete X and let the test suite tell you why it existed").

## How to read a hexagonal codebase

Start from the **inside** and move out — the same direction the commits are ordered:

1. `Atlas.Domain` — entities, value objects, state machines, policies. No framework `using`s.
   If you understand this project, you understand the business.
2. `Atlas.Application` — use cases. Each answers: which port do I need? what do I check first?
   what do I persist? what event do I record? Ports (interfaces) live here; implementations don't.
3. `Atlas.Infrastructure` — the adapters. EF Core, the outbox dispatcher, vendor clients.
   Everything here is replaceable — that's the point.
4. `Atlas.Api` — thin. Endpoints translate HTTP ↔ Contracts DTOs ↔ use cases. An endpoint
   containing business logic is a bug (and the architecture tests are watching).

`tests/Atlas.ArchitectureTests` encodes these rules as executable assertions. Read it early: it is
the map of what may depend on what — and CI fails if the map is violated.

## Comment markers in code

- `// LEARN:` a concept worth studying — often paired with a `docs/concepts/` one-pager
- `// WHY:` a non-obvious decision, justified
- `// GOTCHA:` a trap the code avoids
- `// TRY:` an exercise hook — do what it says and watch what breaks

## Cross-sprint arcs to watch

- **The Money type** (S4 → S7 → S13): decimal discipline, allocation without lost pennies.
- **The clock** (S3 → S6): why `DateTime.Now` is a bug factory in a multi-timezone product.
- **The Approval component** (S6 → S8 → S9): built once, reused twice — watch how the
  abstraction is *extracted*, not predicted.
- **Events out of the system** (S5 → S9 → S10): from "just write an activity row" to a
  transactional outbox with signed webhooks.
- **Rule packs** (S4 → S11 → S12): from a switch statement's worth of country data to plugins
  that make adding a country a zero-core-change diff.
- **The flaw ledger** (revealed in Sprint 15): five realistic defects were planted silently and
  harvested later with failing tests. When you reach the reveal, go back and re-read the sprints
  where they were planted — and notice what the review comments *didn't* say.

## Ground rules for self-study

- Don't skip ahead of the commit order inside a PR; the sequence is deliberate.
- When a test looks redundant, delete it locally and see what breaks before deciding.
- Every ADR lists rejected alternatives — argue with them before accepting them.
- Run the flows yourself: `docs/http/` has a request file per feature area.
