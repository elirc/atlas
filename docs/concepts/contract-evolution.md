# Concept: Contract Evolution — Change the Shape Without Breaking the Client

A shipped API is a promise to people who wrote code against it and went home. "We'll just update the
clients" is not available to you — you don't control them, and some of them are a cron job on a server
nobody has logged into since 2024. So the skill is changing what your service returns **without
changing what an existing consumer observes**. S12 makes it falsifiable: a payslip's line structure
changed, and a v1 client gets back the identical bytes it always did — proven, not asserted.

## Two kinds of change, and the only test that matters

The test is never "is this a big change?" It is "**can a conformant existing client observe it?**"

- **Additive** — a new field. A client that ignores unknown fields (the conformant kind) sails through.
  → Evolve the version **in place**. S12 added `selectedVendor` to the run resource. No new version.
- **Breaking** — a removed field, a renamed field, a changed type, or a **restructured array**. A
  client that reads `lines[3]` or expects a `kind` on each line can see the difference.
  → Ship a **new version**. S12's payslip lines went flat → grouped. That earned `/api/v2/payslips`.

Restructuring an array is the sneaky one. It *feels* additive ("we just organised the lines!") but a
client indexing by position is broken the instant the positions move. When in doubt, ask what
`response.lines[3].kind` does before and after. If the answer changed, it's breaking.

## The move: the old version is an ADAPTER over the new one, not a fork

The tempting mistake is to keep v1's code exactly as it was and write v2 beside it — two projections of
the same numbers, maintained by hand. They drift the first time someone fixes a rounding bug in one and
forgets the other. Now v1 and v2 disagree about a paycheck, and you have two bugs.

Instead: make the NEW shape canonical, and derive the OLD one from it.

```
v2 (canonical):  group the domain's lines by kind → { employeeDeductions, unpaidLeave, employerBurden }
v1 (adapter):    flatten those groups back to a flat, kind-tagged list, in the historical order
```

One source of truth for the numbers. v1 is a thin re-grouping over v2. A rounding fix lands once, in the
domain, and both versions inherit it. This is the same instinct as an anti-corruption layer: don't let
two representations of one fact wander off; pick the canonical one and translate at the edge.

## Prove it, don't promise it

"v1 is unchanged" is a claim, and claims rot. The proof is a **contract test that replays what v1
clients see**:

```
GET /api/v1/payslips/{id}   →  flat list, each line { label, kind, amount }
GET /api/v2/payslips/{id}   →  grouped
assert: v1.lines == flatten(v2.groups)   // same order, labels, kinds, amounts
assert: v1.gross == v2.gross, v1.net == v2.net, v1.employerCost == v2.employerCost
```

That green test is what lets you re-implement v1 through v2 without gambling a client's integration. It
is the byte-for-byte receipt. (You can go further and record a real v1 JSON body as a golden file,
blessed on first run like the OpenAPI drift gate — same discipline, one more nine of confidence.)

## Name the tripwire you're standing on

The v1-from-v2 flatten reproduces v1 *because* every calculator emits its lines already grouped
(deductions, then unpaid, then burden). That is a property, and properties can lapse. A future vendor
that interleaved kinds would make the flatten regroup them — and a v1 client could see the reordering.
So you write the assumption down, at the call site, and let the contract test guard it for the vendors
you actually have. The next author gets a comment, not a surprise. Honest code names the plank it's
standing on.

## Deprecation is a contract too

You don't get to delete v1 the moment v2 ships. You **announce** it, give it a runway, and point at its
successor — in-band, on every response, so the news doesn't depend on someone reading a changelog:

- `Deprecation: <date>` — "on the way out since here."
- `Sunset: <date>` — "gone after here." (RFC 8594. Give integrators months, not days.)
- `Link: </api/v2/payslips/{id}>; rel="successor-version"` — "go here instead."

And push, don't only pull: the same event system that fans out webhooks can announce a deprecation to
registered integrators, so the ones who never read headers still hear about it.

## The tell

Next time someone says "we added v2, v1 still works," ask for the test that replays a v1 response
against the new code. If it exists and it's green, v1 really is unchanged. If it doesn't, "v1 still
works" is a hope — and hopes about other people's cron jobs are how you end up in an incident channel at
2 a.m. explaining why a payslip grew a field.
