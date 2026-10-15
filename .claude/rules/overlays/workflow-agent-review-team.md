# Workflow — Agent-assisted review (team, PR-based)

Add when a repo runs a standing two-role flow: one agent implements a change and
opens the pull request, a separate agent reviews that PR. Compose with
`workflow-team.md` — the flow presupposes a PR, so it does not combine with
`workflow-solo.md` (a solo repo without PRs uses
`workflow-agent-review-solo.md` instead).

This overlay defines the *contract* between the two roles. It deliberately says
nothing about which models run them — that pinning is repo automation,
configured outside the rule set, not policy that belongs here. The reviewer
runs as the agent named `code-reviewer` in the consuming repo's
`.claude/agents/` when one exists, for which `templates/reviewer-subagent.md`
in this rule set's source repository is a skeleton, and otherwise as a fresh
in-session subagent given this contract; the maintainer may instead run it
in a separate session or a CI action. A `.claude/agents/` without that agent
is not a reason to skip the review, and an implementer never writes the
definition as part of the change it is delivering: adding it is a change of
its own.

Roles — never the same context:
- **Implementer** — delivers the change on a `feature/` branch and opens the PR.
  The PR description states the intent, the tests added, and any deliberate rule
  deviations, so the reviewer judges them as choices rather than misses.
- **Reviewer** — reads the diff fresh, with none of the implementer's working
  context — the PR description, and in a follow-up review the thread replies,
  are the only implementer-authored inputs it receives. It checks the change
  against the rules this repo already imports, plus any path-scoped rules
  matching the touched files, and leaves inline comments plus a summary
  comment naming the commit it reviewed. It never pushes, never merges, and
  never resolves its own comments.

Review rounds:
- A PR gets exactly one full review, in which the reviewer reads the PR's
  whole diff against its base branch.
- After the maintainer adjudicates and the accepted findings are fixed, any
  further review is a follow-up review. It covers only the commits pushed
  since the commit the previous review named (e.g.
  `git diff <reviewed-sha>..feature/x`). It answers exactly two questions —
  is each accepted finding resolved, and do the changed lines violate an
  imported rule — and does not reopen content outside that range.
- The follow-up reviewer's inputs are the implementer's replies on the
  accepted findings' comment threads, the reviewed commit's SHA, and the
  list of accepted findings. The SHA and the list come from the maintainer's
  adjudication, never from the implementer.
- A follow-up review is optional: the maintainer may waive it (e.g. for a
  one-line fix) and approve the PR directly. A second full review happens
  only when the maintainer asks for one, e.g. when the fixes changed the
  PR's scope substantially. Before it, an implementer context updates the
  PR description to describe the whole PR as it stands, and that description
  is the review's only implementer-authored input.

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
