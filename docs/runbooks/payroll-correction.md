# Runbook: Payroll Correction / Re-execution

**When:** a payroll run executed with wrong inputs, needs re-running after a fix, or a run is stuck.
**Severity:** SEV1 if incorrect net pay reached anyone; SEV2 otherwise. **Always a second pair of
eyes** — this runbook moves money.

## What is safe and what is not (ADR-0008)

- `ExecutePayrollRun` has a **natural-key guard** (S07): re-executing an already-executed run is a
  domain **no-op**, not a second payment. On top of that, the `/execute` endpoint requires an
  **Idempotency-Key** (S10), so a client retry replays the original response. **Two layers that agree,
  not fight** — the middleware dedupes identical retries at the edge; the use case makes a genuine
  re-entry a no-op (concepts/at-least-once.md).
- Therefore: **re-calling execute on the same run is safe.** It will not double-pay. What is *not*
  safe is inventing a way around the guard (hand-editing run status, deleting payslips) to "force" a
  re-run. Payslips are immutable once a run is executed (S07) for exactly this reason.

## Decide which correction you need

1. **Run not yet executed (Draft/Prepared/Submitted/Approved), inputs wrong.**
   The clean path: reject/re-prepare with corrected inputs. No money has moved. Prefer this always —
   corrections before execution are cheap and leave no reconciliation.

2. **Run executed, but a retry/timeout left the *client* unsure it happened.**
   Not a correction — an idempotency question. Re-call `/execute` with the **same Idempotency-Key**
   the client used; it replays the original result. If a fresh key is used, the natural-key guard
   still makes it a no-op and returns the existing run. Either way: no double pay. Confirm the
   `payroll.run.executed` event is in the outbox/delivered before telling anyone it is done.

3. **Run executed with genuinely wrong amounts (bad terms, wrong FX, vendor divergence).**
   This is a **correction run**, not a re-execution. You do not mutate the executed run — it is a
   permanent, immutable record of what happened, which is what makes the audit and the S14 cost report
   trustworthy. Instead:
   - Fix the root input (effective-dated employment terms, FX table, vendor assignment).
   - Issue a **correcting run** for the delta in the next cycle (or an off-cycle correction run),
     documented against the original. The immutable original + a correcting entry is how payroll
     stays auditable; overwriting history is how it stops being.

## Procedure for a correction run

```http
### Confirm the original run's executed state and payslips (immutable — you are reading, not editing).
GET {{host}}/api/v1/payroll/runs/{{runId}}
Authorization: Bearer {{orgAdminToken}}

### Re-execute is a no-op if already executed — safe to confirm idempotency without moving money.
POST {{host}}/api/v1/payroll/runs/{{runId}}/execute
Authorization: Bearer {{orgAdminToken}}
Idempotency-Key: {{sameKeyAsOriginalIfKnown}}
```

- The delta (correction) is a **new** cycle/run with corrected inputs, not an edit of the old one.
- Every step is audited (S03 audit log records the acting user — including Atlas Ops acting inside an
  org). A correction with no audit trail is the finding a future auditor writes up.

## Vendor-divergence corrections (S12)

If the wrong amount came from a payroll **vendor** divergence, check the shadow-run comparison report
before correcting — it may already show the divergence and the correct figure. A cycle in flight
finishes on the vendor it started with (ADR-0014); the fix is at the next cycle boundary via the
effective-dated `VendorAssignment`, not a mid-run swap.

## Do NOT

- Do not delete or edit executed payslips. Immutability is a feature; a "quick fix" here is a SEV1 in
  waiting.
- Do not bypass the Idempotency-Key on `/execute` to "get it done". That key is your own safety net.
- Do not restore from backup to undo a bad run — you would lose every unrelated write since the
  backup. Correct forward.
