# ADR-0010: Anti-Corruption Layers for External Vendors

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 09)
- **Deciders:** [Author], [Reviewer]

## Context

Sprint 09 is Atlas's first true external integration: money leaves the building through a payment
vendor. The vendor is **PayGrid**, an in-repo mock built to misbehave the way real vendors do —
integer cents, stringly-typed statuses (`OK_PENDING_2`, `SETTLED`, `REJECTED_04`), unpredictable
429s, asynchronous settlement, and no idempotency on the way in.

The question this ADR settles is not "how do we call PayGrid" — that is four method calls. It is
"**where does PayGrid's dialect live, and where does it stop.**" Get that boundary wrong and every
use case that ever touches money becomes a partial PayGrid client; get it right and swapping the
vendor (S12) is a one-file diff.

## Decision 1: an anti-corruption layer — a port in Application, a translating adapter in Infrastructure

`IPaymentProvider` (Application) speaks only Atlas's payment vocabulary:
`PaymentInstruction` in, `PaymentResult` / `PaymentSettlement` out, a `VendorPaymentReference`
value object for the vendor's handle. `PayGridPaymentProvider` (Infrastructure) is the ONE place
that speaks PayGrid — cents conversion, the status table, transport-error handling all live there.

**Above the adapter, the word "PayGrid" never appears. Below it, "invoice" and "domain" never do.**
This is the second vendor port in Atlas (`IFxRates` from ADR-0005 was the first); the pattern is
now deliberate, not coincidental.

## Decision 2: a closed, typed result taxonomy — not `catch (Exception)` + string matching

The adapter returns a closed set the application can act on: `Succeeded` (with a reference and a
settlement state), `AlreadyPaid`, `PaymentRejected`, `VendorUnavailable`. The pay use case switches
over them **exhaustively** — the union is sealed (private constructor), so a new member stops every
switch compiling until it is handled.

- **Rejected: inline translation in the use case** (call PayGrid straight, translate errors where
  they happen). This is how vendor dialects metastasize: the first `if (status == "REJECTED_04")`
  in a use case is a PayGrid contract leaking into business logic, and the second use case copies
  it. The price of the ACL is one file; the price of no ACL is that every money-moving use case is
  a vendor contract. (This was the sprint's design debate; see the PR.)
- **Rejected: `catch (Exception ex) when (ex.Message.Contains("declined"))`.** String-matching a
  vendor's prose is a checklist of one that silently misses every other case, and it re-breaks the
  day the vendor rewords a message. A declined payment is a normal *outcome*, modelled as data — not
  an exception thrown across the boundary for control flow.

## Decision 3: unknown vendor states fail LOUD — never guess

A status the adapter has never been taught to translate throws `UnmappedVendorStateException`. It is
deliberately **absent** from `AtlasExceptionHandler`'s table, so it surfaces as a **500** that pages
someone. An unmapped state is not a business outcome — it is a hole in our model of the vendor, and
the only honest responses are "stop" and "a human reads PayGrid's changelog and adds a row." A
default branch that treated the unknown as "probably fine" (or "probably failed") would be a lie the
size of a payment. Status FAMILIES are matched by prefix (`OK_PENDING_2` and `OK_PENDING_9` are the
same acceptance), so a mere new batch-counter does not trip the fail-loud path — only a genuinely
new family does.

## Decision 4: action returns a typed result; a read throws — the deliberate asymmetry

`PayAsync` folds "the vendor is unreachable" into its result set (`VendorUnavailable`) because paying
is an action with a closed decision set the use case owns. `GetSettlementAsync` is a read with no
such set, so a transient fault surfaces as `PaymentProviderUnavailableException` (mapped to **503**,
retryable) rather than an invented result member. Same fault, two shapes, because the caller does two
different things with it.

## Decision 5: the adapter's tests document the dialect as executable spec

Per the debate's resolution, `PayGridDialectTests` drives the adapter through PayGrid's worst day —
429s, suffixed statuses, a rejection, a duplicate, an unmapped status — and each assertion is a line
of the vendor's manual you can run. When PayGrid's vocabulary drifts, those tests (and only the
adapter) change. "Test the adapter against the vendor's worst day, not its brochure."

## Consequences

- A vendor swap (S12) touches `IPaymentProvider`'s implementations and nothing else — the invoice
  use cases cannot tell PayGrid from its successor.
- The domain records a `VendorReference` as an opaque string and a `FailureReason` as neutral prose;
  it never stores a PayGrid code, so a vendor change cannot orphan historical data's meaning.
- Money → cents rounding is a boundary decision made once, in the adapter (banker's, to whole
  cents), because the vendor cannot represent a fractional cent.

## Notes / recorded bets

- **Idempotency of the pay POST** is not part of this ADR. The `Idempotency-Key` contract and the
  middleware that enforces it on every money-moving POST arrive in **S10** (SPEC §4); `PaymentInstruction`
  carries only business facts today. PayGrid itself deduplicates nothing on the way in — the honest
  reason the responsibility is ours to build, next sprint.
- **`AlreadyPaid`** is modelled and translated (PayGrid's `REJECTED_DUPLICATE`) even though the mock
  produces it only when scripted — the taxonomy is complete against the vendor's documented states,
  not only the ones the happy path hits.
- **Multi-currency settlement and fees** are out of scope (SPEC §8); an invoice is single-currency,
  and the payout currency is the contractor's.
