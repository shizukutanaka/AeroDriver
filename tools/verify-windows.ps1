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

$script:skip = 0

# 戻り値(成功したか)は後続のスキップ判定に使う。使わない呼び出し側は
# `$null = Check ...` で受けること。受けないと True/False が出力に混ざる。
function Check {
    param(
        [string]$Name,
        [scriptblock]$Body,
        # $false のときは実行せずスキップする。restore が失敗した後に build を走らせても
        # 同じ原因で失敗するだけで、本当の失敗が大量のノイズに埋もれるため
        [bool]$When = $true
    )
    if (-not $When) {
        $script:skip++
        Write-Host "  SKIP  $Name  (前段が失敗したため)" -ForegroundColor DarkYellow
        return $false
    }
    try {
        & $Body
        $script:pass++
        Write-Host "  PASS  $Name" -ForegroundColor Green
        return $true
    } catch {
        $script:fail++
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        foreach ($line in ($_.Exception.Message -split "`n" | Select-Object -First 12)) {
            if ($line.Trim()) { Write-Host "        $($line.TrimEnd())" -ForegroundColor DarkRed }
        }
        return $false
    }
}

function Invoke-Checked {
    param([string]$Exe, [string[]]$Arguments)
    $out = & $Exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        # 数百行の MSBuild 出力をそのまま出すと本当の原因が読めない。
        # error/warning 行を優先し、無ければ末尾を出す
        $lines = $out | ForEach-Object { $_.ToString() }
        $errs = $lines | Where-Object { $_ -match ': (error|fatal)' } | Select-Object -Unique -First 10
        if (-not $errs) { $errs = $lines | Select-Object -Last 10 }
        throw ($errs -join "`n")
    }
    return $out
}

Write-Host "== 前提 =="
# Windows でなければここで止める。以降は必ず失敗するので、続けても
# 「Windows で動かしていない」という一点がノイズに埋もれるだけになる。
# ($IsWindows は PowerShell Core のみ。Windows PowerShell 5.1 では未定義なので
#  $env:OS も見る)
if (-not $IsWindows -and $env:OS -ne 'Windows_NT') {
    Write-Host "  FAIL  Windows で実行している" -ForegroundColor Red
    Write-Host "        このスクリプトは Windows 専用です。" -ForegroundColor DarkRed
    Write-Host "        Linux/macOS では tools/verify-all.sh を使ってください。" -ForegroundColor DarkRed
    exit 1
}
$null = Check "Windows で実行している" { }
$null = Check ".NET SDK 8 が使える" {
    $v = (& dotnet --version)
    if (-not $v) { throw "dotnet が見つかりません" }
    Write-Verbose "SDK $v"
}

Write-Host "`n== restore / build (この開発環境で到達できない部分) =="
$restored = Check "dotnet restore AeroDriver.sln" {
    Invoke-Checked dotnet @('restore', "$repo\AeroDriver.sln") | Out-Null
}
# ここが通れば XAML コンパイルとソースジェネレーターの実出力も通っている
$built = Check "dotnet build AeroDriver.sln (XAML/ジェネレーターを含む)" -When $restored {
    Invoke-Checked dotnet @('build', "$repo\AeroDriver.sln", '-c', 'Release', '--no-restore') | Out-Null
}
# 注: -warnaserror は入れない。Core は net8.0 のまま [SupportedOSPlatform("windows")] 付きの
# ヘルパーを呼ぶため CA1416 が多数出るが、これらは実行時に OperatingSystem.IsWindows() で
# ガードされており誤検出。ここで落とすと本物の失敗が埋もれる

Write-Host "`n== テスト =="
$null = Check "dotnet test (xunit)" -When $built {
    Invoke-Checked dotnet @('test', "$repo\AeroDriver.sln", '-c', 'Release', '--no-build') | Out-Null
}

if (-not $SkipSmoke -and $built) {
    Write-Host "`n== CLI スモーク (System.CommandLine の実パース。システムは変更しない) =="
    $cli = "$repo\src\AeroDriver.CLI\AeroDriver.CLI.csproj"

    $null = Check "--help が終了コード0" {
        Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', '--help') | Out-Null
    }
    $null = Check "config が現在の設定を表示する" {
        $out = Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', 'config')
        if (($out | Out-String) -notmatch 'restore-point') { throw "restore-point が出力に無い" }
    }
    $null = Check "config --set が不正な値を拒否する (終了コード2)" {
        & dotnet run --project $cli -c Release --no-build -- config --set 'backup-generations=0' 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 2) { throw "期待した終了コード 2 ではなく $LASTEXITCODE" }
    }
    $null = Check "未知のサブコマンドを拒否する (非0)" {
        & dotnet run --project $cli -c Release --no-build -- no-such-command 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { throw "不正なコマンドが成功してしまった" }
    }
    $null = Check "scan が実 WMI を叩いて完了する" {
        Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', 'scan') | Out-Null
    }
    $null = Check "history が終了コード0 (履歴が空でも)" {
        Invoke-Checked dotnet @('run', '--project', $cli, '-c', 'Release', '--no-build', '--', 'history') | Out-Null
    }
}

Write-Host ""
$summary = "verify-windows: $($script:pass) passed, $($script:fail) failed"
if ($script:skip -gt 0) { $summary += ", $($script:skip) skipped" }
if ($script:fail -eq 0 -and $script:skip -eq 0) {
    Write-Host $summary -ForegroundColor Green
    Write-Host "IMPROVEMENT_BACKLOG.md の P0『Windows実機でのビルドとテスト』はこれで満たされます。"
    exit 0
} else {
    Write-Host $summary -ForegroundColor Red
    exit 1
}
