# ADR-0015: API Versioning Policy — Add in Place, Version for Breaks

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 12)
- **Deciders:** [Author], [Reviewer]

## Context

ADR-0002 §1 promised, in Sprint 01, that "after a version ships, evolution is by addition or by a new
version — never a breaking change (S12 demonstrates v2 of one resource)." S12 is that demonstration.
The pivot reshaped one resource: LedgerPay's different line granularity motivated GROUPING a payslip's
lines by kind instead of serving a flat, `Kind`-tagged array. Regrouping an array a client may index
by position is a **breaking** change to the payslip body. This ADR turns the S01 promise into a
concrete, testable policy, using the payslip as the worked example.

## Decision 1: additive changes evolve a version IN PLACE; breaking changes earn a NEW version

The dividing line is what an existing, well-behaved consumer observes:

- **Additive** — a new field a client that ignores unknowns keeps working through. Stays in v1.
  *This sprint's example:* `PayrollRunDto.SelectedVendor`. A run gained a vendor; we surfaced it by
  adding a field. No v2 of the run resource.
- **Breaking** — a reshape a conformant client can observe as a change: a removed/renamed field, a
  changed type, a **restructured array**. Earns a new version. *This sprint's example:* the payslip's
  lines went from a flat `[{label, kind, amount}]` to `{employeeDeductions, unpaidLeave,
  employerBurden}`. That is `/api/v2/payslips`.

The test for "which kind is this?" is deliberately about the CONSUMER, not the server's convenience.

## Decision 2: v2 is a new SHAPE over the same domain read — versioning lives in the mapping layer

`/api/v2/payslips` reuses the SAME `GetPayslip` use case. The read is version-agnostic; only the
projection differs (`.ToV2Dto()` vs `.ToDto()`). The domain does not know it has two audiences, and
that ignorance is what keeps a future v3 from ever touching a payslip's business logic. A version is a
mapping concern, full stop.

## Decision 3: the old version is served UNCHANGED from the same data — by an adapter, not a fork

v1's flat payslip is not a second, parallel projection maintained by hand; it is v2's grouped shape
**flattened back** (`PayslipV2Mapping.ToDto`) in the historical order (EmployeeDeduction… then
UnpaidLeave… then EmployerBurden…). One source of truth for the numbers; v1 is a thin re-grouping.

- **The byte-for-byte contract test is the guarantee.** `PayslipVersioningTests` walks a real payslip
  through both versions and asserts v1's flat list is exactly v2's groups flattened — same order,
  labels, kinds, amounts — and that gross/net/employer-cost match. This is the 🔍 review demand made
  executable: "prove v1 consumers see the same JSON."
- **The tripwire is written down.** The flatten reproduces v1 only because every current calculator
  emits its lines already grouped in that order. A future vendor that INTERLEAVED kinds would make the
  flatten regroup them, which a v1 client could observe. That seam is commented at the call site and
  pinned by the contract test against the real vendors.

## Decision 4: deprecation is a contract — headers, a sunset date, and comms

A deprecated version does not vanish; it is announced, given a runway, and points at its successor.
The v1 payslip response carries:

- `Deprecation: <http-date>` — the IETF draft header: "on the way out since this date."
- `Sunset: <http-date>` — RFC 8594: "stops being served on this date." We set a **12-month** runway.
- `Link: </api/v2/payslips/{id}>; rel="successor-version"` plus a `rel="deprecation"` link to this ADR.

Comms are not only headers. S10's webhook system is the push half: a deprecation announcement can fan
out to registered integrators, so the news does not depend on someone reading a response header (the
exercise asks the reader to design this).

## Consequences

- OpenAPI changes are drift-gated (ADR-0002 §4): adding `SelectedVendor` to v1 and the whole
  `/api/v2/payslips` surface both show up as reviewable JSON, blessed with `ATLAS_UPDATE_OPENAPI=1`.
- Two versions share one domain and one use case; the maintenance cost of v1 is one flatten function
  under a contract test, not a parallel code path.
- The policy generalises: the next breaking reshape gets its own version group and its own adapter
  back to the prior shape; the next additive field just lands.

## Rejected alternatives

- **Break v1 in place ("clients will adapt").** Silently reshaping a shipped array is exactly the
  broken promise ADR-0002 forbade; scripted integrators do not "just adapt."
- **Fork v1 as a second hand-maintained projection.** Two sources of truth for one set of numbers
  drift the day someone fixes a rounding bug in one and forgets the other. The adapter has one source.
- **Header/media-type versioning (`Accept: application/vnd.atlas.v2+json`).** Cleaner in theory, but
  our consumers are scripted integrators who version by URL path; a visible `/api/v2` is what they
  already build against (same tiebreaker as ADR-0002's header-cursor debate).
- **A global v2 of the whole API.** Versioning the entire surface to reshape one resource forces every
  client to re-test everything for a payslip change. Version the RESOURCE that broke, not the API.
