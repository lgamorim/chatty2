# Workflow — Core

Applies to every project regardless of team size or archetype. Compose with
exactly one of `overlays/workflow-solo.md` or `overlays/workflow-team.md`.

- Make minimal, focused changes; do not refactor unrelated code in the same
  change.
- One logical change per commit, with an imperative-mood message explaining
  *why*.
- In anything the forge renders — commit messages, PR titles/descriptions, and
  review comments — write `#N` (or an issue/PR URL) only for an item that
  actually exists and is being deliberately referenced; verify with
  `gh issue view N` / `gh pr view N` when in doubt. Every other number- or
  token-like reference — step numbers, analyzer IDs, external ticket keys,
  version numbers — is wrapped in backticks as inline code, so autolinking
  never fabricates a link and closing keywords (`Fixes #N`) never fire against
  the wrong item.
- When unsure between two designs, present both with trade-offs and ask before
  implementing.
- Never commit or push directly to the default branch (`master`/`main`). All
  work happens on a `feature/`-prefixed branch.
- Merge by squash so each feature arrives as a single logical commit on the
  default branch, consistent with the "one logical change per commit" rule
  above; the squash commit message keeps the imperative, *why*-focused form.
- Update the applicable workflow rule file when a new convention or correction
  is established.
