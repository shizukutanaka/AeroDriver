# Contributing to AeroDriver

Thanks for your interest in improving AeroDriver.

## Before you start

For anything beyond a small fix, please open an issue first to discuss the
change. This avoids wasted effort on pull requests that don't fit the
project's direction.

Read [CLAUDE.md](CLAUDE.md) before writing code. It carries the project's
absolute rules — cancellation handling, `ConfigureAwait`, process argument
construction, atomic file replacement, fail-closed security checks, and a
strict no-cost policy (no paid dependencies, no telemetry). Most of those
rules are enforced mechanically, so a change that violates one is rejected
by the verification script rather than by a reviewer.

## Development setup

`AeroDriver.Core` uses Windows-only APIs (CimSession, pnputil, Windows
Update Agent COM interop) and `AeroDriver.UI` targets `net8.0-windows`
(WPF), so a full build and the xunit test suite require Windows. Most
work, however, can be done and verified on any platform.

### Any platform (Linux, macOS, Windows)

```bash
# .NET 8 SDK, e.g. on Ubuntu:
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0

tools/verify-all.sh
```

`tools/verify-all.sh` is the check to run before opening a pull request.
It needs no NuGet access: it compiles and **executes** the pure logic in
`AeroDriver.Core`, the `MainViewModel`, the DI container and the
localization pipeline against minimal stubs, type-checks every C# file in
the repository (including the xunit test project), and enforces the
mechanically-checkable rules from CLAUDE.md. Each harness under `tools/`
has its own README stating exactly what it does and does not cover.

### Windows

```powershell
pwsh -File tools/verify-windows.ps1
```

This is the acceptance test. It covers everything the cross-platform
script cannot reach: NuGet restore, XAML compilation, source-generator
output, `dotnet test`, real WMI queries, actual command-line parsing, and
the `dotnet publish` artifacts. A change is not finished until this
reports `0 failed`.

## Guidelines

- Keep pull requests focused on a single change.
- `tools/verify-all.sh` must pass before you open a pull request.
- Add coverage for behaviour changes. Pure logic goes in
  `tools/offline-verify` as a `Check()` assertion — that harness actually
  runs on every platform. The xunit suite in `tests/` is type-checked
  everywhere but only executed on Windows, so it should not be the only
  place a new invariant is asserted.
- When you add a new check to `tools/`, prove it works by breaking the
  thing it guards and confirming it fails. A check that has never caught
  anything may be checking nothing.
- Documentation must match the implementation. Don't write counts
  (assertion totals, resource key totals) into README.md, CLAUDE.md or
  this file — they drift. Let the verification output be the source of
  truth.
- No paid dependencies, no services requiring a paid tier, no telemetry
  that phones home. This is enforced by `tools/check-rule1.py`.
- If you're not sure a change fits, open an issue before writing code.

## License

By contributing, you agree that your contributions will be licensed under
the project's [MIT License](LICENSE).
