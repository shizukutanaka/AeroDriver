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

Console.WriteLine($"\n==== {pass} passed, {fail} failed ====");
return fail == 0 ? 0 : 1;
