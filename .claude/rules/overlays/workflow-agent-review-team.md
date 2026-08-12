# Workflow — Agent-assisted review (team, PR-based)

Add when a repo runs a standing two-role flow: one agent implements a change and
opens the pull request, a separate agent reviews that PR. Compose with
`workflow-team.md` — the flow presupposes a PR, so it does not combine with
`workflow-solo.md` (a solo repo without PRs uses
`workflow-agent-review-solo.md` instead).

This overlay defines the *contract* between the two roles. It deliberately says
nothing about which models run or how the agents are triggered (in-session
subagents, a CI action, an SDK harness) — that wiring is repo automation,
configured outside the rule set, not policy that belongs here. A starting
skeleton for the reviewer agent lives in `templates/reviewer-subagent.md`.

Roles — never the same context:
- **Implementer** — delivers the change on a `feature/` branch and opens the PR.
  The PR description states the intent, the tests added, and any deliberate rule
  deviations, so the reviewer judges them as choices rather than misses.
- **Reviewer** — reads the diff fresh, with none of the implementer's working
  context — the PR description is the only implementer-authored input it
  receives. It checks the change against the rules this repo already imports,
  plus any path-scoped rules matching the touched files, and leaves inline
  comments. It never pushes, never merges, and never resolves its own comments.

Boundaries:
- The reviewer advises; it does not gate. A human adjudicates — disagreements
  surface to the maintainer, they are not auto-resolved between agents, and no
  agent merges its own or the other's work.
- The reviewer's authority is the repo's rule set — the imported modules plus
  any path-scoped rules matching the touched files: each finding cites the
  specific rule it violates. The reviewer does not invent conventions in the
  review, and "not how I would have written it" is not a finding unless a rule
  says so.
- Keep the two contexts genuinely separate. An agent reviewing a diff it wrote
  re-checks its own assumptions — the exact failure this flow exists to avoid.
