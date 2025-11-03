using System;
using System.Runtime.InteropServices;

namespace AeroDriver.Core
{
    /// <summary>
    /// オペレーティングシステムの検出とプラットフォーム固有機能のサポート
    /// </summary>
    public static class PlatformHelper
    {
        /// <summary>
        /// 現在のプラットフォームを検出
        /// </summary>
        public static PlatformType CurrentPlatform
        {
            get
            {
                if (OperatingSystem.IsWindows())
                    return PlatformType.Windows;
                if (OperatingSystem.IsLinux())
                    return PlatformType.Linux;
                if (OperatingSystem.IsMacOS())
                    return PlatformType.macOS;
                if (OperatingSystem.IsFreeBSD())
                    return PlatformType.FreeBSD;

                return PlatformType.Unknown;
            }
        }

        /// <summary>
        /// プラットフォームがサポートされているかどうか
        /// </summary>
        public static bool IsSupportedPlatform => CurrentPlatform != PlatformType.Unknown;

        /// <summary>
        /// プラットフォーム固有の機能が利用可能かどうか
        /// </summary>
        public static bool IsFeatureAvailable(PlatformFeature feature)
        {
            return feature switch
            {
                PlatformFeature.WMI => OperatingSystem.IsWindows(),
                PlatformFeature.SystemD => OperatingSystem.IsLinux(),
                PlatformFeature.LaunchD => OperatingSystem.IsMacOS(),
                PlatformFeature.PowerShell => OperatingSystem.IsWindows(),
                PlatformFeature.Bash => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
                PlatformFeature.PerformanceCounters => OperatingSystem.IsWindows(),
                PlatformFeature.Sysctl => OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD(),
                PlatformFeature.ProcFs => OperatingSystem.IsLinux(),
                PlatformFeature.WindowsRegistry => OperatingSystem.IsWindows(),
                _ => false
            };
        }

        /// <summary>
        /// アーキテクチャを取得
        /// </summary>
        public static ArchitectureType CurrentArchitecture
        {
            get
            {
                return RuntimeInformation.OSArchitecture switch
                {
                    System.Runtime.InteropServices.Architecture.X86 => ArchitectureType.x86,
                    System.Runtime.InteropServices.Architecture.X64 => ArchitectureType.x64,
                    System.Runtime.InteropServices.Architecture.Arm => ArchitectureType.ARM,
                    System.Runtime.InteropServices.Architecture.Arm64 => ArchitectureType.ARM64,
                    _ => ArchitectureType.Unknown
                };
            }
        }

        /// <summary>
        /// プロセスアーキテクチャを取得
        /// </summary>
        public static ArchitectureType ProcessArchitecture
        {
            get
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    System.Runtime.InteropServices.Architecture.X86 => ArchitectureType.x86,
                    System.Runtime.InteropServices.Architecture.X64 => ArchitectureType.x64,
                    System.Runtime.InteropServices.Architecture.Arm => ArchitectureType.ARM,
                    System.Runtime.InteropServices.Architecture.Arm64 => ArchitectureType.ARM64,
                    _ => ArchitectureType.Unknown
                };
            }
        }

        /// <summary>
        /// プラットフォーム情報を取得
        /// </summary>
        public static PlatformInfo GetPlatformInfo()
        {
            return new PlatformInfo
            {
                Platform = CurrentPlatform,
                Architecture = CurrentArchitecture,
                ProcessArchitecture = ProcessArchitecture,
                Is64BitProcess = Environment.Is64BitProcess,
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                OSDescription = RuntimeInformation.OSDescription,
                Version = Environment.OSVersion.Version,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName
            };
        }

        /// <summary>
        /// プラットフォーム固有のパスを取得
        /// </summary>
        public static string GetPlatformSpecificPath(PlatformPathType pathType)
        {
            return pathType switch
            {
                PlatformPathType.AppData => GetAppDataPath(),
                PlatformPathType.LocalAppData => GetLocalAppDataPath(),
                PlatformPathType.Temp => Path.GetTempPath(),
                PlatformPathType.ProgramFiles => GetProgramFilesPath(),
                PlatformPathType.SystemRoot => GetSystemRoot(),
                PlatformPathType.Home => GetHomeDirectory(),
                _ => throw new ArgumentOutOfRangeException(nameof(pathType))
            };
        }

        private static string GetAppDataPath()
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support");
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config");

            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        private static string GetLocalAppDataPath()
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support");
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "share");

            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        private static string GetProgramFilesPath()
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (OperatingSystem.IsMacOS())
                return "/Applications";
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                return "/usr";

            return "/usr";
        }

        private static string GetSystemRoot()
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (OperatingSystem.IsMacOS())
                return "/System";
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                return "/";

            return "/";
        }

        private static string GetHomeDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }
    }

    /// <summary>
    /// プラットフォームタイプ
    /// </summary>
    public enum PlatformType
    {
        Unknown,
        Windows,
        Linux,
        macOS,
        FreeBSD
    }

    /// <summary>
    /// アーキテクチャタイプ
    /// </summary>
    public enum ArchitectureType
    {
        Unknown,
        x86,
        x64,
        ARM,
        ARM64
    }

    /// <summary>
    /// プラットフォーム機能
    /// </summary>
    public enum PlatformFeature
    {
        WMI,
        SystemD,
        LaunchD,
        PowerShell,
        Bash,
        PerformanceCounters,
        Sysctl,
        ProcFs,
        WindowsRegistry
    }

    /// <summary>
    /// プラットフォームパス種別
    /// </summary>
    public enum PlatformPathType
    {
        AppData,
        LocalAppData,
        Temp,
        ProgramFiles,
        SystemRoot,
        Home
    }

    /// <summary>
    /// プラットフォーム情報
    /// </summary>
    public class PlatformInfo
    {
        public PlatformType Platform { get; set; }
        public ArchitectureType Architecture { get; set; }
        public ArchitectureType ProcessArchitecture { get; set; }
        public bool Is64BitProcess { get; set; }
        public bool Is64BitOperatingSystem { get; set; }
        public string FrameworkDescription { get; set; } = string.Empty;
        public string OSDescription { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version();
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }
}
