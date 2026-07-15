# The Rule of Three (and why we extracted at two)

A one-pager on when to turn duplication into an abstraction.

## The rule, as usually stated

> The first time you do something, just do it. The second time, wince at the duplication but do it
> again. The third time, refactor.

The wisdom in it is a warning about **premature abstraction**: an abstraction built from ONE
example is a guess about which parts vary, and a wrong guess is more expensive than the duplication
it replaced — because now every future change has to fight the abstraction *and* the duplication it
failed to capture.

## Why Atlas extracted at TWO, not three

The rule of three is a heuristic, not a law. What actually matters is not the count — it is
**whether the shape is proven**. Two *genuinely similar* consumers can prove a shape more
convincingly than three near-identical copy-pastes. Atlas's approval flow is the case:

- **S06 (one consumer):** leave approval was written CONCRETE, on purpose. One example tells you
  nothing about which fields are essential and which are incidental. We wrote down the guess we
  were *not* making (a breadcrumb on `LeaveRequest`).
- **S08 (two consumers):** expenses arrived. The second consumer is where the shape reveals itself:
  what's shared (draft → submitted → approved/denied, cancellable), what diverges (an amount and a
  receipt; a sixth *Reimbursed* state; a denial reason). We could now abstract the shared part with
  evidence instead of a guess.
- **S09 (a third consumer, later):** invoices add a *two-step* approval — which is a different
  shape, not a parameter of this one. It does NOT go into the component; it earns its own type.
  That is the rule of three doing its real job: telling you a third thing is a different thing.

The count is a proxy. The real signal is: *do I now know which parts vary, from real use rather
than imagination?* At two proven consumers, we did.

## The two duplications, and which is cheaper

| Duplication | Cost | Verdict |
|---|---|---|
| Copy-paste of a **proven** shape (leave → expense, S08 commit 1) | Low, visible, deletable | Cheap. It's a specification you can read. |
| Abstraction of an **unproven** shape (a generic in S06) | High, hidden, sticky | Expensive. Every future change fights the wrong seams. |

> Today's duplication is tomorrow's specification. The copy-paste isn't the sin — *hardening a
> guess into an abstraction* is.

## How to extract safely (the S08 method)

1. **Copy-paste on purpose.** Let the second consumer be a duplicate first, so you can compare.
2. **Characterization tests.** Pin what BOTH copies currently DO — behaviour and message text —
   before you move a line. This is the safety net.
3. **Move behaviour, not tests.** Extract the shared part; rewire the consumers. If your
   characterization tests changed, you changed behaviour — that is not a refactor. The untouched,
   still-green test file is the proof.
4. **Write the YAGNI ledger.** Record what you did NOT abstract and why, in the code, where the
   next person will reach for it. Draw the buy-vs-build tripwire.

## The tell you extracted too early

You keep adding type parameters, flags, or "just one more hook" to the abstraction to fit the next
consumer. That's the abstraction telling you the shape was never proven. When the fix for a new
consumer is a second type parameter, stop and ask whether it's the same shape at all — or whether
you're one `TState` away from having built a framework nobody asked for (see ADR-0009's tripwire).
