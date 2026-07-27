# 1. Record architecture decisions

- Status: Accepted
- Date: 2026-07-27
- Deciders: Peter Falkesand

## Context

We need to record the architectural decisions made on this project — the ones a
new contributor (human or agent) cannot infer from the code alone: why a
boundary exists, why one library beat another, what a constraint is protecting.

Without a durable record, every session re-litigates settled questions or, worse,
silently reverses a decision whose rationale was never written down.

## Decision

We will use Architecture Decision Records, as described by Michael Nygard.

- One ADR per file: `docs/adr/NNNN-short-title.md`, numbered sequentially and
  never renumbered.
- Copy `NNNN-template.md` for each new record.
- An ADR is immutable once Accepted. To change a decision, add a NEW ADR that
  supersedes the old one and set the old ADR's Status to
  `Superseded by ADR-NNNN`. Never edit a decision away — the history is the point.
- Write an ADR for any non-obvious decision: a new boundary or module, a
  dependency choice, a persistence/transport/protocol choice, a security or
  performance trade-off, or anything a reviewer asked "why is it done this way?"

## Consequences

- Design intent survives across sessions; agents read ADRs before proposing
  changes and surface conflicts instead of blending or reversing them.
- A small, ongoing documentation cost per real decision — deliberately not for
  trivial or reversible choices.
- The `docs/adr/` directory becomes the first stop for onboarding and for
  architecture review (`improve-codebase-architecture` skill consumes it).
