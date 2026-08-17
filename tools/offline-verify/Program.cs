using AeroDriver.Core.Helpers;
using AeroDriver.Core.Models;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
}

Console.WriteLine("== HardwareIdParser (PCI/USB, windows-driver-docs formats) ==");
void Hw(string input, string? bus, string? core, string? qualified)
{
    bool ok = HardwareIdParser.TryParse(input, out var p);
    if (bus == null) { Check($"reject {input ?? "(null)"}", !ok, $"got {p?.CoreId}"); return; }
    Check($"{input} -> {core}",
        ok && p!.Bus == bus && p.CoreId == core && p.QualifiedId == qualified,
        $"got bus={p?.Bus} core={p?.CoreId} qual={p?.QualifiedId}");
}
Hw(@"PCI\VEN_8086&DEV_1234", "PCI", @"PCI\VEN_8086&DEV_1234", @"PCI\VEN_8086&DEV_1234");
Hw(@"PCI\VEN_8086&DEV_1234&SUBSYS_00000000&REV_03", "PCI", @"PCI\VEN_8086&DEV_1234", @"PCI\VEN_8086&DEV_1234");
Hw(@"USB\VID_046D&PID_C52B", "USB", @"USB\VID_046D&PID_C52B", @"USB\VID_046D&PID_C52B");
Hw(@"USB\VID_046D&PID_C52B&REV_0100", "USB", @"USB\VID_046D&PID_C52B", @"USB\VID_046D&PID_C52B");
Hw(@"USB\VID_046D&PID_C52B&MI_01", "USB", @"USB\VID_046D&PID_C52B", @"USB\VID_046D&PID_C52B&MI_01");
Hw(@"USB\VID_1234&PID_5678&REV_0100&MI_02", "USB", @"USB\VID_1234&PID_5678", @"USB\VID_1234&PID_5678&MI_02");
Hw(@"usb\vid_046d&pid_c52b", "USB", @"USB\VID_046D&PID_C52B", @"USB\VID_046D&PID_C52B");
Hw(@"ACPI\PNP0A03", null, null, null);
Hw(@"PCI\VEN_808&DEV_1234", null, null, null);
Hw(@"USB\VID_046D", null, null, null);
Hw(null!, null, null, null);
Hw("   ", null, null, null);

Console.WriteLine("== InstallerExitCode (msi/pnputil documented codes) ==");
Check("0 -> Success", InstallerExitCode.Interpret(0) == InstallerOutcome.Success);
Check("3010 -> SuccessRebootRequired", InstallerExitCode.Interpret(3010) == InstallerOutcome.SuccessRebootRequired);
Check("1641 -> SuccessRebootRequired", InstallerExitCode.Interpret(1641) == InstallerOutcome.SuccessRebootRequired);
Check("259 -> NoMatchingDevices", InstallerExitCode.Interpret(259) == InstallerOutcome.NoMatchingDevices);
Check("1603 -> Failed", InstallerExitCode.Interpret(1603) == InstallerOutcome.Failed);
Check("3010 IsSuccess", InstallerExitCode.IsSuccess(3010));
Check("1641 IsSuccess", InstallerExitCode.IsSuccess(1641));
Check("259 not success", !InstallerExitCode.IsSuccess(259));
Check("DriverInstallResult.SuccessRebootRequired.IsSuccess()", DriverInstallResult.SuccessRebootRequired.IsSuccess());
Check("DriverInstallResult.InstallerFailed not success", !DriverInstallResult.InstallerFailed.IsSuccess());
Check("Success enum value still 0", (int)DriverInstallResult.Success == 0);

Console.WriteLine("== VersionHelper (numeric, not lexicographic) ==");
Check("10.2.0 > 9.5.1", VersionHelper.IsNewer("10.2.0", "9.5.1"));
Check("1.0 == 1.0.0", VersionHelper.Compare("1.0", "1.0.0") == 0);
Check("null < 1.0", VersionHelper.Compare(null, "1.0") < 0);

Console.WriteLine("== DriverInstallOrder (chipset before GPU) ==");
var sorted = DriverInstallOrder.Sort(new[]{
    new DriverInfo{ DeviceName="GPU", DeviceClass="DISPLAY", IsGraphicsDriver=true },
    new DriverInfo{ DeviceName="Net", DeviceClass="NET" },
    new DriverInfo{ DeviceName="Chipset", DeviceClass="SYSTEM" },
});
Check("SYSTEM -> NET -> DISPLAY",
    sorted[0].DeviceName=="Chipset" && sorted[1].DeviceName=="Net" && sorted[2].DeviceName=="GPU",
    string.Join(",", sorted.Select(d=>d.DeviceName)));
