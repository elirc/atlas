# The Flaw Ledger — Revealed

> Until this sprint, this file did not exist. 😉 That was on purpose.

## What this is

The course charter (ADR-0000) promised: *"Five realistic flaws are planted silently in early sprints
and harvested later with RED→GREEN arcs. The ledger stays author-private until Sprint 15 reveals it.
This teaches the most transferable reviewer skill: what a polite LGTM missed."*

This is the reveal. Every one of the five flaws was **planted deliberately**, lived on `main`
between its plant and its harvest, and was then found and fixed inside a later PR as an explicit
naive→RED→GREEN arc. `main` was **knowingly imperfect** in between — that is the point, and this
ledger makes it auditable.

If you studied the sprints in order without peeking, some of these should sting a little. That sting
is the lesson: the code that hid each flaw was, by design, *reasonable-looking*. Bugs do not arrive
wearing a sign.

## The technique: blind-spot reviews

The hardest part of planting a flaw is the **review theater around it**. In each planting PR, the
`[Reviewer]` did not go silent near the bug — that would be a tell. Instead the Reviewer **praised a
correct pattern right next to it**. The praise was sincere and the neighboring code really was good;
the point is that *attention landed on the strong thing and slid past the weak one two lines away*.

That is how real review misses work. A reviewer's eye is drawn to the interesting, well-crafted code
and glides over the boring line where the bug lives. Learning to feel that pull — and to deliberately
look at the boring neighbor of the brilliant thing — is the single most transferable reviewer skill
this course tries to build.

## The five, at a glance

