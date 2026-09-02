using System.Linq;
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

    // テーマ/言語の永続化（GUIの選択が再起動後も残ること）
    Check("ThemeName defaults to null (未設定)", s2.ThemeName is null);
    Check("CultureName defaults to null (未設定)", s2.CultureName is null);
    s2.ThemeName = "Dark";
    s2.CultureName = "ja-JP";
    var s3 = new AeroDriver.Core.Services.SettingsService(log, cfg);
    Check("ThemeName persisted across instances", s3.ThemeName == "Dark", s3.ThemeName ?? "(null)");
    Check("CultureName persisted across instances", s3.CultureName == "ja-JP", s3.CultureName ?? "(null)");
    // 既存キーが巻き添えで壊れていないこと
    Check("existing keys survive the new fields", s3.MaxBackupGenerations == 7 && !s3.CreateRestorePoint);
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

Console.WriteLine("== BackupService generation retention (keeps NEWEST, not oldest) ==");
{
    var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vr_{Guid.NewGuid():N}");
    var settings = new AeroDriver.Core.Services.SettingsService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance,
        System.IO.Path.Combine(root, "settings.json"));
    var bk = new ProbeBackup(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.BackupService>.Instance,
        settings, root);

    var driver = new DriverInfo { DeviceID = @"PCI\\VEN_TEST&DEV_0001" };
    // GetAvailableBackups 経由でデバイスディレクトリを作らせ、その実パスを得る
    bk.GetAvailableBackups(driver);
    var deviceDir = System.IO.Directory.GetDirectories(root).Single(d => !d.EndsWith(".json"));

    // 時系列が明確な5世代を作る（naming は backup_yyyyMMddHHmmss）
    string[] stamps = { "20260101100000", "20260102100000", "20260103100000", "20260104100000", "20260105100000" };
    foreach (var st in stamps) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(deviceDir, $"backup_{st}"));

    Check("5 generations created", System.IO.Directory.GetDirectories(deviceDir, "backup_*").Length == 5);

    await bk.CleanupOldBackupsAsync(3);

    var remaining = System.IO.Directory.GetDirectories(deviceDir, "backup_*")
        .Select(System.IO.Path.GetFileName).OrderBy(x => x).ToArray();
    Check("3 generations retained", remaining.Length == 3, $"got {remaining.Length}");
    Check("kept the NEWEST three (not the oldest)",
        remaining.SequenceEqual(new[]{ "backup_20260103100000", "backup_20260104100000", "backup_20260105100000" }),
        string.Join(",", remaining));

    // 一覧は新しい順で返る
    var listed = bk.GetAvailableBackups(driver);
    Check("GetAvailableBackups returns newest first",
        listed.Length == 3 && listed[0] == "20260105100000", string.Join(",", listed));

    // maxGenerations が世代数以上なら何も消さない
    await bk.CleanupOldBackupsAsync(10);
    Check("no deletion when limit exceeds count",
        System.IO.Directory.GetDirectories(deviceDir, "backup_*").Length == 3);

    // 0以下は例外
    try { await bk.CleanupOldBackupsAsync(0); Check("reject maxGenerations=0", false, "no throw"); }
    catch (ArgumentOutOfRangeException) { Check("reject maxGenerations=0", true); }

    try { System.IO.Directory.Delete(root, true); } catch { }
}

Console.WriteLine("== InstallHistoryService の切り詰め(上限5MiB。安全網の初の実行検証) ==");
{
    // 実装はあったが一度も実行されていなかった。年単位で使うと必ず通る経路で、
    // 壊れていれば監査証跡を全損する。実際に上限を超えさせて確かめる
    var hist = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"trim_{Guid.NewGuid():N}.jsonl");
    var svc = new AeroDriver.Core.Services.InstallHistoryService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.InstallHistoryService>.Instance, hist);
    try
    {
        // 5 MiB を超えるまで直接書き込む(RecordAsync を何万回も回すより速い)。
        // 各行は GetHistoryAsync が読める正当な JSONL でなければならない
        // 各行に通し番号を入れる。これが無いと「新しい半分」と「古い半分」を
        // 区別できず、Skip を Take に変えても検出できない(実際に一度そうなった)
        var sb = new System.Text.StringBuilder();
        int written = 0;
        while (sb.Length < 5 * 1024 * 1024 + 4096)
        {
            sb.Append(System.Text.Json.JsonSerializer.Serialize(new InstallHistoryEntry
            {
                TimestampUtc = DateTime.UtcNow,
                DeviceName = $"seq-{written:D6}-" + new string('x', 180),
                ToVersion = "1.0", Success = true, Result = "Success",
            })).Append('\n');
            written++;
        }
        await System.IO.File.WriteAllTextAsync(hist, sb.ToString());

        var before = new System.IO.FileInfo(hist).Length;
        Check("上限を超えた状態を作れた", before > 5 * 1024 * 1024, before.ToString());

        // 追記すると切り詰めが走る
        await svc.RecordAsync(new InstallHistoryEntry
        {
            TimestampUtc = DateTime.UtcNow, DeviceName = "Newest", ToVersion = "9.9",
            Success = true, Result = "Success",
        });

        var after = new System.IO.FileInfo(hist).Length;
        Check("ファイルが縮んだ", after < before, $"{before} -> {after}");
        Check("空にはなっていない(全損させない)", after > before / 4, after.ToString());

        var entries = await svc.GetHistoryAsync();
        Check("切り詰め後も読み出せる", entries.Count > 0, entries.Count.ToString());
        Check("残ったのは新しい方(直前の追記が先頭)", entries[0].DeviceName == "Newest",
            entries[0].DeviceName ?? "(null)");
        Check("件数がおよそ半分になった", entries.Count < written, $"{written} -> {entries.Count}");

        // 残ったのが「新しい半分」であることを通し番号で確かめる。
        // 古い方を残す実装(Skip→Take)ならここで落ちる
        var seqs = entries.Select(e => e.DeviceName ?? string.Empty)
                          .Where(n => n.StartsWith("seq-"))
                          .Select(n => int.Parse(n.Substring(4, 6)))
                          .ToList();
        Check("通し番号を持つ行が残っている", seqs.Count > 0, seqs.Count.ToString());
        Check("残ったのは番号の大きい方(= 新しい半分)",
            seqs.Count > 0 && seqs.Min() >= written / 2 - 1,
            $"min={(seqs.Count > 0 ? seqs.Min() : -1)} written/2={written / 2}");
        Check("最も古い行(seq-000000)は捨てられている", !seqs.Contains(0));
        Check("一時ファイルを残していない", !System.IO.File.Exists(hist + ".tmp"));
    }
    finally
    {
        try { System.IO.File.Delete(hist); } catch { }
        try { System.IO.File.Delete(hist + ".tmp"); } catch { }
    }
}

