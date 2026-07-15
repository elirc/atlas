# Sprint 12 — THE PIVOT SPRINT (The Thesis Exam)

**Goal:** a real mid-quarter requirement change lands, and the PR *measures* how local the diff
was. This is the sprint the whole architecture has been underwriting.

## The pivot (stakeholder issue, dropped cold)

> `[Stakeholder]` "We signed **LedgerPay** as a second payroll-calculation vendor. Some client
> orgs must switch to LedgerPay **mid-quarter** (their current vendor is being deprecated for
> DEU). Requirements: (1) per-org, per-country vendor selection, effective-dated so a cycle in
> flight finishes on the old vendor; (2) LedgerPay returns different line granularity — our
> payslips must stay stable for consumers; (3) finance wants a shadow-run mode comparing both
> vendors' outputs before any org flips. Four weeks."

## Scope

**In:** `IPayrollCalculationVendor` port extracted from S7's calculator (our in-house calculator
becomes the first implementation — `AtlasNativeCalculator`); **LedgerPay** mock vendor (in-repo,
different line granularity + rounding dialect) behind an ACL (S9 pattern, second application);
effective-dated `VendorAssignment` per org+country (S3 pattern, second application);
**shadow-run mode**: run both vendors, persist comparison report, flag divergences beyond
tolerance; **API v2 of the payslip resource** (`/api/v2/payslips` — line grouping change that
motivated versioning) with v1 served unchanged from the same data (adapter, not fork);
**the measurement**: PR body carries a files-touched-per-layer table + commentary on every place
locality broke.

**Out:** automatic reconciliation of divergences (report + human), more vendors.

## Teaching focus

Contract evolution without breakage (v1 adapter over v2 shape). Ports paying rent. Effective
dating and ACLs as *reusable moves*, not one-off tricks — the course's patterns compounding.

## Commit narrative (sketch)

1. `refactor(domain): extract IPayrollCalculationVendor; native calculator implements it`
   (golden files pinned, untouched — third time this move appears; it should feel routine now)
2. `feat(vendor): LedgerPay mock + ACL adapter with dialect tests`
3. `feat(domain): effective-dated VendorAssignment + selection resolution`
4. `feat(application): shadow-run comparison + divergence report`
5. `feat(api): /api/v2/payslips + v1 compatibility adapter + deprecation headers on v1`
6. `docs: ADR-0014 vendor abstraction & shadow running, ADR-0015 API versioning policy,`
   `concepts/contract-evolution.md; PR body = the locality report`

## Teaching placements

- 📘 on the locality table: the thesis, adjudicated with numbers (files per layer, core vs edge).
- 📘 on shadow running: how real migrations de-risk — run both, compare, then flip.
- 🔍 on the v1 adapter: "check what v1 consumers observe: same JSON, byte-for-byte? prove it."
  (contract test: recorded v1 responses replayed against the new implementation)
- ⚠️ on effective-dated vendor switch: the cycle-in-flight rule — pin vendor at run *creation*.
- 🔗 S1 debate ("if S12's diff isn't local, I was right") — `[Author]` collects the bet, with data.

## Debate

`[Reviewer]`: "Shadow mode doubles payroll compute and stores duplicate sensitive results — gate
it." `[Author]`: agreed on gating (org allowlist + auto-expiry after flip) but not on skipping it;
an unverified vendor swap on payroll is how companies end up in the news. Conclusion: shadow runs
are time-boxed per org, comparison reports redact net-pay values to deltas. Both changes land in
this PR — the Reviewer wins on both counts (course's third Reviewer win).

## Exercises

- The locality table shows which layer took the most change. Was it the right layer? Argue.
- Add a `dry-run` query param to v2 payslips that runs the *other* vendor. How does the port make
  this trivial (or does it)?
- Write the deprecation policy for v1 (headers, sunset date, comms). What does S10's webhook
  system contribute?
