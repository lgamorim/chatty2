# Testing — Test-Driven Development

YOU MUST follow a test-driven development approach for all production code:

1. **Red** — write a failing test that specifies the desired behavior before
   writing implementation code.
2. **Green** — write the minimum implementation to make the test pass.
3. **Refactor** — clean up the code and tests while keeping everything green.

Rules:

- Never write production code without a failing test that motivates it. Never
  mark a task complete while any test fails. Exception: when porting or adapting
  already-designed code (e.g., a pre-written draft) rather than discovering new
  behavior, there is no red step — treat `dotnet test` passing as the gate.
- Ensure all relevant edge cases are covered: null/empty inputs, boundary
  values, invalid state transitions, concurrency where applicable, and failure
  paths (exceptions, timeouts, cancellation via `CancellationToken`).
- When a task explicitly scopes test coverage (e.g., "one test", "happy path
  only"), that explicit scope takes precedence over this checklist — but say so
  out loud (commit message, notes, or conversation) rather than silently
  under-testing.
- Test naming: `Should_ExpectedOutcome_When_Scenario` (e.g.,
  `Should_ThrowInsufficientStockException_When_InventoryInsufficient`).
- Structure tests as Arrange–Act–Assert.
- Unit tests must be fast and deterministic: no real I/O, network, clock, or
  `Task.Delay`. Abstract time behind `TimeProvider`.
- Run `dotnet test` after every change and before declaring any work finished.
