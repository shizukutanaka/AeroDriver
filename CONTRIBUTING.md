# Contributing to AeroDriver

Thanks for your interest in improving AeroDriver.

## Before you start

For anything beyond a small fix, please open an issue first to discuss the
change. This avoids wasted effort on pull requests that don't fit the
project's direction.

## Development setup

```bash
git clone https://github.com/shizukutanaka/aerodriver.git
cd aerodriver
dotnet restore
dotnet build
dotnet test
```

Note: `AeroDriver.Core` uses Windows-only APIs (CimSession, pnputil,
Windows Update Agent COM interop), and `AeroDriver.UI` targets
`net8.0-windows` (WPF). Full build/run requires Windows; `AeroDriver.Core`
alone can be edited and unit-tested cross-platform since its test suite
avoids calling into the Windows-only code paths directly.

## Guidelines

- Keep pull requests focused on a single change.
- Add or update tests for behavior changes in `AeroDriver.Core`.
- This project has a strict no-cost policy: no paid dependencies, no
  services requiring a paid tier, no telemetry that phones home.
- If you're not sure a change fits, open an issue before writing code.

## License

By contributing, you agree that your contributions will be licensed under
the project's [MIT License](LICENSE).
