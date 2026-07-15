# ADR-0000: Course Charter — Atlas as a Teaching Repository

- **Status:** Accepted
- **Date:** 2026-07-14
- **Deciders:** [Author], [Stakeholder]

## Context

We want to take a junior engineer to mid/senior level in C#/.NET back-end engineering. Reading
books and toy tutorials doesn't transfer: the skills that matter — domain modeling, API contract
discipline, architecture that tolerates change, reading a diff like a reviewer — are learned from
**real codebases with visible decision history**. Real codebases, however, hide their history:
squashed rationale, deleted debates, invisible dead ends.

## Decision

Build a real product (Atlas, an EOR platform) **as a curriculum**: 15 sprints, each a single
squash-merged PR engineered as a chapter.

- The **thesis** of the course is *software is optimized for change*. Sprint 11 (add countries as
  plugins) and Sprint 12 (the pivot exam) exist to prove it with diffs, not claims.
- **Business logic and API design** are the twin subjects. There is no frontend; the OpenAPI
  contract and the integration surface (webhooks, outbox, idempotency, partner keys) are the
  product's face.
- Every PR carries: ordered teaching commits, 8–11 tagged inline review comments, one
  design-debate thread, an ADR, a concept one-pager, and exercises.
- **Five realistic flaws** are planted silently in early sprints and harvested later with
  RED→GREEN arcs. The ledger stays author-private until Sprint 15 reveals it. This teaches the
  most transferable reviewer skill: what a polite LGTM missed.
- All personas (`[Author]`, `[Reviewer]`, `[Stakeholder]`) are played by the course author from
  one GitHub account, prefix-tagged. The learner observes; they never ship product code here.

## Consequences

- The repo is optimized for *reading*, which sometimes costs terseness: commits are smaller and
  more narrated than a production team might bother with.
- CI must be deterministic and dependency-free (no DB, no Docker, no network) so every PR's gate
  is trustworthy years later.
- The planted flaws mean **main is knowingly imperfect between plant and harvest**. That is the
  point; the ledger reveal in Sprint 15 makes it auditable.
- Domain fidelity has limits: country rules are realistic in *shape* but simplified in *rates*;
  this is a curriculum about software design, not a payroll compliance reference.

## Rejected alternatives

- **A tutorial repo with final code only** — hides exactly the history that teaches.
- **The learner builds, the AI reviews** — slower feedback and the learner cannot yet produce the
  design mistakes worth studying; observation-first comes before production.
- **Microservices** — fashionable, but the change-tolerance thesis is better taught in a modular
  monolith where boundaries are enforced by tests instead of network calls.
