# Penny Allocation: Making the Parts Sum to the Whole

**One-pager · Sprint 07 · pairs with ADR-0008**

There is no way to write three equal two-decimal numbers that add up to €10.00. `10.00 / 3 = 3.333…`,
and `3.33 × 3 = 9.99`. A cent vanished. Payroll does this dance twelve times a year, for every line of
every payslip — and "a cent vanished" times a few thousand workers times twelve months is a
reconciliation ticket, an angry auditor, and a real bug.

## The rule

> When you split one rounded amount into several, the shares must sum to **exactly** the original — no
> cent lost, no cent conjured.

Rounding each share independently violates this. **Allocation** is the operation that doesn't.

## Largest-remainder, worked

Split **€100,000** across **12** monthly periods.

1. Work in minor units (cents) so the maths is integer and exact: `10,000,000` cents.
2. Floor-divide: `10,000,000 / 12 = 833,333` cents each, with `10,000,000 − 833,333 × 12 = 4` cents
   left over.
3. Hand the 4 leftover cents to the first 4 shares (the ones with the largest division remainder —
   here every remainder is equal, so ties break toward the earliest period).

| Period | Cents | Euros |
|-------:|------:|------:|
| Jan–Apr (×4) | 833,334 | 8,333.34 |
| May–Dec (×8) | 833,333 | 8,333.33 |
| **Sum** | **10,000,000** | **100,000.00** |

`4 × 8,333.34 + 8 × 8,333.33 = 100,000.00`. Exactly. No two shares differ by more than one cent.

The naive alternative — `round(100,000 / 12) = 8,333.33`, applied twelve times — gives `99,999.96`.
Four cents evaporate from the worker's year, every year.

## Where Atlas uses it

`Money.Allocate(int parts)` (added in S04) is the primitive. `PayrollCalculator` calls it for the
period gross and every statutory line: the annual figure is rounded once, then allocated across the
year's 12 periods. The invariant is a test — `PayrollAllocationTests`: *the twelve monthly payslips
sum, line by line, to the annual figures.* It is the GREEN end of this sprint's RED→GREEN arc; the
naive version failed it on any salary not divisible by twelve.

`Money.AllocateByWeights(weights)` is the sibling for *proportional* splits (distribute a total across
lines in proportion to weights) — same guarantee, weighted floors plus largest-remainder.

## Golden files vs property tests

Both prove the calculator; they answer different questions.

- **Golden files** (`PayrollCalculatorGoldenTests`) show *one worker at one salary* with every number
  hand-computed in a comment. An auditor reads them like a worked example: "for €60,000, net is
  €4,020.00, here is why." Example-based tests are the ones a non-programmer can check.
- **Property tests** (`PayrollAllocationTests`) assert an *invariant over many inputs*: "for ANY
  salary, twelve periods sum to annual." They catch the class of bug a single example can't — the one
  that only appears when the number doesn't divide evenly.

Ship both. The golden file convinces the auditor; the property test catches the rounding drift the
auditor would never think to look for.

## Try it

1. Delete `Money.Allocate` from `PayrollCalculator.PeriodShareOf` and inline
   `Money.Of(annual.Amount / 12).Rounded()`. Run `PayrollAllocationTests` and read the failures — which
   salaries drift high, which drift low, and by how much?
2. Add a PHL 13th-month-pay rule (statutory) to the rule pack. Where must it be allocated — across 12
   periods, or paid whole in one? What does that choice do to the "twelve periods sum to annual" test?
3. A worker joins mid-year. Should their statutory annual figures still be split into 12, or into the
   periods they were actually employed? Write the test that pins your answer, then check whether the
   current code agrees.
