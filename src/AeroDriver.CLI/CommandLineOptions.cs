using AeroDriver.Core.Interfaces;
using AeroDriver.CLI.Services;

namespace AeroDriver.CLI
{
    /// <summary>
    /// コマンドラインオプション
    /// </summary>
    public class CommandLineOptions
    {
        public string Command { get; set; } = "";
        public string[] Arguments { get; set; } = Array.Empty<string>();
        public bool Verbose { get; set; }
        public bool Silent { get; set; }
        public bool Force { get; set; }
        public bool NoBackup { get; set; }
        public bool DryRun { get; set; }
        public bool ShowProgress { get; set; } = true;
        public string? OutputFormat { get; set; }
        public string? OutputPath { get; set; }
        public string? ConfigPath { get; set; }
        public int? Timeout { get; set; }
        public bool Help { get; set; }
        public bool Version { get; set; }
        public bool NoColor { get; set; }
        
        /// <summary>
        /// コマンドライン引数をパース
        /// </summary>
        public static CommandLineOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            
            var options = new CommandLineOptions();
            var arguments = new List<string>();
            
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                
                if (arg.StartsWith("--"))
                {
                    // Long options
                    switch (arg.ToLowerInvariant())
                    {
                        case "--verbose":
                            options.Verbose = true;
                            break;
                        case "--silent":
                            options.Silent = true;
                            break;
                        case "--force":
                            options.Force = true;
                            break;
                        case "--no-backup":
                            options.NoBackup = true;
                            break;
                        case "--dry-run":
                            options.DryRun = true;
                            break;
                        case "--no-progress":
                            options.ShowProgress = false;
                            break;
                        case "--no-color":
                            options.NoColor = true;
                            break;
                        case "--config":
                            if (i + 1 < args.Length)
                            {
                                options.ConfigPath = args[++i];
                            }
                            break;
                        case "--help":
                            options.Help = true;
                            break;
                        case "--version":
                            options.Version = true;
                            break;
                        case "--format":
                            if (i + 1 < args.Length)
                            {
                                options.OutputFormat = args[++i];
                            }
                            break;
                        case "--output":
                            if (i + 1 < args.Length)
                            {
                                options.OutputPath = args[++i];
                            }
                            break;
                        case "--timeout":
                            if (i + 1 < args.Length && int.TryParse(args[++i], out var timeout))
                            {
                                options.Timeout = timeout;
                            }
                            break;
                        default:
                            if (!options.Help)
                            {
                                var console = new EnterpriseConsoleOutput();
                                console.WriteWarning($"Unknown option: {arg}");
                            }
                            break;
                    }
                }
                else if (arg.StartsWith("-"))
                {
                    // Short options
                    foreach (var c in arg.Skip(1))
                    {
                        switch (c)
                        {
                            case 'v':
                                options.Verbose = true;
                                break;
                            case 's':
                                options.Silent = true;
                                break;
                            case 'f':
                                options.Force = true;
                                break;
                            case 'h':
                                options.Help = true;
                                break;
                            case 'V':
                                options.Version = true;
                                break;
                            default:
                                if (!options.Help)
                                {
                                    var console = new EnterpriseConsoleOutput();
                                    console.WriteWarning($"Unknown option: -{c}");
                                }
                                break;
                        }
                    }
                }
                else
                {
                    // Command or argument
                    if (string.IsNullOrEmpty(options.Command))
                    {
                        options.Command = arg.ToLowerInvariant();
                    }
                    else
                    {
                        arguments.Add(arg);
                    }
                }
            }
            
            options.Arguments = arguments.ToArray();
            return options;
        }
        
        /// <summary>
        /// ヘルプメッセージを表示
        /// </summary>
        public static void ShowHelp(IConsoleOutput? output = null)
        {
            var console = output ?? new EnterpriseConsoleOutput();
            
            console.WriteHeader("AeroDriver CLI - Enterprise Driver Management Tool");
            
            console.WriteInfo("Usage: AeroDriver.CLI [options] <command> [arguments]");
            console.WriteInfo("");
            
            console.WriteInfo("Options:");
            console.WriteList(new[]
            {
                "-v, --verbose          Enable verbose logging",
                "-s, --silent           Silent mode (minimal output)",
                "-f, --force            Force operation without confirmation",
                "--no-backup            Skip backup creation",
                "--dry-run              Show what would be done without executing",
                "--no-progress          Disable progress indicators",
                "--no-color             Disable colored output",
                "--format <format>      Output format (text|json|csv|html)",
                "--config <path>        Use custom configuration file",
                "--output <path>        Output file path",
                "--timeout <seconds>    Operation timeout in seconds",
                "-h, --help             Show help message",
                "-V, --version          Show version information"
            }, "  ");
            
            console.WriteInfo("Commands:");
            console.WriteList(new[]
            {
                "auto                   Auto mode - comprehensive driver maintenance",
                "list                   List all installed drivers with details",
                "scan                   Scan for available driver updates",
                "update <deviceId>      Update a specific driver (enterprise-safe)",
                "backup <deviceId>      Create validated backup of a driver",
                "rollback <deviceId>    Rollback a driver to previous version",
                "fix                    Analyze and suggest fixes for driver issues",
                "diag                   Run comprehensive system diagnostics",
                "info                   Show detailed system information",
                "health                 Generate comprehensive health report",
                "cleanup [type]         Clean up files (all|backups|temp|cache)",
                "cache <action>         Manage cache (clear|cleanup|info)",
                "report [type]          Generate detailed report (quick|full|drivers|system)",
                "logs [filter]          View logs (recent|today|errors|all)",
                "settings               Show current configuration settings"
            }, "  ");
            
            console.WriteInfo("Examples:");
            console.WriteList(new[]
            {
                "AeroDriver.CLI --verbose scan",
                "AeroDriver.CLI update \"PCI\\VEN_8086&DEV_1234\" --no-backup",
                "AeroDriver.CLI report full --format json --output report.json",
                "AeroDriver.CLI health --verbose",
                "AeroDriver.CLI auto --dry-run --verbose"
            }, "  ");
        }
        
        /// <summary>
        /// バージョン情報を表示
        /// </summary>
        public static void ShowVersion(IConsoleOutput? output = null)
        {
            var console = output ?? new EnterpriseConsoleOutput();
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version ?? new Version(1, 0, 0);
            
            console.WriteHeader("AeroDriver CLI - Enterprise Edition");
            console.WriteInfo($"Version: {version.Major}.{version.Minor}.{version.Build}");
            console.WriteInfo($"Copyright (c) 2025 AeroDriver Team");
            console.WriteInfo($".NET Runtime: {Environment.Version}");
            console.WriteInfo($"OS: {Environment.OSVersion}");
            console.WriteInfo($"Architecture: {Environment.OSVersion.Platform} {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
            console.WriteInfo($"Process Privileges: {(AeroDriver.Core.Helpers.SecurityHelper.IsRunningAsAdministrator() ? "Administrator" : "Standard User")}");
            console.WriteInfo("");
            console.WriteInfo("Enterprise Features:");
            console.WriteList(new[]
            {
                "WHQL-certified driver validation",
                "Enterprise-grade security controls",
                "Comprehensive audit logging",
                "Performance monitoring and telemetry",
                "Automated backup and recovery",
                "System health monitoring",
                "Multi-language support (10 languages)"
            });
        }
    }
}