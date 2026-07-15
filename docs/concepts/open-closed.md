# Concept: Open for Extension, Closed for Modification (with Receipts)

The open-closed principle says a module should let you add behavior **without editing it**. It's easy
to say and easy to fake. S11 makes it falsifiable: adding two countries produces a `git diff --stat`
where core is untouched. If core had changed, the principle was aspirational, not real.

## The shape of the win

```
Add Japan + Canada:
 src/Atlas.Domain/Countries/Packs/JapanRulePack.cs   | +NN   (new file)
 src/Atlas.Domain/Countries/Packs/CanadaRulePack.cs  | +NN   (new file)
 tests/.../JapanRulePackTests.cs                      | +NN   (new file)
 ── and nothing else. No registry edit. No calculator edit. No endpoint edit. ──
```

The registry doesn't change because it **discovers** packs by reflection. The calculators don't
change because they already fold a **default-empty hook**. The endpoints don't change because they
ask the *plugin* what a country requires. Every seam that a new country touches was drawn to be
extended, not edited.

## How you actually get there

Three moves, none exotic:

1. **A contract, not a switch.** `ICountryRulePack` replaces `switch (country) { case "DEU": … }`.
   The switch is the anti-pattern: every new country edits it, and the compiler won't remind you.
2. **A default that covers the common case.** `DataDrivenRulePack` implements the whole contract from
   data, with the code hooks empty. A country that needs no code writes no code — the 80% case is
   free. This is the difference between "extensible" and "extensible but everyone pays the tax."
3. **A discovery mechanism.** Reflection here; it could be DI registration or a manifest. The point
   is that "the list of countries" is derived, not hand-maintained in a core file.

## When data must become code

Open-closed is not "never write code for a country." It's "adding a country doesn't edit *other*
code." Japan needed a flat conditional levy the data shape can't say — so it wrote code, in its own
file, behind a hook the contract already had. The **promotion criterion** is precise:

> Promote a value from data to a code hook only when `(rate, allowance, cap)` on gross cannot express
> it: a flat amount, a conditional toggle, a non-decomposable bracket.

Japan's health and pension stayed data. Only the levy graduated. "This country feels special" is not
a criterion; "the shape can't say it" is.

## The cost you must name

Discovery magic has a failure mode explicit registration doesn't: a pack that can't be constructed
(no parameterless ctor) is **silently skipped** rather than a compile error. We paid for that with a
fitness test that reflects over the assembly and asserts every pack is discoverable — so the failure
is red in CI, not a country quietly missing in production. Every convenience has an invoice; the
engineering is choosing which invoice you'd rather pay and then actually paying it.

## The tell

The next time someone claims a design is "extensible," ask for the diff that adds the next case. If
it edits a core file, the claim is a hope. If it's all new files, it's a receipt.
