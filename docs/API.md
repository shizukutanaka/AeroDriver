# AeroDriver API Documentation

## Core Services API

### IDriverService

Main service interface for driver operations.

#### Methods

##### GetDriversAsync()
```csharp
Task<List<DriverInfo>> GetDriversAsync()
```
Retrieves all installed drivers from the system using WMI.

**Returns:** List of DriverInfo objects containing driver metadata.

**Example:**
```csharp
var drivers = await driverService.GetDriversAsync();
foreach (var driver in drivers)
{
    Console.WriteLine($"{driver.DeviceName}: {driver.DriverVersion}");
}
```

##### ScanForDriversAsync()
```csharp
Task<List<DriverInfo>> ScanForDriversAsync()
```
Scans for available driver updates.

**Returns:** List of available driver updates.

##### UpdateDriverAsync(string deviceId)
```csharp
Task<bool> UpdateDriverAsync(string deviceId)
```
Updates a specific driver by device ID.

**Parameters:**
- `deviceId`: Hardware device ID (e.g., "PCI\\VEN_10DE&DEV_1234")

**Returns:** True if update successful, false otherwise.

##### BackupDriverAsync(string deviceId)
```csharp
Task<bool> BackupDriverAsync(string deviceId)
```
Creates a backup for the specified driver.

##### RollbackDriverAsync(string deviceId)
```csharp
Task<bool> RollbackDriverAsync(string deviceId)
```
Rolls back a driver to the previous version using backup.

### IWhqlDatabaseService

Service for WHQL driver database operations.

#### Methods

##### CheckForUpdatesAsync()
```csharp
Task<List<DriverInfo>> CheckForUpdatesAsync()
```
Checks for available driver updates across all known drivers.

##### FindAvailableUpdateAsync(DriverInfo currentDriver)
```csharp
Task<DriverInfo> FindAvailableUpdateAsync(DriverInfo currentDriver)
```
Finds available update for a specific driver.

**Parameters:**
- `currentDriver`: Current driver information

**Returns:** Updated driver info if available, null otherwise.

### IBackupService

Service for driver backup operations.

#### Methods

##### CreateBackupAsync(string deviceId)
```csharp
Task<bool> CreateBackupAsync(string deviceId)
```
Creates a backup for the specified device.

##### RestoreBackupAsync(string backupPath)
```csharp
Task<bool> RestoreBackupAsync(string backupPath)
```
Restores from a backup.

##### GetBackupsAsync()
```csharp
Task<List<BackupInfo>> GetBackupsAsync()
```
Retrieves all available backups.

### ISettingsService

Service for application settings management.

#### Properties

```csharp
bool AutoUpdateEnabled { get; set; }
bool IncludeBetaDrivers { get; set; }
bool BackupEnabled { get; set; }
int MaxBackupGenerations { get; set; }
```

#### Methods

##### Save()
```csharp
void Save()
```
Persists current settings to storage.

##### ResetToDefaults()
```csharp
void ResetToDefaults()
```
Resets all settings to default values.

## Data Models

### DriverInfo

```csharp
public class DriverInfo
{
    public string DeviceID { get; set; }
    public string DeviceName { get; set; }
    public string DriverVersion { get; set; }
    public string DriverProviderName { get; set; }
    public string DeviceClass { get; set; }
    public string Status { get; set; }
    public bool IsWHQLCertified { get; set; }
}
```

### BackupInfo

```csharp
public class BackupInfo
{
    public string DeviceId { get; set; }
    public DateTime BackupDate { get; set; }
    public string BackupPath { get; set; }
    public string Description { get; set; }
}
```

## CLI Interface

### Available Commands

#### auto
Performs comprehensive driver maintenance automatically.
```bash
aerodriver auto
```

#### scan
Scans for available driver updates.
```bash
aerodriver scan
```

#### list
Lists all installed drivers.
```bash
aerodriver list
```

#### update <deviceId>
Updates a specific driver.
```bash
aerodriver update "PCI\\VEN_10DE&DEV_1234"
```

#### backup <deviceId>
Creates a backup for a specific driver.
```bash
aerodriver backup "PCI\\VEN_10DE&DEV_1234"
```

#### rollback <deviceId>
Rolls back a driver to previous version.
```bash
aerodriver rollback "PCI\\VEN_10DE&DEV_1234"
```

#### info
Shows system information.
```bash
aerodriver info
```

#### diag
Runs system diagnostics.
```bash
aerodriver diag
```

#### settings
Shows current application settings.
```bash
aerodriver settings
```

## Error Handling

### Common Error Scenarios

#### Insufficient Privileges
- **Error:** Access denied when accessing system drivers
- **Solution:** Run application as Administrator

#### Driver Not Found
- **Error:** Specified device ID not found
- **Solution:** Use `list` command to find correct device ID

#### Backup Failed
- **Error:** Unable to create backup
- **Solution:** Check disk space and permissions

#### Update Failed
- **Error:** Driver update unsuccessful
- **Solution:** Use `rollback` command to restore previous version

### Exception Types

#### DriverNotFoundException
Thrown when specified driver cannot be found.

#### BackupException
Thrown when backup operations fail.

#### UpdateException
Thrown when driver updates fail.

## Usage Examples

### Complete Driver Maintenance Workflow

```csharp
// Initialize services
var backupService = new BackupService();
var whqlService = new WhqlDatabaseService();
var settingsService = new SettingsService();
var driverService = new DriverService(whqlService, backupService);

// Get all drivers
var drivers = await driverService.GetDriversAsync();

// Scan for updates
var updates = await driverService.ScanForDriversAsync();

// Process updates
foreach (var update in updates)
{
    // Create backup first
    await driverService.BackupDriverAsync(update.DeviceID);
    
    // Update driver
    var success = await driverService.UpdateDriverAsync(update.DeviceID);
    
    if (!success)
    {
        // Rollback on failure
        await driverService.RollbackDriverAsync(update.DeviceID);
    }
}
```

### Settings Management

```csharp
var settingsService = new SettingsService();

// Enable auto-updates
settingsService.AutoUpdateEnabled = true;
settingsService.BackupEnabled = true;
settingsService.MaxBackupGenerations = 5;

// Save settings
settingsService.Save();
```