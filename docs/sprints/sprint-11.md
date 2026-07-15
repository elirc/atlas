# Sprint 11 — Compliance Engine & Rule Packs v2 as Plugins (The Thesis Proof)

**Goal:** promote country rule packs from data tables to plugins — then prove the architecture by
**adding two countries with zero core-code changes**, diff attached to the PR body.

## Scope

**In:** `ICountryRulePack` plugin contract (burden components, leave minimums, probation/notice
rules, required document kinds, gross-to-net hooks); existing four countries (DEU/PHL/USA/GBR)
migrated: data-table packs become the default `DataDrivenRulePack` implementation (S4's bet, paid
off exactly as ADR-0005 scheduled); registry + discovery (assembly scan of `Atlas.Domain.Countries`);
**compliance engine**: required documents per country, WorkerDocument with expiry, compliance
status rollup (worker → org), expiring-documents view; **the proof:** two new countries (JPN —
needs a code-level conditional rule; CAN — pure data) added as pack files only; PR body includes
`git diff --stat` per layer showing core untouched; compliance endpoints; `docs/http/compliance.http`.

**Out:** document binary storage, government e-filing, visa/right-to-work workflows.

## Teaching focus

The open-closed principle with receipts. Plugin contracts: how wide is too wide. When data
graduates to code (JPN's conditional rule is exactly the case S4's Reviewer predicted).

## Commit narrative (sketch)

1. `refactor(domain): ICountryRulePack contract + registry; four countries rewired, behavior pinned`
   (characterization tests from golden files untouched — the S8 lesson re-applied)
2. `feat(domain): compliance requirements + rollup engine (pure)`
3. `feat(application+infrastructure+api): worker documents, expiry, endpoints`
4. `feat(domain/countries): add JPN — first code-bearing rule pack` ← the S4 debate's payoff
5. `feat(domain/countries): add CAN — pure data pack`
6. `docs: ADR-0013 rule packs as plugins, concepts/open-closed.md` (+ diff-stat table in PR body)

## Teaching placements

- 📘 on ICountryRulePack: designing a plugin contract — minimal surface, versioned expectations.
- 📘 on the JPN pack: the data→code promotion criteria, applied not theorized.
- 🔍 on commit 1's diff: "a migration that changes no tests is the only kind you approve quickly."
- ⚠️ on assembly scanning: discovery magic vs explicit registration — what we chose and the cost.
- 🔗 back to S4's debate comment (link): "the Reviewer's collapse prediction, adjudicated."

## Debate

`[Reviewer]`: "Two implementations of gross-to-net hooks (pack overrides) means payroll's logic
is now split across core and plugins — an auditor has to read N files." `[Author]`: the split is
the domain's real shape: statutory rules ARE per-country; pretending one calculator covers them
was the fiction. Mitigation: every pack override is golden-file tested per country, and the
calculator logs which pack hook fired. Reviewer accepts with a demand: a `docs/` table of which
countries override which hooks — added.

## Exercises

- Add a third country (pick one) using only pack files. Attach your own diff --stat. Zero core?
- Break the plugin contract (rename a hook) and count how many failures name the problem clearly.
- The compliance rollup is a pure fold over documents. Rewrite it as SQL. Which wins here, and why?