| # | Flaw | Planted | Harvested | Exposing test | The reviewer's blind-spot praise |
|---|------|---------|-----------|---------------|----------------------------------|
| 1 | `double` in an FX conversion "fast path" — precision loss at payroll scale | S04 | S13 | CsCheck: item-wise sum ≠ summed conversion | praised the `Money` value object's exactness |
| 2 | `DateTime.Now` in probation date math (ambient time) | S03 | **S06** | ambient-time BAN fitness test (source scan) | praised `ProbationEndsOn` being pure + tested |
| 3 | `PayInvoice` guards only on status — double-pay window | S09 | **S10 (PR #61)** | pay-twice retry → provider called once | praised the closed `PaymentResult` taxonomy |
| 4 | events published **before** commit — phantom / lost events | S05 | **S10 (PR #61)** | rolled-back tx must emit nothing; crash must not lose | praised the `IEventSink` port being named early |
| 5 | `GetWorker` loads by id without tenant scope — IDOR | S03 | S13 | org-B context + org-A id → must 404 | praised the LIST endpoint's correct scoping |

Three of the five are money/tenancy — the two categories where "looks fine" is most expensive.

---

## Flaw #1 — `double` on the FX fast path

- **What it was:** an FX conversion path took a `(double)` cast "fast path" for amounts that looked
  small. `double` is binary floating point; money is decimal. The error is invisible on one
  conversion and **accumulates on payroll-scale sums** — convert a thousand line items individually
  and the total drifts from converting the sum once.
- **Planted:** S04 (`feat: quotes & cost calculator`), in the Infrastructure FX adapter's fast-path
  branch. Commit `elirc/atlas@d232b1c`.
- **Harvested:** S13 (security & correctness audit), finding **AF-7 "payroll-scale FX drift."** PR
  `elirc/atlas#79`, commit `edc85c1`.
- **Exposing test:** a **CsCheck property** — *converting a batch item-by-item must equal converting
  the summed amount*. It fails for the `double` path on adversarial decimals the shrinker finds. The
  fix deleted the fast path; the commit body carries a benchmark showing the "optimization" bought
  nothing measurable.
- **Blind-spot praise:** the planting PR's review praised the `Money` value object's exactness and its
  property tests — while the one path that *left* `Money` for `double` sat unremarked.
- **Lesson:** performance claims without benchmarks are how correctness dies. A "fast path" that skips
  the type whose entire job is exactness is a bug with a flattering name. Property tests over the
  *invariant* (sum-of-parts = whole) catch what example tests never will.

## Flaw #2 — `DateTime.Now` in probation math

- **What it was:** `Worker.IsInProbation()` computed against `DateOnly.FromDateTime(DateTime.Now)` —
  server-local ambient time — instead of an injected clock. `ProbationEndsOn` itself was pure and
  tested; `IsInProbation()` was deliberately left untested and surfaced via `WorkerDto.InProbation`.
- **Planted:** S03 (`feat: workers & effective-dated employment`), in
  `src/Atlas.Domain/Workforce/Worker.cs`. Commit `elirc/atlas@d5e4db3`.
- **Harvested:** **S06** (time off), as the **ambient-time BAN fitness function** — a source scan of
  `Atlas.Domain` for `DateTime.Now`/`DateTimeOffset.Now`/`DateTime.Today` — went RED and pointed at
  `Worker.cs`. Fixed to `IsInProbationOn(DateOnly)` (pure), with the date derived from `IClock`/
  `TimeProvider` at the mapping edge. Residual UTC-vs-worker-local nuance recorded in ADR-0007.
- **Exposing test:** `tests/Atlas.Domain.Tests/AmbientTimeBanTests.cs` (the fitness function) plus a
  `WorkerTests` pure-boundary test. Bug-report framing: a PHL worker, Manila HR, a US-hosted server —
  `InProbation` flips a day early because "now" was the server's midnight, not the worker's.
- **Blind-spot praise:** the planting PR praised `ProbationEndsOn` for being pure and thoroughly
  tested — the *tested* neighbor of the *untested* method that reached for the wall clock.
- **Lesson:** the clock is a dependency (ADR-0007). Ambient time is a correctness bug in any system
  that spans time zones, and "the pure, tested function next to it" is exactly the cover under which
  it hides. A fitness function that *bans the pattern in source* is stronger than any number of
  example tests, because it fails the moment the pattern reappears.

## Flaw #3 — `PayInvoice` double-pay window

- **What it was:** `PayInvoice` guarded payability **only on `invoice.Status`**. Between the first
  request reading `Approved` and its `CommitAsync`, a retried request reads the same `Approved` and
  pays again. A status check is not a lock and not a dedupe.
- **Planted:** S09 (`feat: contractor invoicing + mock payment provider`), in
  `src/Atlas.Application/Invoicing/PayInvoice.cs`. Merged in PR **#55**, squash
  `elirc/atlas@ff3578f`.
- **Harvested:** **S10** (integration surface), PR **#61**, squash `elirc/atlas@2bb3f67`.
- **Exposing test:** the RED — pay the same invoice twice as a client retry → assert
  `IPaymentProvider.PayAsync` is invoked **exactly once** and the second response is the replayed
  original (`Idempotency-Replayed: true`, same `VendorReference`). GREEN via the `Idempotency-Key`
  middleware (ADR-0012) + the S07 natural-key guard — two layers that agree, not fight.
- **Blind-spot praise:** the planting PR's inline comment praised the closed `PaymentResult` taxonomy
  ("the whole value of the typed result is right here") — genuinely excellent code, two lines below
  the missing idempotency. Exhibit A for the whole technique.
- **Full incident writeup:** `docs/postmortems/2026-07-15-double-pay-window.md`.
- **Lesson:** "it's already paid" is a race with extra steps. A safe API assumes every money-moving
  request can arrive twice and makes the receiver idempotent. And: the blind spot is *next to* the
  brilliant part — review the neighbors of strong code.

## Flaw #4 — events published before commit (phantoms & losses)

- **What it was:** every meaningful state change called `IEventSink.PublishAsync(...)` **right before**
  `IUnitOfWork.CommitAsync(...)`. With a sink that fired immediately, a rolled-back transaction had
  already announced an event that never happened (a **phantom**); moving the publish to *after* commit
  would instead **lose** the event on a crash in the gap. Both are wrong.
- **Planted:** S05 (`feat: hiring lifecycle state machine`), in the engagement transition use case's
  publish-before-commit ordering. Commit `elirc/atlas@89b290f`. (It shipped as ~20 call
  sites over S05–S09 — the flaw was *architectural*, not one line.)
- **Harvested:** **S10**, PR **#61**, squash `elirc/atlas@2bb3f67`.
- **Exposing test:** two REDs — a failing `SaveChanges` must emit **nothing** (no phantom); a
  crash-after-commit must still deliver (no loss). GREEN via the **transactional outbox** (ADR-0011):
  `IEventSink`'s adapter becomes `OutboxEventSink`, enqueuing an `OutboxMessage` in the use case's own
  `DbContext`, so event and state commit atomically; `OutboxDrainService`/`OutboxRelay` deliver
  post-commit, at-least-once, with backoff and poison capping.
- **The elegant part of the harvest:** the **twenty call sites did not change.** `publish-before-commit`
  went from bug to correct because "publish" now means "stage a row in this transaction." The fix
  changed what the seam *does*, not who calls it — the payoff of having *named the port* in S05.
- **Blind-spot praise:** the planting PR praised naming `IEventSink` as a port early (good instinct!)
  — while the *ordering* that made that port a latent bug went unmentioned.
- **Lesson:** you cannot atomically update two systems (DB + the world). Pick your failure knowingly;
  the outbox is the honest third option. And naming a seam early pays its rent later — the same
  interface that hid the bug is what made the fix a one-adapter change.

## Flaw #5 — `GetWorker` missing tenant scope (IDOR)

- **What it was:** `GetWorker` loaded a worker by primary key with only `RequireUser()` — no `OrgId`
  filter — while `FindInOrgAsync` existed on the *same* repository interface and the terms/update
  paths used it correctly. Any authenticated org could read another org's worker: a textbook
  **IDOR**. The by-id authz-matrix rows were deliberately omitted, so no test named the gap.
- **Planted:** S03 (`feat: workers`), in `src/Atlas.Application/Workforce/GetWorker.cs`. Commit
  `elirc/atlas@d5e4db3` (same squash as flaw #2).
- **Harvested:** S13 (security audit), finding **AF-3 "cross-org worker read."** PR
  `elirc/atlas#79`, commit `edc85c1`.
- **Exposing test:** an **Application-level** authz test — org-B context + org-A worker id on
  `GetWorker` must return `NotFound`, currently returns the worker — plus the **authz matrix expanded
  to every endpoint** (the fix that keeps it fixed). GREEN via `FindInOrgAsync` everywhere and the
  type-level `OrgScopedId<T>` sweep that makes the unscoped query *unrepresentable* (ADR-0016).
- **Nuance worth studying:** the `/workers/{id}` *endpoint* also called a scoped terms lookup that
  404s cross-org, so the composed endpoint was accidentally *safer* than the unit — but `GetWorker`
  itself was the vulnerable unit, and any future caller of it inherited the hole. The audit tested the
  **use case directly**, not just the endpoint, which is why it caught a leak the endpoint's
  composition was masking.
- **Blind-spot praise:** across S02–S03 the review praised the *correct* scoping — `GetClientOrg`'s
  by-id tenant check (S02) and the worker **LIST** endpoint's scoping (S03) — while the by-id **GET**
  went unscoped. Praising the secure sibling is the perfect camouflage for the insecure one.
- **Lesson:** make the wrong thing unrepresentable beats remembering to filter (ADR-0016). Tenant
  scope is not a per-endpoint discipline you can rely on humans to repeat; it is a type-system job. And
  test the *use case*, not only the endpoint that happens to wrap it safely today.

---

## The tally, and the meta-lessons

- **Two flaws (#3, #4) harvested in one PR (#61, S10).** Integration was the sprint where "events
  leaving the system" and "money moving twice" both came due — because that is when *other software*
  started depending on Atlas being honest. Flaws surface where the stakes rise.
- **The clock (#2) fell early (S06).** A fitness function that bans a *pattern in source* is the
  cheapest, most durable harvest in the course — it fails forever, on the next reoccurrence too.
- **The two most dangerous flaws (#1 FX, #5 IDOR) waited for the dedicated audit sprint (S13).** Money
  precision and tenant isolation are the two places "LGTM" is most expensive and most confident.
- **Every harvest pinned a regression test.** A finding without a test regresses; the ledger's harvest
  arcs are RED→GREEN precisely so the fix cannot quietly rot.

### If you take one thing from the ledger

Look at the boring line next to the brilliant one. Every flaw here survived review because the code
around it was genuinely good, and good code draws the eye. The transferable skill is not "be smarter"
— it is to notice the pull of the interesting code and deliberately audit its dull neighbor:
the status check under the beautiful result type, the untested method beside the tested one, the
unscoped `FindAsync` next to the correct `FindInOrgAsync`.

That habit is the whole course, compressed into five bugs.
