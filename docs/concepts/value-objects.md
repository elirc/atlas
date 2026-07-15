# Value Objects: Types That Make Bugs Not Compile

**One-pager · Sprint 04 · pairs with ADR-0005**

## The idea

A value object is a small immutable type defined entirely by its value, that verifies its
own rules once, at construction, so nothing downstream ever has to. Atlas already had two
(`CountryCode`, `EmailAddress`); S04 adds the two that carry money: `CurrencyCode` and
`Money`.

The test for "should this be a value object?" is: **does a bare primitive here accept
values that are nonsense?** A `string country` accepts `"Germany"`, `"de"`, and `""`. A
`decimal amount` with a `string currency` accepts `€10 + $10`. Each accepted nonsense is a
bug waiting for the worst possible input.

## Money, and the three decisions inside it

1. **`decimal`, not `double`.** Money is base-10 and exact. `double` cannot represent
   `0.10`, so `0.1 + 0.2` is `0.30000000000000004`. Use `double` for physics, `decimal`
   for ledgers.
2. **Currency is identity, not a label.** `Money.Of(10, EUR)` and `Money.Of(10, USD)` are
   not "10 with a note attached" — they are different values, and their sum is undefined
   without a rate. So the type refuses it: cross-currency `+` throws. The bug that would
   have been a wrong number becomes an exception at the exact line that made the mistake.
3. **Rounding is a policy, chosen once.** Atlas rounds half-to-**even** (banker's), at one
   `Rounded()` method. Away-from-zero rounding biases every half-cent upward; across
   thousands of lines that is a real, accumulating drift. Pick one, write it down, test it
   (`MoneyTests.Rounded_uses_bankers_rounding`).

## The cent that won't divide

`10.00 / 3` has no answer in two decimal places. Naive rounding gives `3.33 × 3 = 9.99`
and a cent vanishes. `Money.Allocate(3)` uses **largest-remainder**: hand the leftover
minor units out one per share. Result `[3.34, 3.33, 3.33]`, and the sum is exactly
`10.00`. The property test states the law for *every* amount and part count:

> the shares always sum to the total, and no two differ by more than one minor unit.

That is the difference between an example ("I checked 10 into 3") and a guarantee.

## A naming trap worth knowing

`Money` and `CurrencyCode` live in the **root** `Atlas.Domain` namespace, not in a
`Atlas.Domain.Money` namespace matching their folder. A type named `Money` inside a
namespace also named `Money` collides: for any sibling `Atlas.Domain.*` file, the simple
name `Money` can resolve to the *namespace* instead of the type. Rooting the type — beside
`DomainRuleException` — sidesteps it and makes Money visible to every aggregate with no
`using`. Sometimes the folder and the namespace disagree on purpose.

## Where this goes next

- **S07 (payroll)** consumes employment terms as `Money` and runs gross-to-net — the same
  rounding and allocation rules, now moving real money. `EmploymentRecord`'s
  `decimal + string` still waits its turn there.
- **S13 (hardening)** is where money bugs get hunted with property tests as the weapon.
- **Rule packs** (data now) get promoted to plugins in **S11** when a country needs a
  conditional — the value-object lifecycle, one more time, at a larger scale.

## Try it

1. Write `Money.Of(10, eur) + Money.Of(10, usd)` in a test and read the exception. Now
   imagine that as a silent `20` in a payroll run. Which failure would you rather have?
2. Delete `MoneyPropertyTests.Allocate_sums_to_the_total...` and try to hand-write example
   cases that give the same confidence. Report how many you needed — and whether you'd
   trust them at 2 a.m.