Console.WriteLine("== SettingsService の保存がアトミックであること ==");
{
    // File.WriteAllText は切り詰めてから書くため、途中で落ちると設定が全損する。
    // 履歴の切り詰めと同じ temp+Move に統一したことを実際に確かめる
    var cfg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"atomic_{Guid.NewGuid():N}.json");
    var log = Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance;
    try
    {
        var svc = new AeroDriver.Core.Services.SettingsService(log, cfg);
        svc.MaxBackupGenerations = 7;
        svc.ThemeName = "Dark";
        svc.Save();

        Check("保存後に設定ファイルが存在する", System.IO.File.Exists(cfg));
        Check("一時ファイルを残していない", !System.IO.File.Exists(cfg + ".tmp"));

        var body = await System.IO.File.ReadAllTextAsync(cfg);
        Check("中身が完全な JSON(切り詰められていない)",
            body.TrimStart().StartsWith("{") && body.TrimEnd().EndsWith("}"), body);

        var reloaded = new AeroDriver.Core.Services.SettingsService(log, cfg);
        Check("保存内容が読み戻せる", reloaded.MaxBackupGenerations == 7 && reloaded.ThemeName == "Dark",
            $"{reloaded.MaxBackupGenerations}/{reloaded.ThemeName}");

        // 既存ファイルがある状態で上書きしても壊れないこと(Move の overwrite 経路)
        reloaded.MaxBackupGenerations = 3;
        reloaded.Save();
        var again = new AeroDriver.Core.Services.SettingsService(log, cfg);
        Check("上書き保存も読み戻せる", again.MaxBackupGenerations == 3, again.MaxBackupGenerations.ToString());
        Check("上書き後も一時ファイルを残さない", !System.IO.File.Exists(cfg + ".tmp"));

        // 一時ファイル名はプロセス毎に一意(固定名だと GUI と CLI が同時保存で衝突し、
        // 書き途中の内容を Move してしまう)。名前が一意な分、後始末も必須
        var dir = System.IO.Path.GetDirectoryName(cfg)!;
        var stem = System.IO.Path.GetFileName(cfg);
        var strays = System.IO.Directory.GetFiles(dir, stem + ".*.tmp");
        Check("一意名の一時ファイルも残っていない", strays.Length == 0,
            string.Join(",", strays));

        // 連続保存でも溜まらない(名前が一意なので後始末が無いと溜まる)
        for (int i = 0; i < 5; i++) { again.MaxBackupGenerations = i + 1; again.Save(); }
        Check("連続保存後も一時ファイルが溜まらない",
            System.IO.Directory.GetFiles(dir, stem + ".*.tmp").Length == 0);
        Check("連続保存の最後の値が読み戻せる",
            new AeroDriver.Core.Services.SettingsService(log, cfg).MaxBackupGenerations == 5);
    }
    finally
    {
        try { System.IO.File.Delete(cfg); } catch { }
        try { System.IO.File.Delete(cfg + ".tmp"); } catch { }
    }
}

Console.WriteLine("== PlatformGuard(Windows専用ツールを非対応OSで走らせない) ==");
{
    // この環境は Linux。ガードが正しく「非対応」と判定することを実際に確かめる。
    // Windows でこれを走らせると逆の分岐になるが、どちらでも整合するよう書く
    bool win = OperatingSystem.IsWindows();
    Check($"IsSupportedPlatform が OS と一致 (IsWindows={win})",
        PlatformGuard.IsSupportedPlatform() == win);
    var desc = PlatformGuard.DescribeUnsupportedPlatform();
    if (win)
    {
        Check("Windows では理由が null", desc == null, desc ?? "(null)");
    }
    else
    {
        Check("非Windows では理由が返る", !string.IsNullOrWhiteSpace(desc), desc ?? "(null)");
        Check("理由に OS の識別情報が入る", desc!.Contains('/'), desc);
    }
}

