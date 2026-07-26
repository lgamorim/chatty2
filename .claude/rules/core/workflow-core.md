# Workflow — Core

Applies to every project regardless of team size or archetype. Compose with
exactly one of `overlays/workflow-solo.md` or `overlays/workflow-team.md`.

- Make minimal, focused changes; do not refactor unrelated code in the same
  change.
- One logical change per commit, with an imperative-mood message explaining
  *why*.
- When unsure between two designs, present both with trade-offs and ask before
  implementing.
- Never commit or push directly to the default branch (`master`/`main`). All
  work happens on a `feature/`-prefixed branch.
- Merge by squash so each feature arrives as a single logical commit on the
  default branch, consistent with the "one logical change per commit" rule
  above; the squash commit message keeps the imperative, *why*-focused form.
- Update the applicable workflow rule file when a new convention or correction
  is established.
