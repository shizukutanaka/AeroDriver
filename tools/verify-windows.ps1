#Requires -Version 5.1
<#
.SYNOPSIS
    Windows 実機でのみ実行できる検証を一括で回す。

.DESCRIPTION
    tools/verify-all.sh は Linux で可能な検証(Core と MainViewModel の実行、
    型検査、リソース整合、sln 健全性)を回すが、以下には到達できない:

      - NuGet からの restore(この開発環境ではプロキシに遮断されている)
      - XAML のコンパイル(PresentationBuildTasks は Windows 専用)
      - ソースジェネレーター(CommunityToolkit.Mvvm)の実出力
      - 実 WMI クエリ / 実 pnputil / 実 WUA COM
      - System.CommandLine の実パース挙動
      - xunit テストの実行

    このスクリプトはその残り全部を1コマンドで回す。
    IMPROVEMENT_BACKLOG.md の P0「Windows実機でのビルドとテスト」がこれに当たる。

.EXAMPLE
    pwsh -File tools/verify-windows.ps1
    pwsh -File tools/verify-windows.ps1 -SkipSmoke
#>
[CmdletBinding()]
param(
    # scan / config など、システムを変更しない CLI コマンドの実起動を省く
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$script:pass = 0
$script:fail = 0

function Check {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:pass++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    } catch {
        $script:fail++
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        Write-Host "        $($_.Exception.Message)" -ForegroundColor DarkRed
    }
}

function Invoke-Checked {
    param([string]$Exe, [string[]]$Arguments, [string]$WorkDir = $repo)
    $out = & $Exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($out | Select-Object -Last 20 | Out-String)
    }
    return $out
}

Write-Host "== 前提 =="
Check ".NET SDK 8 が使える" { 
    $v = (& dotnet --version)
    if (-not $v) { throw "dotnet が見つかりません" }
    Write-Verbose "SDK $v"
}
Check "Windows で実行している" {
    if (-not $IsWindows -and $env:OS -ne 'Windows_NT') { throw "Windows 専用です" }
}

Write-Host "`n== restore / build (この開発環境で到達できない部分) =="
Check "dotnet restore AeroDriver.sln" {
    Invoke-Checked dotnet @('restore', "$repo\AeroDriver.sln") | Out-Null
}
# ここが通れば XAML コンパイルとソースジェネレーターの実出力も通っている
Check "dotnet build AeroDriver.sln (XAML/ジェネレーターを含む)" {
    Invoke-Checked dotnet @('build', "$repo\AeroDriver.sln", '-c', 'Release', '--no-restore') | Out-Null
}
# 注: -warnaserror は入れない。Core は net8.0 のまま [SupportedOSPlatform("windows")] 付きの
# ヘルパーを呼ぶため CA1416 が多数出るが、これらは実行時に OperatingSystem.IsWindows() で
# ガードされており誤検出。ここで落とすと本物の失敗が埋もれる

Write-Host "`n== テスト =="
Check "dotnet test (xunit)" {
    Invoke-Checked dotnet @('test', "$repo\AeroDriver.sln", '-c', 'Release', '--no-build') | Out-Null
}

if (-not $SkipSmoke) {
    Write-Host "`n== CLI スモーク (System.CommandLine の実パース。システムは変更しない) =="
    $cli = "$repo\src\AeroDriver.CLI\AeroDriver.CLI.csproj"

    Check "--help が終了コード0" {
        Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', '--help') | Out-Null
    }
    Check "config が現在の設定を表示する" {
        $out = Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', 'config')
        if (($out | Out-String) -notmatch 'restore-point') { throw "restore-point が出力に無い" }
    }
    Check "config --set が不正な値を拒否する (終了コード2)" {
        & dotnet run --project $cli -c Release --no-build -- config --set 'backup-generations=0' 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 2) { throw "期待した終了コード 2 ではなく $LASTEXITCODE" }
    }
    Check "未知のサブコマンドを拒否する (非0)" {
        & dotnet run --project $cli -c Release --no-build -- no-such-command 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { throw "不正なコマンドが成功してしまった" }
    }
    Check "scan が実 WMI を叩いて完了する" {
        Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', 'scan') | Out-Null
    }
    Check "history が終了コード0 (履歴が空でも)" {
        Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', 'history') | Out-Null
    }
}

Write-Host ""
if ($script:fail -eq 0) {
    Write-Host "verify-windows: $($script:pass) passed, 0 failed" -ForegroundColor Green
    Write-Host "IMPROVEMENT_BACKLOG.md の P0『Windows実機でのビルドとテスト』はこれで満たされます。"
    exit 0
} else {
    Write-Host "verify-windows: $($script:pass) passed, $($script:fail) failed" -ForegroundColor Red
    exit 1
}
