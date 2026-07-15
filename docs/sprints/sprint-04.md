# Sprint 04 — Quotes & the Cost Calculator (Business-Logic Flagship #1)

**Goal:** given a country and a gross salary, produce a defensible total-cost-of-employment
breakdown. Money becomes a type; countries become rule packs; FX becomes a port.

## Scope

**In:** `Money` value object (amount+currency, banker's rounding policy explicit, no cross-currency
arithmetic without an explicit rate); `CountryRulePack` v1 — employer-burden components as data
(percentages + caps) for DEU, PHL, USA, GBR; quote engine: gross → burden lines → atlas fee →
totals; `IFxRates` port + fixed-table fake provider + Infrastructure adapter; Quote persistence +
endpoints (`POST /quotes`, get, list); CsCheck property tests (Money add/multiply/allocate);
`docs/http/quotes.http`.

**Out:** payroll (S7) — quotes estimate, payroll executes; live FX.

## Teaching focus

Value objects as bug repellent. Policy-as-data vs policy-as-code and when each earns its place.
Ports for volatility (FX will change vendor; the domain shouldn't notice).

## Commit narrative (sketch)

1. `feat(domain): Money — construction, arithmetic, rounding policy` (+ property tests in same commit)
2. `feat(domain): CountryRulePack v1 + burden line computation`
3. `feat(domain): quote calculation — burden lines, fee, totals`
4. `feat(application): create-quote use case + IFxRates port`
5. `feat(infrastructure): fx adapter (fixed table) + quote persistence`  ← **ledger #1 planted here**
6. `feat(api): quote endpoints + contracts`
7. `docs: ADR-0005 money & rule packs, concepts/value-objects.md`

## Teaching placements

- 📘 on Money: why `decimal`, why currency is part of identity, why no implicit conversion.
- 📘 on rule pack data: the line between configuration and code.
- 🔍 on the quote calculation: "trace one euro through the pipeline before approving."
- ⚠️ on rounding: banker's vs away-from-zero — pick one, write it down, test it.
- 🔗 IFxRates ← ADR-0001 ports; foreshadow S12 vendor swap ("ports earn rent later").
- **Ledger #1:** the FX adapter's fast path converts small amounts via `double` "to avoid decimal
  division cost." Commit message honest about the fast path, silent about the hazard. One
  `[Reviewer]` comment praises the *port design* on the same file.

## Debate

`[Reviewer]`: "Rule packs as data tables will collapse — Germany's church tax isn't a percentage,
it's a conditional. Make packs code from day one." `[Author]`: data survives until a country
needs logic; when one does we promote packs to plugin classes (scheduled: S11) and today's data
tables become the default implementation. Explicit bet recorded in ADR-0005 with the promotion
trigger. (This debate is the setup for the S11 payoff.)

## Exercises

- Add a burden component with a wage cap (e.g., contribution ceiling) using only rule-pack data.
- Try to write `money1 + money2` with mixed currencies. Read the compiler/domain error. Explain
  why this is better than a runtime check in one endpoint.
- The allocate method distributes cents across lines. Prove (test) the lines always sum to the total.