Check("GPU by flag when DeviceClass null",
    DriverInstallOrder.GetPriority(new DriverInfo{ IsGraphicsDriver=true })
    == DriverInstallOrder.GetPriority(new DriverInfo{ DeviceClass="DISPLAY" }));


Console.WriteLine("== InstallHistoryService (JSONL append-only, torn-line resilience) ==");
{
    var hist = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vh_{Guid.NewGuid():N}.jsonl");
    var svc = new AeroDriver.Core.Services.InstallHistoryService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.InstallHistoryService>.Instance, hist);

    await svc.RecordAsync(new InstallHistoryEntry {
        TimestampUtc = new DateTime(2026,3,1,12,0,0,DateTimeKind.Utc),
        DeviceName="GPU", FromVersion="1.0", ToVersion="2.0",
        Result="Success", Success=true, RestorePointSequence=42, BackupCreated=true });
    await svc.RecordAsync(new InstallHistoryEntry { TimestampUtc=DateTime.UtcNow, DeviceName="NIC", ToVersion="3.0", Success=false, Result="InstallerFailed" });

    var all = await svc.GetHistoryAsync();
    Check("2 entries recorded", all.Count==2, $"got {all.Count}");
    Check("newest first", all[0].DeviceName=="NIC", $"got {all[0].DeviceName}");
    Check("restore point sequence round-trips", all[1].RestorePointSequence==42);
    Check("failures are recorded too", all[0].Success==false);
    Check("UTC timestamp preserved", all[1].TimestampUtc==new DateTime(2026,3,1,12,0,0,DateTimeKind.Utc));

    // torn final line (the reason JSONL was chosen)
    await File.AppendAllTextAsync(hist, "{\"DeviceName\":\"Torn\",\"ToVer");
    var afterTorn = await svc.GetHistoryAsync();
    Check("torn final line skipped, earlier entries survive", afterTorn.Count==2, $"got {afterTorn.Count}");

    // corrupt middle line
    await File.AppendAllTextAsync(hist, "\nnot json at all\n");
    await svc.RecordAsync(new InstallHistoryEntry { TimestampUtc=DateTime.UtcNow, DeviceName="After", Success=true });
    var afterCorrupt = await svc.GetHistoryAsync();
    Check("corrupt middle line skipped", afterCorrupt.Count==3 && afterCorrupt[0].DeviceName=="After", $"got {afterCorrupt.Count}");

    Check("limit respected", (await svc.GetHistoryAsync(limit:2)).Count==2);
    File.Delete(hist);
}

Console.WriteLine("== SettingsService (persistence + new CreateRestorePoint key) ==");
{
    var cfg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vs_{Guid.NewGuid():N}.json");
    var log = Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance;
    var s1 = new AeroDriver.Core.Services.SettingsService(log, cfg);
    Check("CreateRestorePoint defaults true", s1.CreateRestorePoint);
    Check("BackupEnabled defaults true", s1.BackupEnabled);
    Check("IncludeBetaDrivers defaults false", !s1.IncludeBetaDrivers);
    s1.CreateRestorePoint = false;
    s1.MaxBackupGenerations = 7;
    var s2 = new AeroDriver.Core.Services.SettingsService(log, cfg);
    Check("CreateRestorePoint persisted", !s2.CreateRestorePoint);
    Check("MaxBackupGenerations persisted", s2.MaxBackupGenerations==7);
    Check("MaxBackupGenerations clamps to >=1", new AeroDriver.Core.Services.SettingsService(log, cfg){ MaxBackupGenerations = 0 }.MaxBackupGenerations>=1);
    File.Delete(cfg);
}

Console.WriteLine("== AuthenticodeHelper (failure-reason diagnosis) ==");
{
    Check("0 -> valid", AuthenticodeHelper.DescribeVerificationFailure(0).Contains("有効"));
    var unreachable = AuthenticodeHelper.DescribeVerificationFailure(unchecked((int)0x800B010E));
    var revoked     = AuthenticodeHelper.DescribeVerificationFailure(unchecked((int)0x800B010C));
    Check("revocation-unreachable != actually-revoked", unreachable != revoked);
    Check("unreachable mentions could-not-check", unreachable.Contains("確認できませんでした"));
    Check("revoked mentions revoked", revoked.Contains("失効しています"));
    Check("bad digest is specific", !AuthenticodeHelper.DescribeVerificationFailure(unchecked((int)0x80096010)).Contains("0x"));
    Check("unknown code falls back with hex", AuthenticodeHelper.DescribeVerificationFailure(unchecked((int)0xDEADBEEF)).Contains("DEADBEEF"));
    Check("non-Windows VerifyTrustStatus fails closed", AuthenticodeHelper.VerifyTrustStatus("/no/such/file.exe") != 0);
    Check("non-Windows HasValidSignature false", !AuthenticodeHelper.HasValidSignature("/no/such/file.exe"));
}

