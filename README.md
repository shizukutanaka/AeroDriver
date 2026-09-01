# AeroDriver

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)

**AeroDriver** is a driver management tool for Windows that prioritizes WHQL
(Windows Hardware Quality Labs) certified drivers to keep systems stable.
It ships as both a command-line tool and a WPF GUI (`AeroDriver.UI`).

## ✨ Features (implemented today)

- **WHQL-aware installs**: Warns when a driver isn't WHQL certified, especially under WDAC kernel enforcement
- **Driver detection**: Enumerates installed drivers via `CimSession` (modern WMI) and `pnputil.exe`
- **Update sources**: Windows Update Agent (COM) and pnputil driver-store enumeration
- **Install-order planning**: the update list is ordered chipset/storage/bus → … → GPU so dependencies land before dependents in a batch install
- **Real file backup/restore**: `pnputil /export-driver` + `/add-driver` — not just metadata
- **Security-hardened installs**: HTTPS-only downloads, Authenticode signature verification, elevation checks, WQL-injection-safe queries
- **CLI**: `scan`, `update` (`--install-all` for ordered batch install), `install --device-id <id>`, `backups --device-id <id>`, `rollback --device-id <id> [--version <gen>]`, `details --device-id <id>`, `history` (audit trail of what was installed when), `config` (list/change settings)
- **GUI** (`AeroDriver.UI`): WPF/MVVM front end sharing the same core services — installed-driver and available-update tabs, scan / check-updates / install-selected / **install-all (in dependency order)** / rollback with cancellable progress, custom-file (.inf/.exe/.msi/.cab) install, a driver detail pane (double-click), live language switching across all 10 cultures (including grid headers and the detail pane), light/dark theme switching, and settings toggles (restore point / backup / beta drivers / check on startup)
- **BYOVD protection**: rejects known-vulnerable drivers by SHA256 against the free LOLDrivers list on every install/restore path
- **Localization**: 10 languages (en, ja, zh-CN, ko, fr, es, de, it, pt-BR, ru), auto-detected from the OS UI culture with en-US fallback. Every user-facing string in the GUI and CLI goes through the resource bundle — `tools/verify-all.sh` fails the build if a hardcoded one appears. Structured dumps (`details`, `history`) deliberately keep English field names that mirror the underlying WMI properties

## 📋 System Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 runtime

## 🚀 Installation

AeroDriver is distributed as source. Build it on Windows with the .NET 8 SDK:

```powershell
git clone https://github.com/shizukutanaka/AeroDriver.git
cd AeroDriver
dotnet publish src/AeroDriver.CLI -c Release -r win-x64 -o dist\cli
dotnet publish src/AeroDriver.UI  -c Release -r win-x64 -o dist\gui
```

Then run `dist\cli\AeroDriver.CLI.exe scan` or `dist\gui\AeroDriver.UI.exe`.
Both publish framework-dependent, so the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
must be installed. Add `--self-contained true` if you would rather not require it.

> **Do not** pass `-p:InvariantGlobalization=true` or narrow `SatelliteResourceLanguages`.
> Either one silently kills localization in the published output — every label becomes
> `[Button_Scan]`. `tools/verify-all.sh` fails the build if these are set in the project files.

### Elevation

Scanning, `details`, `backups`, `history` and `config` run unelevated. Installing, rolling
back and backing up modify the driver store and need an elevated prompt — the app does
**not** ship a `requireAdministrator` manifest, so read-only use never triggers UAC.
Unelevated write attempts return `AdminRequired` rather than failing obscurely.

### Development

```bash
dotnet run --project src/AeroDriver.CLI -- scan
dotnet run --project src/AeroDriver.UI
```

## 🧩 Architecture

- **DriverService**: driver detection and update orchestration
- **BackupService**: real driver file backup/restore via pnputil
- **AeroDriver.Languages**: localization framework (all 10 supported cultures translated)
- **AeroDriver.UI**: WPF/MVVM GUI (CommunityToolkit.Mvvm) over the shared core services

## 🗺️ Roadmap

- [x] WPF GUI (`AeroDriver.UI`) — scan/update/install/rollback, custom-file install, driver detail pane, live language switching, light/dark theme switching
- [x] Language translations for zh-CN, ko-KR, fr-FR, es-ES, de-DE, it-IT, pt-BR, ru-RU (all 10 supported cultures now have translated content)
- [x] Driver dependency ordering (chipset/storage/bus → … → GPU) applied to the update list

For a detailed breakdown of what's implemented, what's dead code, and what's
still an open decision, see [docs/FEATURE_AUDIT.md](docs/FEATURE_AUDIT.md).
For known strengths/weaknesses and the prioritized improvement backlog, see
[docs/IMPROVEMENT_BACKLOG.md](docs/IMPROVEMENT_BACKLOG.md); contributor/AI
working rules live in [CLAUDE.md](CLAUDE.md).

## ✅ Verification

```bash
tools/verify-all.sh              # everything checkable without Windows
pwsh -File tools/verify-windows.ps1   # the rest, on real Windows (syntax-checked here)
```

Core is compiled and executed for real (130 assertions), `MainViewModel` and the value
converters are executed too (111 assertions, against hand-written mocks and a real DI
container), the DI container itself is built and resolved with `ValidateOnBuild` and `ValidateScopes`
(16 assertions) — captive dependencies only ever surface at runtime — and the localization
pipeline is exercised end to end (24 assertions: resx compilation, satellite assemblies,
neutral fallback for cultures with no satellite).
The script also checks that no user-visible string is hardcoded in the XAML and that
every `{Binding ...}` name resolves to a real ViewModel or model member.
The remaining WPF and CLI code — and the whole xunit test suite — is type-checked
against minimal stubs because their packages cannot be restored here. The script also
validates the solution file and every `PackageReference`, two things that previously
broke the Windows build for reasons unrelated to Windows. This does **not** replace
`dotnet build AeroDriver.sln && dotnet test` on Windows — XAML compilation,
source-generator output, WMI behaviour and command-line parsing still need a real
build. Each tool's README states its own limits. `verify-windows.ps1` covers what only Windows can:
restore, build (XAML + source generators), `dotnet test`, CLI smoke against real WMI,
`dotnet publish` for both surfaces, satellite assemblies for all 9 translated cultures,
and launching the GUI to confirm it survives startup. The one thing it cannot automate is
switching the UI culture — `LanguageService` reads the OS user culture, so verify the
language combo box by hand.

## 🛠️ Development

```bash
git clone https://github.com/shizukutanaka/aerodriver.git
cd aerodriver
dotnet restore
dotnet build
dotnet test
```

## 📝 ライセンス

MITライセンスの下で公開されています。詳細は[LICENSE](LICENSE)ファイルを参照してください。

## 🤝 コントリビューション

プルリクエストは大歓迎です。大きな変更を加える場合は、まず問題を提起して議論してください。

詳細は[CONTRIBUTING.md](CONTRIBUTING.md)を参照してください。


---

© 2025 Shizuku Tanaka. Released under the MIT License.
