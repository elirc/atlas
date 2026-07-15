# Fitness Functions: Architecture You Can Fail

**One-pager · Sprint 01 · pairs with ADR-0001**

## The idea

An architectural principle that lives in a diagram is a hope. The same principle expressed as a
test is a *fitness function*: an automated check that fails the build when the architecture
degrades. Atlas's dependency rule is not documentation — it is `tests/Atlas.ArchitectureTests`,
red or green on every PR.

## Why teams need them

Erosion is never a decision. It's a Tuesday: a deadline, an endpoint that needs one EF query,
a `using Atlas.Infrastructure;` that saves twenty minutes. Each step is locally reasonable;
eighteen months later the "hexagon" is a ball of mud with project boundaries as decoration.
A fitness function converts each of those Tuesdays from a silent erosion into a red build that
forces the conversation *now*, when the fix costs minutes.

## What Atlas enforces (Sprint 01)

| Rule | Test |
|---|---|
| Domain references no layer, no framework | `Domain_depends_on_no_other_layer_and_no_framework` |
| Application references only Domain | `Application_depends_only_on_Domain` |
| Contracts are a dependency-free leaf | `Contracts_are_a_leaf_with_no_atlas_dependencies` |
| Infrastructure never reaches up | `Infrastructure_never_reaches_up_into_Api_or_Contracts` |
| Api touches Infrastructure only in the composition root | `Api_touches_Infrastructure_only_inside_the_composition_root` |

More arrive with the features that need them: the OpenAPI drift gate (also Sprint 01) is a
fitness function for the *contract*; Sprint 13 adds security gates; Sprint 14's report queries
get a "reads stay off the write model" rule.

## The senior habit

When you read an unfamiliar codebase, look for its fitness functions first. They tell you which
principles the team actually enforces, as opposed to the ones in the wiki. If there are none,
the architecture is whatever the last six months of deadlines made it.

## Try it

1. Add `using Atlas.Infrastructure;` + a `AtlasDbContext` field to `CountryEndpoints`. Run
   `dotnet test`. Read the failure message — it names the offending type.
2. Harder: find a violation these tests would *miss*. (Hint: reflection, service location.)
   That gap is why fitness functions complement review rather than replace it.
