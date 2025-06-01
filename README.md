# AeroDriver

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-7.0-blueviolet)
[![Build Status](https://github.com/shizukutanaka/AeroDriver/actions/workflows/build.yml/badge.svg)](https://github.com/shizukutanaka/AeroDriver/actions)

![AeroDriver Logo](assets/images/logo/aerodriver-logo.png)

**AeroDriver** is a comprehensive driver management tool for Windows systems that focuses on using only WHQL (Windows Hardware Quality Labs) certified drivers to ensure system stability and compatibility. With a sleek interface supporting 300 languages, users worldwide can easily operate the application in their native language.

## ✨ Features

- **WHQL Certified Drivers Only**: Install only high-quality drivers that meet Microsoft's strict testing standards
- **Automatic Driver Detection**: Accurately detect all drivers in the system using WMI
- **Windows Update Catalog Integration**: Direct integration with Microsoft's official driver sources
- **300+ Language Support**: Covers nearly all major languages worldwide
- **Automatic Language Detection**: Automatically selects the optimal language based on system settings
- **3-Generation Backup**: Automatically manages and deletes old backups
- **30+ Themes**: Rich design variations to suit all preferences
- **BTC Donation**: Cryptocurrency donation system to support development

## 📋 System Requirements

- Windows 10/11 (64-bit)
- Intel Core i3/AMD Ryzen 3 or higher
- 4GB RAM
- 1GB free disk space

## 🚀 Installation

### Using Installer
1. Download the latest version from the [Releases](https://github.com/shizukutanaka/AeroDriver/releases) page
2. Run the installer
3. Follow the on-screen instructions to complete the installation
4. AeroDriver will automatically detect your system language and start in the appropriate language

### Using Command Line
```bash
# After installation, you can run from the command line
aerodriver --scan
```

## 📸 Screenshots

![Main Dashboard](assets/images/screenshots/dashboard.png)
![Driver Update Screen](assets/images/screenshots/update-screen.png)
![WHQL Database](assets/images/screenshots/whql-database.png)

## 🌍 Language Support

AeroDriver supports 300+ languages, covering the following language groups:

- **East Asian Languages**: Japanese, Chinese (Simplified/Traditional), Korean, etc.
- **Western European Languages**: English, French, German, Spanish, etc.
- **Eastern European Languages**: Russian, Polish, Ukrainian, etc.
- **Middle Eastern Languages**: Arabic, Hebrew, Persian, etc.
- **South Asian Languages**: Hindi, Bengali, Tamil, etc.
- **Southeast Asian Languages**: Indonesian, Thai, Vietnamese, etc.

---

# AeroDriver

AeroDriverは、Windows向けのシンプルなドライバー管理ツールです。Microsoft認証済みのWHQLドライバーのみを使用し、システムの安定性と互換性を重視しています。

## 主な機能
- WHQL認証ドライバーのみインストール
- 自動ドライバー検出
- Windows Update Catalog連携

## システム要件
- Windows 10/11（64bit）
- Intel Core i3/AMD Ryzen 3以上
- 4GB RAM
- 1GB空き容量

## インストール方法
1. [リリースページ](https://github.com/shizukutanaka/AeroDriver/releases)から最新版をダウンロード
2. インストーラーを実行し、画面の指示に従ってインストール

## 使い方
インストール後、スタートメニューまたはコマンドラインから `AeroDriver` を起動してください。

---

This is the Minimum Viable Product (MVP) version. For details, visit the official repository.
aerodriver --scan
```

## 📊 スクリーンショット

![メインダッシュボード](assets/images/screenshots/dashboard.png)
![ドライバー更新画面](assets/images/screenshots/update-screen.png)
![WHQL認証データベース](assets/images/screenshots/whql-database.png)

## 🌍 多言語対応

AeroDriverは300以上の言語に対応しており、以下のような言語グループをカバーしています：

- **東アジア言語**: 日本語、中国語(簡体字/繁体字)、韓国語など
- **西ヨーロッパ言語**: 英語、フランス語、ドイツ語、スペイン語など
- **東ヨーロッパ言語**: ロシア語、ポーランド語、ウクライナ語など
- **中東言語**: アラビア語、ヘブライ語、ペルシャ語など
- **南アジア言語**: ヒンディー語、ベンガル語、タミル語など
- **東南アジア言語**: インドネシア語、タイ語、ベトナム語など
- **中東・南アジア言語**: アラビア語、ヘブライ語、ヒンディー語など
- **アフリカ言語**: スワヒリ語、アムハラ語、ズールー語など
- **その他多数**

詳細な言語リストは[対応言語ドキュメント](docs/supported-languages.md)を参照してください。

## 🧩 アーキテクチャ

AeroDriverは以下の主要コンポーネントで構成されています：

- **DriverService**: ドライバー検出と更新管理
- **WhqlDatabaseService**: Microsoft Windows Update Catalogとの連携
- **BackupService**: バックアップと復元機能
- **LanguageService**: 300言語対応の多言語システム
- **FontService**: 言語ごとの最適フォント管理
- **SettingsService**: ユーザー設定の管理
- **NotificationService**: システム通知管理

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
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="7.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="7.0.0" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="QRCoder" Version="1.4.3" />
<PackageReference Include="System.Management" Version="7.0.2" />
```

## 📝 ライセンス

MITライセンスの下で公開されています。詳細は[LICENSE](LICENSE)ファイルを参照してください。

## 💰 開発サポート

AeroDriverの開発をサポートするには、以下のBTCアドレスに寄付してください：

```
1GzHriuokSrZYAZEEWoL7eeCCXsX3WyLHa
```

## 🤝 コントリビューション

プルリクエストは大歓迎です。大きな変更を加える場合は、まず問題を提起して議論してください。

詳細は[CONTRIBUTING.md](CONTRIBUTING.md)を参照してください。


---

© 2025 Shizuku Tanaka. All Rights Reserved.
