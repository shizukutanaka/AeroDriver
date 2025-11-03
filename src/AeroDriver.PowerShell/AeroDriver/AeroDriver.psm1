# AeroDriver PowerShell Module
# Professional Windows Driver Management

#Requires -Version 5.1
#Requires -RunAsAdministrator

using namespace System.Management.Automation
using namespace AeroDriver.Core
using namespace AeroDriver.Core.Services

# Load the AeroDriver Core assembly
$script:ModulePath = $PSScriptRoot
$script:CoreAssemblyPath = Join-Path $script:ModulePath "AeroDriver.Core.dll"

if (Test-Path $script:CoreAssemblyPath) {
    Add-Type -Path $script:CoreAssemblyPath
}

# Create logger instance
$script:Logger = [SimpleLogger]::new()

# Create driver service instance
$script:DriverService = [CoreDriverService]::new($script:Logger)

<#
.SYNOPSIS
    Gets all installed drivers on the system.

.DESCRIPTION
    Retrieves comprehensive information about all installed drivers including name, version, status, and type.

.PARAMETER IncludeDetails
    Include detailed information about each driver.

.EXAMPLE
    Get-AeroDriver

.EXAMPLE
    Get-AeroDriver -IncludeDetails
#>
function Get-AeroDriver {
    [CmdletBinding()]
    param(
        [switch]$IncludeDetails
    )

    try {
        $drivers = $script:DriverService.GetAllDrivers()

        if ($IncludeDetails) {
            $drivers | ForEach-Object {
                [PSCustomObject]@{
                    Id = $_.Id
                    Name = $_.Name
                    Version = $_.Version
                    Status = $_.Status
                    Type = $_.Type
                    Path = $_.Path
                    DeviceClass = $_.DeviceClass
                    IsSigned = $_.IsSigned
                    DriverDate = $_.DriverDate
                    Provider = $_.Provider
                    Location = $_.Location
                }
            }
        } else {
            $drivers | ForEach-Object {
                [PSCustomObject]@{
                    Id = $_.Id
                    Name = $_.Name
                    Version = $_.Version
                    Status = $_.Status
                    Type = $_.Type
                }
            }
        }
    }
    catch {
        Write-Error "Failed to retrieve drivers: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Gets the system status and driver health information.

.DESCRIPTION
    Provides an overview of the system's driver health, including counts of active, problematic, and outdated drivers.

.EXAMPLE
    Get-AeroDriverStatus
#>
function Get-AeroDriverStatus {
    [CmdletBinding()]
    param()

    try {
        $drivers = $script:DriverService.GetAllDrivers()
        $systemStats = [SystemStats]::new()
        $systemStats.TotalDrivers = $drivers.Count
        $systemStats.ActiveDrivers = ($drivers | Where-Object { $_.Status -eq "OK" }).Count
        $systemStats.ProblemDrivers = ($drivers | Where-Object { $_.Status -ne "OK" }).Count
        $systemStats.OutdatedDrivers = ($drivers | Where-Object { $_.Status -eq "Outdated" }).Count
        $systemStats.UnsignedDrivers = ($drivers | Where-Object { -not $_.IsSigned }).Count
        $systemStats.LastScanTime = [DateTime]::Now

        [PSCustomObject]@{
            TotalDrivers = $systemStats.TotalDrivers
            ActiveDrivers = $systemStats.ActiveDrivers
            ProblemDrivers = $systemStats.ProblemDrivers
            OutdatedDrivers = $systemStats.OutdatedDrivers
            UnsignedDrivers = $systemStats.UnsignedDrivers
            HealthPercentage = $systemStats.HealthPercentage
            HealthScore = $systemStats.HealthScore
            HealthGrade = $systemStats.HealthGrade
            LastScanTime = $systemStats.LastScanTime
        }
    }
    catch {
        Write-Error "Failed to get system status: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Scans the system for driver issues and updates.

.DESCRIPTION
    Performs a comprehensive scan of all drivers to identify problems, outdated drivers, and security issues.

.PARAMETER Quick
    Perform a quick scan focusing on critical issues only.

.EXAMPLE
    Scan-AeroDriver

.EXAMPLE
    Scan-AeroDriver -Quick
#>
function Scan-AeroDriver {
    [CmdletBinding()]
    param(
        [switch]$Quick
    )

    try {
        Write-Progress -Activity "Scanning Drivers" -Status "Initializing scan..." -PercentComplete 0

        $result = $script:DriverService.ScanSystem()

        Write-Progress -Activity "Scanning Drivers" -Status "Scan completed" -PercentComplete 100

        [PSCustomObject]@{
            Success = $true
            ScannedDrivers = $result.ScannedDrivers
            AvailableUpdates = $result.AvailableUpdates
            ScanDate = $result.ScanDate
            Message = "Driver scan completed successfully"
        }
    }
    catch {
        Write-Progress -Activity "Scanning Drivers" -Status "Scan failed" -PercentComplete 0
        Write-Error "Failed to scan drivers: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Optimizes system performance by managing drivers.

.DESCRIPTION
    Optimizes driver performance, removes unnecessary drivers, and improves system stability.

.EXAMPLE
    Optimize-AeroDriver
#>
function Optimize-AeroDriver {
    [CmdletBinding()]
    param()

    try {
        Write-Progress -Activity "Optimizing System" -Status "Analyzing drivers..." -PercentComplete 0

        $result = $script:DriverService.OptimizeSystem()

        Write-Progress -Activity "Optimizing System" -Status "Optimization completed" -PercentComplete 100

        [PSCustomObject]@{
            Success = $result.Success
            Message = $result.Message
            Data = $result.Data
        }
    }
    catch {
        Write-Progress -Activity "Optimizing System" -Status "Optimization failed" -PercentComplete 0
        Write-Error "Failed to optimize system: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Repairs driver issues automatically.

.DESCRIPTION
    Attempts to automatically fix common driver problems and issues.

.EXAMPLE
    Repair-AeroDriver
#>
function Repair-AeroDriver {
    [CmdletBinding()]
    param()

    try {
        Write-Progress -Activity "Repairing Drivers" -Status "Identifying issues..." -PercentComplete 0

        $result = $script:DriverService.FixIssues()

        Write-Progress -Activity "Repairing Drivers" -Status "Repair completed" -PercentComplete 100

        [PSCustomObject]@{
            Success = $result.Success
            Message = $result.Message
            ProcessedCount = $result.ProcessedCount
            Errors = $result.Errors
        }
    }
    catch {
        Write-Progress -Activity "Repairing Drivers" -Status "Repair failed" -PercentComplete 0
        Write-Error "Failed to repair drivers: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Backs up drivers to a safe location.

.DESCRIPTION
    Creates backups of specified drivers or all drivers for disaster recovery.

.PARAMETER DriverId
    The ID of the specific driver to backup. If not specified, backs up all drivers.

.EXAMPLE
    Backup-AeroDriver -DriverId "HTREE\ROOT\0"

.EXAMPLE
    Backup-AeroDriver
#>
function Backup-AeroDriver {
    [CmdletBinding()]
    param(
        [string]$DriverId
    )

    try {
        Write-Progress -Activity "Backing Up Drivers" -Status "Creating backups..." -PercentComplete 0

        if ($DriverId) {
            $result = $script:DriverService.BackupDriver($DriverId)
        } else {
            $result = $script:DriverService.BackupAllDrivers()
        }

        Write-Progress -Activity "Backing Up Drivers" -Status "Backup completed" -PercentComplete 100

        [PSCustomObject]@{
            Success = $result.Success
            Message = $result.Message
            Data = $result.Data
        }
    }
    catch {
        Write-Progress -Activity "Backing Up Drivers" -Status "Backup failed" -PercentComplete 0
        Write-Error "Failed to backup drivers: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Restores drivers from backup.

.DESCRIPTION
    Restores drivers from previously created backups.

.PARAMETER BackupPath
    The path to the backup file or directory to restore from.

.EXAMPLE
    Restore-AeroDriver -BackupPath "C:\AeroDriver\Backups\drivers.bak"
#>
function Restore-AeroDriver {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupPath
    )

    try {
        Write-Progress -Activity "Restoring Drivers" -Status "Restoring from backup..." -PercentComplete 0

        $result = $script:DriverService.RestoreDrivers($BackupPath)

        Write-Progress -Activity "Restoring Drivers" -Status "Restore completed" -PercentComplete 100

        [PSCustomObject]@{
            Success = $result.Success
            Message = $result.Message
            Data = $result.Data
        }
    }
    catch {
        Write-Progress -Activity "Restoring Drivers" -Status "Restore failed" -PercentComplete 0
        Write-Error "Failed to restore drivers: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Gets system performance information.

.DESCRIPTION
    Retrieves detailed performance metrics including CPU, memory, and disk usage.

.EXAMPLE
    Get-AeroDriverPerformance
#>
function Get-AeroDriverPerformance {
    [CmdletBinding()]
    param()

    try {
        $report = $script:DriverService.GetPerformanceReport()

        [PSCustomObject]@{
            OverallScore = $report.Metrics.OverallScore
            IsHealthy = $report.IsHealthy
            ReportTime = $report.ReportTime
            Recommendations = $report.Recommendations
            CpuUsagePercent = $report.Metrics.CpuUsagePercent
            AvailableMemoryMB = $report.Metrics.AvailableMemoryMB
            TotalMemoryMB = $report.Metrics.TotalMemoryMB
            ProcessMemoryMB = $report.Metrics.ProcessMemoryMB
            DiskReadMBps = $report.Metrics.DiskReadMBps
            DiskWriteMBps = $report.Metrics.DiskWriteMBps
            NetworkSentKBps = $report.Metrics.NetworkSentKBps
            NetworkReceivedKBps = $report.Metrics.NetworkReceivedKBps
            ProcessCount = $report.Metrics.ProcessCount
            ThreadCount = $report.Metrics.ThreadCount
            IsDegraded = $report.Metrics.IsDegraded
            Note = $report.Metrics.Note
        }
    }
    catch {
        Write-Error "Failed to get performance report: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Performs a security scan of drivers.

.DESCRIPTION
    Scans all drivers for security vulnerabilities, unsigned drivers, and other security issues.

.EXAMPLE
    Get-AeroDriverSecurity
#>
function Get-AeroDriverSecurity {
    [CmdletBinding()]
    param()

    try {
        $report = $script:DriverService.GetSecurityReport()

        [PSCustomObject]@{
            OverallScore = $report.OverallScore
            IsSecure = $report.IsSecure
            ReportTime = $report.ReportTime
            SecurityIssues = $report.SecurityIssues
            UnsignedDrivers = $report.UnsignedDrivers
            VulnerableDrivers = $report.VulnerableDrivers
            Recommendations = $report.Recommendations
        }
    }
    catch {
        Write-Error "Failed to get security report: $($_.Exception.Message)"
    }
}

# Export module members
Export-ModuleMember -Function @(
    'Get-AeroDriver',
    'Get-AeroDriverStatus',
    'Scan-AeroDriver',
    'Optimize-AeroDriver',
    'Repair-AeroDriver',
    'Backup-AeroDriver',
    'Restore-AeroDriver',
    'Get-AeroDriverPerformance',
    'Get-AeroDriverSecurity'
)
