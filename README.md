# AeroDriver

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)

**AeroDriver** is a lightweight and practical driver management tool for Windows systems focusing on WHQL certified drivers for stability.

## ✨ Features

- **WHQL Certified Drivers Only**: Install only high-quality drivers that meet Microsoft's strict testing standards
- **Intelligent Driver Detection**: High-performance WMI-based driver enumeration with filtering and optimization
- **Windows Update Catalog Integration**: Direct integration with Microsoft's official driver sources
- **Advanced Caching System**: Smart caching for improved performance and reduced system load
- **Comprehensive Health Monitoring**: Real-time system health assessment with actionable recommendations
- **Automated Maintenance**: Smart cleanup of old backups, temporary files, and cache data
- **10 Language Support**: Supports major languages including English, Japanese, Chinese, German, Spanish, French, Italian, Korean, Portuguese, and Russian
- **Automatic Language Detection**: Automatically selects the optimal language based on system settings
- **3-Generation Backup**: Automatically manages and deletes old backups
- **Enhanced Error Handling**: Robust exception management with detailed logging and recovery

## 📋 System Requirements

- Windows 10/11 (64-bit)
- Intel Core i3/AMD Ryzen 3 or higher
- 4GB RAM
- 1GB free disk space

## 🚀 Installation

### Using Command Line
```bash
# 最も実用的なワンクリックコマンド（推奨）
dotnet run --project src/AeroDriver.CLI -- auto

# 診断・メンテナンスコマンド
dotnet run --project src/AeroDriver.CLI -- health   # 包括的ヘルスレポート
dotnet run --project src/AeroDriver.CLI -- scan     # ドライバー更新スキャン
dotnet run --project src/AeroDriver.CLI -- diag     # システム診断
dotnet run --project src/AeroDriver.CLI -- fix      # 問題修復提案
dotnet run --project src/AeroDriver.CLI -- info     # システム情報表示

# クリーンアップとキャッシュ管理
dotnet run --project src/AeroDriver.CLI -- cleanup          # 全体クリーンアップ
dotnet run --project src/AeroDriver.CLI -- cleanup backups  # バックアップクリーンアップ
dotnet run --project src/AeroDriver.CLI -- cache clear      # キャッシュクリア

# 新機能 - レポート生成とログ管理
dotnet run --project src/AeroDriver.CLI -- report quick --format json --output report.json
dotnet run --project src/AeroDriver.CLI -- logs errors
dotnet run --project src/AeroDriver.CLI -- monitor  # パフォーマンス監視
dotnet run --project src/AeroDriver.CLI -- autoupdate status  # 自動更新状態

# 高度なオプション
dotnet run --project src/AeroDriver.CLI -- auto --force --no-backup  # 強制更新（バックアップなし）
dotnet run --project src/AeroDriver.CLI -- scan --verbose            # 詳細情報付きスキャン
dotnet run --project src/AeroDriver.CLI -- health --format html --output health.html
```

## 🌍 Language Support

AeroDriver currently supports 10 major languages:

- **English** (en-US) - Default
- **Japanese** (ja-JP) - 日本語
- **Chinese Simplified** (zh-CN) - 简体中文
- **Korean** (ko-KR) - 한국어
- **German** (de-DE) - Deutsch
- **Spanish** (es-ES) - Español
- **French** (fr-FR) - Français
- **Italian** (it-IT) - Italiano
- **Portuguese Brazilian** (pt-BR) - Português
- **Russian** (ru-RU) - Русский

---

## 🌍 多言語対応

AeroDriverは現在10の主要言語に対応しています：

- **英語** (en-US) - デフォルト
- **日本語** (ja-JP) - Japanese
- **中国語簡体字** (zh-CN) - Chinese Simplified
- **韓国語** (ko-KR) - Korean
- **ドイツ語** (de-DE) - German
- **スペイン語** (es-ES) - Spanish
- **フランス語** (fr-FR) - French
- **イタリア語** (it-IT) - Italian
- **ポルトガル語ブラジル** (pt-BR) - Portuguese Brazilian
- **ロシア語** (ru-RU) - Russian

## 🧩 アーキテクチャ

AeroDriverは以下の主要コンポーネントで構成されています：

- **DriverService**: 高性能ドライバー検出と更新管理（キャッシュ対応）
- **WhqlDatabaseService**: Microsoft Windows Update Catalogとの連携
- **BackupService**: バックアップと復元機能
- **SystemHealthService**: システム包括的ヘルス監視
- **CacheService**: インメモリキャッシュシステム
- **CleanupService**: 自動メンテナンスとクリーンアップ
- **LanguageService**: 10言語対応の多言語システム
- **SettingsService**: ユーザー設定の管理

## 🛠️ 開発者向け情報

### リポジトリのクローン

```bash
git clone https://github.com/shizukutanaka/aerodriver.git
cd aerodriver
```

### 開発環境のセットアップ

```bash
# 必要なパッケージのインストール
dotnet restore

# ビルド
dotnet build

# 実行
dotnet run
```

### 必要な NuGet パッケージ

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.1" />
<PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Localization" Version="8.0.1" />
<PackageReference Include="System.Management" Version="8.0.0" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

## 📝 ライセンス

MITライセンスの下で公開されています。詳細は[LICENSE](LICENSE)ファイルを参照してください。


## 🤝 コントリビューション

プルリクエストは大歓迎です。大きな変更を加える場合は、まず問題を提起して議論してください。

詳細は[CONTRIBUTING.md](CONTRIBUTING.md)を参照してください。


---

© 2025 Shizuku Tanaka. All Rights Reserved.
