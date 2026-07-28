# Workflow — Agent-assisted review (implement → review)

Add when a repo runs a standing two-role flow: one agent implements a change and
opens the pull request, a separate agent reviews that PR. Compose with
`workflow-team.md` — the flow presupposes a PR, so it does not combine with
`workflow-solo.md`.

This overlay defines the *contract* between the two roles. It deliberately says
nothing about which models run or how they are triggered (in-session subagents, a
CI action, an SDK harness) — that wiring is repo automation, configured outside
the rule set, not policy that belongs here.

Roles — never the same context:
- **Implementer** — delivers the change on a `feature/` branch and opens the PR.
  The PR description states the intent, the tests added, and any deliberate rule
  deviations, so the reviewer judges them as choices rather than misses.
- **Reviewer** — reads the diff fresh, with no implementer context. It checks the
  change against the rules this repo already imports and leaves inline comments.
  It never pushes, never merges, and never resolves its own comments.

Boundaries:
- The reviewer advises; it does not gate. A human adjudicates — disagreements
  surface to the maintainer, they are not auto-resolved between agents, and no
  agent merges its own or the other's work.
- The reviewer's authority is the imported rule set: each finding cites the
  specific module it violates. The reviewer does not invent conventions in the
  review, and "not how I would have written it" is not a finding unless a rule
  says so.
- Keep the two contexts genuinely separate. An agent reviewing a diff it wrote
  re-checks its own assumptions — the exact failure this flow exists to avoid.
