# Threat Modeling: Walk the Boundaries, Name the Curtains

**One-pager · Sprint 13 · pairs with ADR-0016 and `docs/security/threat-model.md`**

## The idea

A threat model is not a document you commission from a security team and frame on a wall. It is a
**habit**: at every place where trust changes hands, ask "what does the code on the other side of
this line get to assume — and what happens when the thing on the other side lies?" The places where
trust changes hands are called **trust boundaries**, and they are where an attacker lives, so they
are where your review attention goes. Everything else is bookkeeping.

## STRIDE is a checklist, not a religion

Walk each boundary through six failure kinds so you don't skip one because it was late on a Friday:

| Letter | Question at this boundary |
|---|---|
| **S**poofing | Can someone pretend to be who they aren't? |
| **T**ampering | Can they change data or messages in flight or at rest? |
| **R**epudiation | Can they act and then deny it — is there a trail? |
| **I**nfo disclosure | Can they read what they shouldn't (incl. *existence*)? |
| **D**enial of service | Can they make it unavailable or expensive? |
| **E**levation of privilege | Can they become more than they are? |

The point of the acronym is coverage, not ceremony. If a letter doesn't apply to a boundary, write
one line saying why and move on. STRIDE earns its keep by making you *look*, not by generating a
report.

## Walls and curtains

Every control is either a **wall** (a defense you actually have) or a **curtain** (a defense you
have *decided* to defer). Curtains are fine — you can't build everything at once. What is **not**
fine is a *forgotten* wall: a place everyone assumed was protected and no one checked. The whole
discipline reduces to one rule:

> **A curtain with an owner and a trigger is honest engineering. A wall you only imagine is an
> incident with a date on it.**

Atlas writes its curtains down: the plaintext webhook secret, the org-less outbox dispatcher, the
in-memory login throttle. Each names who owns it and what event pays it off. None of them can
surprise you, because they're on the map.

## The two failures this sprint taught

- **A one-layer guard hides leaks.** The cross-org worker read (AF-3) passed the authorization
  matrix, because the HTTP endpoint 404'd for an unrelated reason. The matrix was a *necessary* wall
  and an *insufficient* one. The fix wasn't only the two-line code change — it was adding the layer
  that could actually see the gap (a use-case-level cross-org test) and admitting, in the sweep test
  itself, what it can and cannot prove.
- **Make the wrong thing unrepresentable beats "remember to filter."** A global query filter that
  reads the ambient tenant is invisible — and silently does nothing where there is no ambient tenant
  (background jobs). A typed, org-scoped id forces every caller to *say* whose org it is. The
  compiler is a better reviewer than a Friday afternoon.

## The senior habit

When you inherit a system, find its threat model and its fitness functions first. Together they tell
you what the team actually defends versus what's in the wiki. If the threat model is all walls and no
curtains, be suspicious — it's not that the system is perfect; it's that no one wrote down where it
isn't.

## Try it

1. Pick any boundary in `threat-model.md` and add a STRIDE row the audit missed. Is it a wall, a
   curtain, or a forgotten wall?
2. Find a control that is *invisible* (runs without anyone stating intent). Ask where it silently
   no-ops. That silence is where the next AF-3 lives.
