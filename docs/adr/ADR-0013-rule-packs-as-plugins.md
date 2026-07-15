# ADR-0013: Country Rule Packs as Plugins

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 11)
- **Deciders:** [Author], [Reviewer]

## Context

Since S04, a country's rules have been a `CountryRulePack` record in a static table, with a bet
recorded in ADR-0005: when a country needs something the data shape can't express, promote packs to
plugins. S11 pays that bet AND uses it to prove the course thesis — *software optimized for change* —
by adding two countries with **zero core-code changes**, diff attached to the PR.

## Decision 1: `ICountryRulePack` — data by default, code by exception

Each country is now a class implementing `ICountryRulePack`. The contract is deliberately mostly
DATA (currency, probation, leave minimum, burden/deduction components, required documents) with a
narrow, **default-empty** escape hatch: two hooks (`AdditionalEmployerBurden`,
`AdditionalEmployeeDeductions`) that return computed statutory lines.

- A data-only country subclasses `DataDrivenRulePack` (S04's table, now the default implementation),
  passes its numbers, and inherits the empty hooks. Four v1 countries + Canada are exactly this.
- A code-bearing country overrides one hook. Japan is exactly this.

**Where the line is drawn (the promotion criteria, applied not theorized):** promote a value to a
hook only when `(rate, allowance, cap)` on gross cannot express it — a flat amount (not a rate), a
conditional toggle (no on/off in the shape), a non-decomposable bracket. Everything else stays data.
Japan's *health and pension are still data*; only its flat family-support levy graduated. The bar is
"the shape can't say it," not "this country feels special."

## Decision 2: discovery by assembly scan — magic, with its cost named

The registry reflects over the domain assembly and instantiates every `ICountryRulePack` with a
public parameterless constructor. Adding a country is adding a file; the registry never changes — the
open-closed principle with a receipt.

- **The cost (⚠️):** discovery is quieter than explicit registration. A pack without a parameterless
  ctor is silently skipped; two packs for one country would collide.
- **The mitigations:** the registry throws on a duplicate country at first touch (fail loud), and a
  fitness test (`CountryRulePackDiscoveryTests`) asserts every concrete pack type is discoverable —
  so a non-constructable pack goes red in CI, not missing in production.
- **Rejected: an explicit `[GermanyRulePack(), …]` list.** It's louder, but it's a line of core code
  per country — exactly the edit the thesis is trying to eliminate. We chose discovery and paid for
  its failure modes with tests.

## Decision 3: the calculators fold the hooks unconditionally

`QuoteCalculator` and `PayrollCalculator` iterate the data components AND the hook results. For the
four (now five) data-only countries the hook returns empty, so the fold is a no-op that changes no
existing number — the migration to plugins **changed no characterization test** (only type
annotations moved from `CountryRulePack` to `ICountryRulePack`). That is the migration you approve
quickly: it can't have altered behavior it doesn't touch.

## The debate: split logic across core and plugins

- **[Reviewer]:** two implementations of the gross-to-net hooks means an auditor now reads N files to
  understand payroll — the logic is split.
- **[Author]:** the split is the domain's real shape. Statutory rules ARE per-country; pretending one
  calculator covered them was the fiction. Mitigations: every hook override is tested per country, and
  the calculator's line labels name which pack contributed each line.
- **Resolution:** accepted, with a demand met below — a table of which countries override which hooks,
  so the split is navigable rather than surprising.

### Which countries override which hooks

| Country | Data components | Code hook overridden |
|---------|-----------------|----------------------|
| DEU, GBR, PHL, USA, CAN | ✅ all rules | — (pure data) |
| JPN | ✅ health, pension, employment insurance | `AdditionalEmployerBurden` (family-support levy) |

## Consequences

- Adding a country is a pack file + (if code-bearing) one override + its test. The PR's `diff --stat`
  shows core untouched by the JPN/CAN commits — the thesis, demonstrated.
- The compliance engine reads `RequiredDocuments` off the same plugin, so a new country's document
  requirements arrive with its pack, no endpoint change.
- Illustrative rates/ceilings are teaching values, not tax advice, in every pack.