Console.WriteLine("== SettingsKeys (設定を UI から到達可能にする表) ==");
{
    Check("全キーが一意", SettingsKeys.All.Select(e => e.Name).Distinct().Count() == SettingsKeys.All.Count);
    Check("キーは小文字ケバブ", SettingsKeys.All.All(e => e.Name == e.Name.ToLowerInvariant() && !e.Name.Contains(' ')));
    Check("説明と値書式が空でない", SettingsKeys.All.All(e => e.Description.Length > 0 && e.ValueSyntax.Length > 0));

    foreach (var t in new[] { "true", "on", "yes", "y", "1", "TRUE", " On " })
        Check($"真として解釈: '{t}'", SettingsKeys.TryParseBool(t, out var b) && b);
    foreach (var f in new[] { "false", "off", "no", "n", "0", "FALSE" })
        Check($"偽として解釈: '{f}'", SettingsKeys.TryParseBool(f, out var b) && !b);
    foreach (var bad in new[] { "", "  ", "maybe", "2", null })
        Check($"拒否: '{bad ?? "(null)"}'", !SettingsKeys.TryParseBool(bad, out _));

    Check("key=value を分解", SettingsKeys.TryParseAssignment("backup=on", out var k1, out var v1) && k1 == "backup" && v1 == "on");
    Check("前後の空白を落とす", SettingsKeys.TryParseAssignment(" backup = on ", out var k2, out var v2) && k2 == "backup" && v2 == "on");
    Check("値に = を含められる", SettingsKeys.TryParseAssignment("backup=a=b", out _, out var v3) && v3 == "a=b");
    Check("= 無しは拒否", !SettingsKeys.TryParseAssignment("backup", out _, out _));
    Check("キー空は拒否", !SettingsKeys.TryParseAssignment("=on", out _, out _));

    var stCfg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sk_{Guid.NewGuid():N}.json");
    var st = new AeroDriver.Core.Services.SettingsService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance, stCfg);
    st.CreateRestorePoint = false;
    Check("restore-point を書ける", SettingsKeys.TryApply(st, "restore-point=on", out _) && st.CreateRestorePoint);
    Check("読み出しが書き込みと一致", SettingsKeys.Find("restore-point")!.Read(st) == "true");
    Check("大文字キーでも引ける", SettingsKeys.Find("Restore-Point") != null);
    Check("未知キーは拒否", !SettingsKeys.TryApply(st, "nope=on", out var e1) && e1.Contains("nope"));

    st.MaxBackupGenerations = 5;
    Check("世代数を書ける", SettingsKeys.TryApply(st, "backup-generations=3", out _) && st.MaxBackupGenerations == 3);
    Check("0世代は拒否", !SettingsKeys.TryApply(st, "backup-generations=0", out _));
    Check("負数は拒否", !SettingsKeys.TryApply(st, "backup-generations=-1", out _));
    Check("非数値は拒否", !SettingsKeys.TryApply(st, "backup-generations=many", out _));
    Check("拒否されたら値は変わらない", st.MaxBackupGenerations == 3, st.MaxBackupGenerations.ToString());
    Check("不正な真偽値は拒否", !SettingsKeys.TryApply(st, "backup=maybe", out var e2) && e2.Contains("backup"));

    // TryValidate は「適用せずに判定する」。複数件をまとめて適用する経路が
    // 「1件でも不正なら何も変更しない」を守るために必須(実際に CLI が partial apply していた)
    st.BackupEnabled = true;
    Check("TryValidate は受理可能な代入に true", SettingsKeys.TryValidate("backup=off", out _));
    Check("TryValidate は値を書き換えない", st.BackupEnabled, "backup が変更されてしまった");
    Check("TryValidate は未知キーを拒否", !SettingsKeys.TryValidate("nope=1", out var e3) && e3.Contains("nope"));
    Check("TryValidate は不正な値を拒否", !SettingsKeys.TryValidate("backup-generations=0", out _));
    Check("TryValidate は書式不正を拒否", !SettingsKeys.TryValidate("backup", out _));
    Check("TryValidate の判定は TryApply と一致",
        SettingsKeys.All.All(entry =>
            new[] { "on", "off", "maybe", "", "3", "0" }.All(v =>
                entry.IsValid(v) == entry.Write(new AeroDriver.Core.Services.SettingsService(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<AeroDriver.Core.Services.SettingsService>.Instance,
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sk2_{Guid.NewGuid():N}.json")), v))));

    // ISettingsService の「ユーザー設定」全件が表に載っているか
    // (ThemeName/CultureName は GUI が直接書くため対象外)
    Check("ユーザー設定5件すべてが到達可能", SettingsKeys.All.Count == 5, SettingsKeys.All.Count.ToString());
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
