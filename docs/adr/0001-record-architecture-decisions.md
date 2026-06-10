# ADR 0001 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-06-10

## Context

scry has several load-bearing design decisions (transport, threading model, packaging) that future
contributors and agents will need the *reasoning* behind, not just the outcome. The implementation
handoff captured these in prose; we want them versioned alongside the code as it evolves.

## Decision

We keep lightweight Architecture Decision Records (ADRs) in `docs/adr/`, one Markdown file per
decision, numbered sequentially (`NNNN-title.md`). Each records context, the decision, and its
consequences. Superseded ADRs are kept and marked, not deleted.

## Consequences

- Decisions are discoverable in the repo and reviewed through normal pull requests.
- The format is deliberately minimal so writing one is cheap and they actually get written.
- This file (0001) establishes the practice; substantive decisions follow from 0002 onward.
