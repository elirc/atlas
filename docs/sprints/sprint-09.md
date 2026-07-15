# Sprint 09 — Contractor Invoicing & the Mock Payment Provider (First Real Vendor)

**Goal:** money leaves the building (fictionally). First true external integration, built behind
an anti-corruption layer, against a vendor that misbehaves on purpose.

## Scope

**In:** ContractorInvoice (lines as Money, submit → approve [two-step: HR then OrgAdmin, via
ApprovalWorkflow variant] → pay → paid/failed); **PayGrid**, an in-repo mock payment vendor with
a deliberately awkward API (cents-as-integers, statuses like `"OK_PENDING_2"`, random 429s,
async settlement callback) implemented as an in-process fake behind an `IPaymentProvider` port; **anti-corruption layer** translating PayGrid's dialect into
domain language (typed errors: `VendorUnavailable`, `PaymentRejected`, `AlreadyPaid`); payment
status reconciliation endpoint; invoice endpoints; `docs/http/invoices.http`.

**Out:** webhooks from PayGrid (S10 gives us the outbox first), multi-currency settlement, fees.

## Teaching focus

The anti-corruption layer: vendor types stop at the adapter; the domain never says "PayGrid."
Error taxonomies: closed sets the application can act on, vs stringly-typed vendor codes.

## Commit narrative (sketch)

1. `feat(domain): ContractorInvoice + two-step approval variant`
2. `feat(application): submit/approve/pay use cases + IPaymentProvider port with typed errors`  ← **ledger #3 planted here** (pay use case has no idempotency guard)
3. `feat(vendor): PayGrid mock — the vendor we deserve` (awkward on purpose; README documents its dialect)
4. `feat(infrastructure): PayGridPaymentProvider adapter — the ACL` (+ adversarial adapter tests: 429s, weird statuses)
5. `feat(api): invoice endpoints`
6. `docs: ADR-0010 anti-corruption layers, concepts/error-taxonomies.md`

## Teaching placements

- 📘 on the ACL adapter: ubiquitous language defense; the adapter is where translation lives and dies.
- 📘 on typed errors: why `PaymentRejected` beats `catch (Exception)` + string matching.
- 🔍 on the adapter tests: "test the adapter against the vendor's worst day, not its brochure."
- ⚠️ on the mock's `"OK_PENDING_2"`: real vendors return states your enum forgot — the ACL's
  unknown-state policy (fail loud, never guess).
- 🔗 IPaymentProvider ← ADR-0005's FX port ("second vendor port; notice the pattern maturing").
- **Ledger #3:** `[Reviewer]` praises the typed error taxonomy on the pay use case. Nothing about
  what happens when the caller times out and retries.

## Debate

`[Reviewer]`: "The ACL is 200 lines to wrap 4 HTTP calls — call PayGrid straight from the use
case and translate errors inline." `[Author]`: inline translation is how vendor dialects
metastasize (S12 will swap a vendor; the invoice use case must not notice). Price of the ACL:
one file. Price of no ACL: every use case is a vendor contract. Reviewer concedes but demands the
adapter's tests document PayGrid's dialect as executable spec — accepted, done in commit 4.

## Exercises

- PayGrid adds status `"OK_PENDING_9"`. Walk the failure path. Which single file changes?
- Write the retry-after-timeout scenario as a test at the use-case level. What does it reveal? (Keep
  the test — you'll want it next sprint.)
- Map PayGrid's dialect table (their term → our term). Which translations lose information?
