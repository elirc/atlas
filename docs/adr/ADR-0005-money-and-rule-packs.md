# ADR-0005: Money as a Value Object, Country Rules as Data

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 04)
- **Deciders:** [Author], [Reviewer], [Stakeholder]

## Context

Sprint 04 turns Atlas from "stores employment terms" into "reasons about the cost of
employment." Two primitives had been living on borrowed time:

- **Money.** Since S03, salaries were a `decimal` plus a `string` currency — two fields a
  caller must remember to keep in sync, with nothing stopping `€10 + $10` or an
  away-from-zero rounding drift. S01 explicitly predicted the promotion (see
  `Country.Currency`'s comment: "it becomes load-bearing in Sprint 04").
- **Country rules.** S03 shipped a one-column probation table. S04 needs employer-burden
  math per country: rates, wage ceilings, thresholds. The question is whether that lives
  as **data** (rows the engine reads) or **code** (a class per country from day one).

FX enters here too: the employer total is also shown in the org's display currency, and
the rate source will change vendors (S12 is scheduled to do exactly that).

## Decisions

1. **Money is a value object** (`Atlas.Domain.Money`), backed by `decimal`.
   - `decimal`, never `double`: money is base-10 and exact; `double` is neither.
   - **Currency is part of identity.** Arithmetic is closed within one currency;
     cross-currency `+`/`-` throw `DomainRuleException`. There is no implicit conversion
     to or from `decimal` — attaching a currency is a deliberate act at each boundary.
   - **One rounding policy, written down and tested:** banker's rounding
     (`MidpointRounding.ToEven`) applied at a single `Rounded()` point. Arithmetic does
     not round; we compute in full precision and round once, so the result never depends
     on evaluation order.
   - **Allocation is exact:** `Allocate(n)` / `AllocateByWeights(...)` use the
     largest-remainder method so shares always sum to the total and no two differ by more
     than one minor unit. The indivisible cent is distributed, never dropped.
   - Money and `CurrencyCode` live in the **root** `Atlas.Domain` namespace (beside
     `DomainRuleException`), not in `Atlas.Domain.Money` — a type named `Money` inside a
     namespace also named `Money` collides for every sibling file. See
     `docs/concepts/value-objects.md`.

2. **Country rules are DATA, v1** (`CountryRulePack`). Each country is one immutable
   record: currency, probation months, and a list of `BurdenComponent`s (name, rate,
   optional allowance, optional wage cap). The registry `CountryRulePacks.For(code)`
   throws for an unsupported country. The S03 probation table now lives on the pack; the
   old `CountryEmploymentRules` static delegates to it, so S03 code compiles unchanged.

3. **FX is a port** (`IFxRates`). The domain and use cases depend only on
   `Money Convert(Money, CurrencyCode)`. The only adapter today is a fixed in-code cross
   table (`FxRateTable`). This is ADR-0001's "ports for volatility" made concrete.

### The recorded bet (the reason this ADR exists)

> **Data-driven packs survive exactly until a country needs a conditional.** Germany's
> church tax is not a percentage — it depends on the employee's declared religion. The
> moment a rule cannot be expressed as `(rate, allowance, cap)`, we **promote packs to
> plugin classes**: an interface with one implementation per country, where today's data
> table becomes the default implementation. **Promotion trigger: Sprint 11.** Until a real
> conditional arrives, adding a country is adding a row, and that cheapness is the whole
> point.

The Reviewer argued for code-from-day-one and partially won: the trigger is now explicit
and dated rather than left to "we'll know it when we see it" (see the S04 debate thread).

## Rejected alternatives

- **Floating point (`double`) for money.** Fast, and wrong: `0.1 + 0.2 != 0.3`. Rounding
  errors are invisible per-operation and material per-quarter. Non-starter for a ledger.
- **Money as an integer count of minor units (`long cents`).** Genuinely tempting —
  exact, no rounding surface. Rejected for v1 because (a) it hard-codes a 2-digit minor
  unit and leaks that assumption into every caller, (b) FX and percentage math still need
  a scale/round step, so the rounding decision doesn't actually disappear, and (c) a bare
  `long` loses the currency again. `decimal` + an explicit `Rounded()` keeps the rounding
  policy in one visible place. Revisit if profiling ever shows decimal math is hot (it
  won't at quote volumes).
- **Policy as code from day one** (a `class GermanyRules : ICountryRules`). Maximum
  flexibility, paid for up front on four countries that are all just `(rate, cap)` today —
  four near-identical classes and a lot of ceremony to express a spreadsheet. We take the
  data path and record the promotion trigger instead of paying the abstraction tax before
  the volatility arrives.

## Consequences

- EF owns `Money` as a value object (`OwnsOne`) — `Gross_Amount`/`Gross_Currency` columns
  via table splitting, and an owned `quote_lines` collection. `ModelMappingTests` keeps
  every owned property honest.
- A quote is an **immutable snapshot**: it stores the numbers (including the converted
  display total at the rate then in effect) and never recomputes. "Quotes estimate;
  payroll executes" (S7) depends on this.
- **New dependency — CsCheck** (test-only, `Atlas.Domain.Tests` only): property-based
  testing for Money's algebra (closure, exact allocation). Justification per CONTRIBUTING:
  Money's guarantees are laws over all inputs, and stating them as laws catches the input
  we didn't think to write down. Scoped to one pure-logic test project.

### Known simplifications (deliberate, scheduled)

- **Minor-unit scale is fixed at 2.** Correct for EUR/GBP/PHP/USD; JPY (0) and BHD (3)
  would need a per-currency scale table. Deferred until such a currency is onboarded.
- **Org display currency is a hard-coded "USD".** A per-org display currency (a column on
  `ClientOrg`, chosen at signup) is future work — see NOTES.
- **The Atlas fee ceiling is a currency-blind `12,000`.** Illustrative; a real ceiling is
  currency-aware. Flagged in `QuoteCalculator` and revisited when fees become
  configurable.
