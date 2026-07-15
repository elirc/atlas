# ADR-0002: API Conventions — the Contract Before the Features

- **Status:** Accepted
- **Date:** 2026-07-14 (Sprint 01)
- **Deciders:** [Author], [Reviewer], [Stakeholder]

## Context

Atlas is API-only: the OpenAPI contract and the integration surface are the product's face.
Conventions decided per-endpoint become inconsistencies; conventions decided once, in the walking
skeleton, become rails every later sprint runs on.

## Decisions

1. **Versioned base path** `/api/v1/...`. After a version ships, evolution is by addition or by a
   new version — never a breaking change (S12 demonstrates v2 of one resource).
2. **Errors are RFC 7807 `application/problem+json`** with stable `type` URIs
   (`https://atlas.example/problems/<slug>`). Three families:
   - `not-found` → 404 (also used where the caller may not learn whether a resource exists)
   - `invalid-request` → 400 (malformed cursor, out-of-range limit)
   - `domain-rule-violated` → 422 (well-formed request; business said no)
   Clients switch on `type`, so `detail` text can improve without breaking anyone.
3. **Cursor pagination** on every list: `?cursor=` (opaque base64url token) + `?limit=`
   (1..100, default 25, out-of-range **rejected**, not clamped). The next-page token is returned
   in the **`X-Next-Cursor` response header**; the body stays a plain array of the resource.
   Ordering is `(CreatedAtUtc DESC, Id DESC)` with the Id tiebreak mandatory.
4. **OpenAPI is committed** (`docs/openapi/atlas-v1.json`) and drift-gated: a contract test
   regenerates the served document and diffs it against the committed one. Intentional changes
   are blessed via `ATLAS_UPDATE_OPENAPI=1 dotnet test` and reviewed as part of the diff.
5. **`Idempotency-Key`** will be honored on all money-moving POSTs (arrives with S10 middleware;
   the convention is contractual from day one so vendors/partners can build against it).
6. **Timestamps** are UTC `DateTimeOffset` serialized ISO-8601; calendar dates (HR dates) are
   `DateOnly` — never smuggled inside midnight timestamps.

## Why header-cursor instead of an envelope?

An envelope (`{ items: [], next: "..." }`) types every list twice in OpenAPI and forces clients
to unwrap; a header keeps resource payloads clean and generic clients can share one walker.
Cost: header metadata is invisible to naive JSON-only tooling — accepted, and the `.http` files
demonstrate the loop. (The Reviewer preferred the envelope; the debate thread on PR #7 records
the trade and the tiebreaker: our primary consumers are scripted integrators, not browsers.)

## Consequences

- Every new list endpoint inherits the cursor pattern (and its index) for free.
- The drift gate makes "update the spec" unforgettable — and makes PR diffs show contract changes
  in reviewable JSON.
- Rejecting bad limits (rather than clamping) surfaces client bugs at development time.