Console.WriteLine("== SystemRestoreHelper (non-Windows no-op) ==");
{
    var log = Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance;
    Check("BeginRestorePoint returns null off-Windows", SystemRestoreHelper.BeginRestorePoint(log, "test") is null);
    SystemRestoreHelper.EndRestorePoint(log, 0);
    Check("EndRestorePoint(0) does not throw", true);
}


Console.WriteLine("== PnpUtilDriverSource.ParseEnumOutput (pnputil /enum-drivers output) ==");
{
    // ParseEnumOutputPublic は protected virtual なので、テスト用サブクラス経由で叩く
    var src = new ProbePnpUtil(Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.PnpUtilDriverSource>.Instance);
    var sample = string.Join("\n", new[]{
        "Published Name:     oem12.inf",
        "Original Name:      nv_dispi.inf",
        "Provider Name:      NVIDIA",
        "Class Name:         Display adapters",
        "Class GUID:         {4D36E968-E325-11CE-BFC1-08002BE10318}",
        "Driver Version:     01/15/2026 31.0.15.3667",
        "Signer Name:        Microsoft Windows Hardware Compatibility Publisher",
        "",
        "Published Name:     oem34.inf",
        "Original Name:      e1d68x64.inf",
        "Provider Name:      Intel",
        "Class Name:         Network adapters",
        "Class GUID:         {4D36E972-E325-11CE-BFC1-08002BE10318}",
        "Driver Version:     02/02/2026 12.19.2.60",
        "Signer Name:        Microsoft Windows Hardware Compatibility Publisher",
        "",
    });
    var parsed = src.Probe(sample);
    Check("parses 2 driver blocks", parsed.Count == 2, $"got {parsed.Count}");
    if (parsed.Count == 2)
    {
        Check("provider parsed", parsed[0].DriverProviderName == "NVIDIA", $"got {parsed[0].DriverProviderName}");
        Check("version parsed (not the date)", parsed[0].DriverVersion == "31.0.15.3667", $"got {parsed[0].DriverVersion}");
        Check("second block provider", parsed[1].DriverProviderName == "Intel", $"got {parsed[1].DriverProviderName}");
    }
    Check("empty output -> empty list", src.Probe("").Count == 0);
    Check("garbage output does not throw", src.Probe("no colons here at all\njust text").Count == 0);
}


Console.WriteLine("== WqlSanitizer (WQL injection allowlist) ==");
{
    // 正常系: 実在形式の DeviceID は通り、バックスラッシュが WQL リテラル用に二重化される
    Check("PCI id accepted and backslash doubled",
        WqlSanitizer.SanitizeDeviceId(@"PCI\VEN_10DE&DEV_2204") == @"PCI\\VEN_10DE&DEV_2204",
        WqlSanitizer.SanitizeDeviceId(@"PCI\VEN_10DE&DEV_2204"));
    Check("USB id accepted", WqlSanitizer.SanitizeDeviceId(@"USB\VID_0BDA&PID_8153").Contains("VID_0BDA"));
    Check("GUID braces accepted", WqlSanitizer.SanitizeDeviceId("{4D36E968-E325-11CE-BFC1-08002BE10318}").Length > 0);

    // 攻撃系: いずれも ArgumentException で拒否されること
    void Reject(string label, string payload)
    {
        try { var r = WqlSanitizer.SanitizeDeviceId(payload); Check($"reject {label}", false, $"accepted -> {r}"); }
        catch (ArgumentException) { Check($"reject {label}", true); }
    }
    Reject("single quote", "abc'");
    Reject("OR-injection", "' OR '1'='1");
    Reject("quote+OR with real prefix", @"PCI\VEN_10DE' OR '1'='1");
    Reject("space", "PCI VEN");
    Reject("semicolon", "abc;DROP");
    Reject("percent wildcard", "abc%");
    Reject("newline", "abc\ndef");
    Reject("null char", "abc\0def");
    Reject("empty", "");
    Reject("double quote", "abc\"def");
    Reject("equals sign", "abc=def");

    // エスケープ順序: バックスラッシュを先に二重化しないと、クォートエスケープで
    // 追加したバックスラッシュがさらに二重化されて壊れる
    Check("escape order does not corrupt", WqlSanitizer.SanitizeDeviceId(@"A\B") == @"A\\B");
}

