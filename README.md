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
- **CLI**: `scan`, `update` (`--install-all` for ordered batch install), `install --device-id <id>`, `rollback --device-id <id>`, `details --device-id <id>`
- **GUI** (`AeroDriver.UI`): WPF/MVVM front end sharing the same core services — installed-driver and available-update tabs, scan / check-updates / install-selected / **install-all (in dependency order)** / rollback with cancellable progress, custom-file (.inf/.exe/.msi/.cab) install, a driver detail pane (double-click), live language switching across all 10 cultures, and light/dark theme switching
- **BYOVD protection**: rejects known-vulnerable drivers by SHA256 against the free LOLDrivers list on every install/restore path
- **Localization**: 10 languages (en, ja, zh-CN, ko, fr, es, de, it, pt-BR, ru), auto-detected from the OS UI culture with en-US fallback

## 📋 System Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 runtime

## 🚀 Installation

### Command Line
```bash
dotnet build
dotnet run --project src/AeroDriver.CLI -- scan
```

### GUI
```bash
dotnet run --project src/AeroDriver.UI
```

## 🧩 Architecture

- **DriverService**: driver detection and update orchestration
- **WhqlDatabaseService**: Windows Update Catalog lookups
- **BackupService**: real driver file backup/restore via pnputil
- **PciIdDatabase**: vendor/device ID resolution (pci-ids.ucw.cz mirror)
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
