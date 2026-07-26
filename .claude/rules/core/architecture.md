# Architecture

Rules governing physical project and solution structure (as opposed to logical
design, which lives in `design-principles.md`). This is the invariant skeleton;
archetype files add the parts that vary — test-project naming, `bench/`,
integration-test projects, packaging.

- Source lives under `src/<ProjectName>/`; tests under `test/` (singular).
- One solution file (`.slnx`) per repo, at its root, referencing every project
  beneath it.
- Centralize shared MSBuild properties (`TargetFramework`, `Nullable`,
  `TreatWarningsAsErrors`, etc.) in a `Directory.Build.props` at that same root
  instead of repeating them per `.csproj`.