Console.WriteLine("== BackupService path traversal (device id -> directory) ==");
{
    var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vb_{Guid.NewGuid():N}");
    var settings = new AeroDriver.Core.Services.SettingsService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance,
        System.IO.Path.Combine(root, "settings.json"));
    var bk = new ProbeBackup(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.BackupService>.Instance,
        settings, root);

    // 正常系: 実在形式の DeviceID はバックアップ無しでも空配列を返すだけ
    Check("normal device id -> no throw, empty list",
        bk.GetAvailableBackups(new DriverInfo { DeviceID = @"PCI\VEN_10DE&DEV_2204" }).Length == 0);

    // 検証すべき不変条件は「例外を投げること」ではなく
    // **どんな入力でもルート外に出ないこと**。
    // 区切り文字は GetInvalidFileNameChars で除去されるため、"../escaped" は
    // "..escaped" という無害な単一名に潰れてルート内に収まる(例外は出ない、それで正しい)。
    // 一方 ".." は除去対象の文字を含まないため素通りし、正規化後のルート配下チェックで弾かれる。
    void NeverEscapes(string label, string deviceId)
    {
        try
        {
            bk.GetAvailableBackups(new DriverInfo { DeviceID = deviceId });
            // 例外なし = 無害化されたはず。ルート外にディレクトリが出来ていないことを確認
            var rootFull = System.IO.Path.GetFullPath(root);
            var parent = System.IO.Directory.GetParent(rootFull)!.FullName;
            var strayInParent = System.IO.Directory.EnumerateDirectories(parent)
                .Any(d => !System.IO.Path.GetFullPath(d).Equals(rootFull, StringComparison.Ordinal)
                          && System.IO.Path.GetFileName(d).Contains("escaped", StringComparison.Ordinal));
            Check($"{label}: neutralised, stays inside root", !strayInParent, "created a directory outside root");
        }
        catch (ArgumentException) { Check($"{label}: rejected", true); }
    }
    NeverEscapes("dot-dot", "..");
    NeverEscapes("dot-dot with separator", ".." + System.IO.Path.DirectorySeparatorChar + "escaped");
    NeverEscapes("nested traversal", ".." + System.IO.Path.DirectorySeparatorChar + ".." + System.IO.Path.DirectorySeparatorChar + "escaped");
    NeverEscapes("windows-style separator", "..\\escaped");
    NeverEscapes("absolute path", System.IO.Path.DirectorySeparatorChar + "etc" + System.IO.Path.DirectorySeparatorChar + "escaped");

    void RejectPath(string label, string deviceId)
    {
        try
        {
            bk.GetAvailableBackups(new DriverInfo { DeviceID = deviceId });
            Check($"reject {label}", false, "no exception thrown");
        }
        catch (ArgumentException) { Check($"reject {label}", true); }
    }
    RejectPath("empty", "");
    RejectPath("whitespace only", "   ");

    // ルート外に何も作られていないこと（総括）
    var parentDir = System.IO.Directory.GetParent(System.IO.Path.GetFullPath(root))!.FullName;
    Check("no 'escaped' dir created outside root",
        !System.IO.Directory.Exists(System.IO.Path.Combine(parentDir, "escaped")));

    try { System.IO.Directory.Delete(root, true); } catch { }
}
Console.WriteLine($"\n==== {pass} passed, {fail} failed ====");
return fail == 0 ? 0 : 1;


sealed class ProbePnpUtil : AeroDriver.Core.Services.PnpUtilDriverSource
{
    public ProbePnpUtil(Microsoft.Extensions.Logging.ILogger<AeroDriver.Core.Services.PnpUtilDriverSource> l) : base(l) { }
    public IReadOnlyList<DriverInfo> Probe(string output) => ParseEnumOutputPublic(output);
}


sealed class ProbeBackup : AeroDriver.Core.Services.BackupService
{
    public ProbeBackup(
        Microsoft.Extensions.Logging.ILogger<AeroDriver.Core.Services.BackupService> l,
        AeroDriver.Core.Interfaces.ISettingsService s, string root) : base(l, s, root) { }
}
