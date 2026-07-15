# Error taxonomies: closed sets you can act on vs stringly-typed vendor codes

A one-page companion to ADR-0010. The lesson: **an integration's error handling is only as good as
the vocabulary it forces you to reason in.** Translate the vendor's open-ended codes into a closed
set the moment they cross your boundary, and the rest of your system gets simple.

## The two vocabularies

| | The vendor's | Ours (the ACL's output) |
|---|---|---|
| Shape | open, versioned without notice | closed, sealed union |
| Examples | `"OK_PENDING_2"`, `"SETTLED"`, `"REJECTED_04"`, `"REJECTED_DUPLICATE"`, a 429 | `Succeeded`, `AlreadyPaid`, `PaymentRejected`, `VendorUnavailable` |
| Who reasons in it | the adapter only | every use case |
| When it changes | whenever the vendor ships | when the *business* gains a new outcome |

The vendor's vocabulary is a fact of their system; ours is a fact of ours. The adapter is the
translator, and translation is where you decide what information to keep and what to drop.

## Why closed beats stringly-typed

- **The compiler becomes a checklist.** A sealed result union means every exhaustive `switch` stops
  compiling when you add a member. `catch (Exception ex) when (ex.Message.Contains("declined"))` is a
  checklist of one — it silently ignores every code you forgot and re-breaks when the vendor rewords
  a sentence.
- **Outcomes are data; bugs are exceptions.** A declined payment is a normal business outcome, so it
  is a *value* (`PaymentRejected`) the use case switches on — not an exception thrown for control
  flow into a `catch` far from the decision. Reserve exceptions for what is genuinely exceptional.
- **Policy lives with the decision.** Because the outcome is returned, the use case (not a `catch`
  block) decides what to do: record a payment, record a failure, surface a retryable 503. The
  vendor's dialect was already spent inside the adapter.

## The unknown-state rule: fail loud, never guess

A closed set has a fifth possibility the vendor can always spring on you: **a state you have never
seen.** The wrong move is a `default:` that treats it as "probably fine" or "probably failed" — both
are lies the size of a payment. The right move is to STOP:

> An unmapped vendor state is not a business outcome. It is a hole in your model of the vendor.
> Throw, page someone, and add a row to the dialect table by hand.

In Atlas this is `UnmappedVendorStateException`, deliberately *not* in the HTTP error table, so it
becomes a loud 500. Match status **families** by prefix (`OK_PENDING_2` ≈ `OK_PENDING_9`) so a new
batch counter is not "unknown" — only a genuinely new family is.

## The dialect table (PayGrid → Atlas), and what each translation costs

| PayGrid says | Atlas hears | Information lost |
|---|---|---|
| `SETTLED` | `Succeeded` (Settled) | none |
| `OK_PENDING_2`, `OK_PENDING_9`, … | `Succeeded` (Pending) | the batch counter — irrelevant to us |
| `REJECTED_04`, `REJECTED_07`, … | `PaymentRejected` | the numeric *reason* is collapsed to prose (raw code kept for logs, not for logic) |
| `REJECTED_DUPLICATE` | `AlreadyPaid` | nothing we act on |
| HTTP 429 / 5xx | `VendorUnavailable` (or a retryable 503 on reads) | the exact transport code |
| anything else | **throw** — refuse to guess | — |

Losing information is not a bug here; it is the job. The use case cannot act on a batch counter or a
transport code, so keeping them in the domain would be coupling with no payoff. Keep exactly what the
caller can decide with, drop the rest, and fail loud on what you cannot classify.

## Try it

- PayGrid adds `OK_PENDING_9`. Which test proves it already works, and which single file would change
  if it did not? (Answer: prefix matching in `PayGridPaymentProvider`; `PayGridDialectTests` pins it.)
- PayGrid adds a brand-new family `OK_HOLD_1`. Walk the failure path: which exception, which HTTP
  status, which file changes to teach it?
