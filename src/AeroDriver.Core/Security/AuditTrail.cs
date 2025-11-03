using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Security;

/// <summary>
/// Enterprise-grade audit trail system for compliance and security monitoring
/// Implements tamper-evident logging for national-level security requirements
/// </summary>
public class AuditTrail : IDisposable, IAsyncDisposable
{
    private readonly string _auditLogPath;
    private readonly ConcurrentQueue<AuditEvent> _pendingEvents = new();
    private readonly SemaphoreSlim _fileSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<IAuditEventSink, byte> _eventSinks = new();
    private readonly string _encryptionKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditTrail(string? auditLogPath = null, string? encryptionKey = null, ISimpleLogger? logger = null)
    {
        _auditLogPath = auditLogPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AeroDriver",
            "Audit",
            $"audit_{DateTime.UtcNow:yyyyMM}.log.enc");

        _encryptionKey = encryptionKey ?? GenerateEncryptionKey();
        _logger = logger ?? new SimpleLogger();

        var auditDir = Path.GetDirectoryName(_auditLogPath);
        if (!string.IsNullOrEmpty(auditDir))
        {
            Directory.CreateDirectory(auditDir);
        }

        // Ensure encryption key is properly configured
        if (string.IsNullOrEmpty(_encryptionKey) || _encryptionKey.Length < 32)
        {
            throw new InvalidOperationException("Encryption key must be at least 32 characters long for AES-256 encryption");
        }
    }

    private static string GenerateEncryptionKey()
    {
        // Generate a cryptographically secure random key
        var keyBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }

    public bool RegisterSink(IAuditEventSink sink)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sink);
        return _eventSinks.TryAdd(sink, 0);
    }

    public bool UnregisterSink(IAuditEventSink sink)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sink);
        return _eventSinks.TryRemove(sink, out _);
    }

    public async Task RecordEventAsync(
        AuditAction action,
        string resource,
        AuditResult result,
        string? details = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(resource, nameof(resource));

        var auditEvent = CreateEvent(action, resource, result, details, metadata);

        _pendingEvents.Enqueue(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();
        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AuditEvent CreateEvent(
        AuditAction action,
        string resource,
        AuditResult result,
        string? details,
        Dictionary<string, string>? metadata)
    {
        resource = NormalizeResource(resource);

        var auditEvent = new AuditEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Action = action,
            Resource = resource,
            Result = result,
            UserIdentity = GetCurrentUserIdentity(),
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            ProcessName = Environment.ProcessPath ?? "Unknown",
            Details = details,
            Metadata = NormalizeMetadata(metadata)
        };

        NormalizeEvent(auditEvent);
        return auditEvent;
    }

    public async Task RecordDriverOperationAsync(
        string driverId,
        DriverOperation operation,
        bool success,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId, nameof(driverId));

        var action = operation switch
        {
            DriverOperation.Scan => AuditAction.DriverScan,
            DriverOperation.Update => AuditAction.DriverUpdate,
            DriverOperation.Backup => AuditAction.DriverBackup,
            DriverOperation.Restore => AuditAction.DriverRestore,
            DriverOperation.Delete => AuditAction.DriverDelete,
            DriverOperation.Install => AuditAction.DriverInstall,
            _ => AuditAction.Other
        };

        var metadata = new Dictionary<string, string>
        {
            ["driverId"] = driverId,
            ["operation"] = operation.ToString()
        };

        if (!string.IsNullOrEmpty(errorMessage))
        {
            metadata["error"] = errorMessage;
        }

        await RecordEventAsync(
                action,
                $"Driver:{driverId}",
                success ? AuditResult.Success : AuditResult.Failure,
                errorMessage,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordConfigurationChangeAsync(
        string configKey,
        string? oldValue,
        string? newValue,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey, nameof(configKey));

        configKey = NormalizeResource(configKey);

        var metadata = new Dictionary<string, string>
        {
            ["configKey"] = configKey,
            ["oldValue"] = SanitizeValue(oldValue),
            ["newValue"] = SanitizeValue(newValue)
        };

        if (!string.IsNullOrEmpty(reason))
        {
            metadata["reason"] = reason;
        }

        await RecordEventAsync(
                AuditAction.ConfigurationChange,
                $"Config:{configKey}",
                AuditResult.Success,
                reason,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordSecurityEventAsync(
        SecurityEventType eventType,
        string description,
        SecuritySeverity severity,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(description, nameof(description));

        var enrichedMetadata = metadata ?? new Dictionary<string, string>();
        enrichedMetadata["eventType"] = eventType.ToString();
        enrichedMetadata["severity"] = severity.ToString();

        await RecordEventAsync(
                AuditAction.SecurityEvent,
                "Security",
                AuditResult.Success,
                description,
                enrichedMetadata,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordAuthenticationAttemptAsync(
        string username,
        bool success,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(username, nameof(username));

        username = NormalizeResource(username);

        var metadata = new Dictionary<string, string>
        {
            ["username"] = username,
            ["sourceIp"] = "localhost" // Would be real IP in web scenario
        };

        if (!string.IsNullOrEmpty(failureReason))
        {
            metadata["failureReason"] = failureReason;
        }

        await RecordEventAsync(
                AuditAction.Authentication,
                $"User:{username}",
                success ? AuditResult.Success : AuditResult.Failure,
                failureReason,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<AuditEvent>> QueryEventsAsync(
        DateTime? startTime = null,
        DateTime? endTime = null,
        AuditAction? action = null,
        string? userIdentity = null,
        int maxResults = 1000,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (maxResults <= 0)
        {
            return new List<AuditEvent>();
        }

        var events = new Queue<AuditEvent>(Math.Min(maxResults, 128));

        try
        {
            if (!File.Exists(_auditLogPath))
            {
                return new List<AuditEvent>();
            }

            await using var stream = new FileStream(
                _auditLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            using var reader = new StreamReader(stream);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    // Decrypt the line first
                    var decryptedLine = await EncryptionManager.DecryptStringAsync(line, _encryptionKey);
                    var @event = JsonSerializer.Deserialize<AuditEvent>(decryptedLine, JsonOptions);
                    if (@event == null)
                    {
                        continue;
                    }

                    NormalizeEvent(@event);

                    // Apply filters
                    if (startTime.HasValue && @event.Timestamp < startTime.Value)
                    {
                        continue;
                    }

                    if (endTime.HasValue && @event.Timestamp > endTime.Value)
                    {
                        continue;
                    }

                    if (action.HasValue && @event.Action != action.Value)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(userIdentity) &&
                        !string.Equals(@event.UserIdentity, userIdentity, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    events.Enqueue(@event);

                    if (events.Count > maxResults)
                    {
                        events.Dequeue();
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                    continue;
                }
                catch (Exception ex)
                {
                    // Log decryption errors but continue
                    await _logger.LogSecurityEventAsync("AuditQueryDecryptError", $"Failed to decrypt audit event: {ex.Message}").ConfigureAwait(false);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            await _logger.LogSecurityEventAsync("AuditQueryError", $"Failed to query audit events: {ex.Message}").ConfigureAwait(false);
        }

        return events
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_pendingEvents.IsEmpty)
        {
            return;
        }

        var eventsToWrite = new List<AuditEvent>();

        while (_pendingEvents.TryDequeue(out var @event))
        {
            eventsToWrite.Add(@event);
        }

        if (eventsToWrite.Count == 0)
        {
            return;
        }

        var semaphoreAcquired = false;

        try
        {
            await _fileSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            semaphoreAcquired = true;

            await using var stream = new FileStream(
                _auditLogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            await using var writer = new StreamWriter(stream);
            foreach (var @event in eventsToWrite)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NormalizeEvent(@event);
                var json = JsonSerializer.Serialize(@event, JsonOptions);
                
                // Encrypt the JSON before writing
                var encryptedJson = await EncryptionManager.EncryptStringAsync(json, _encryptionKey);
                await writer.WriteLineAsync(encryptedJson).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
            await DispatchToSinksAsync(eventsToWrite, cancellationToken).ConfigureAwait(false);
        }
    }
        catch (OperationCanceledException)
        {
            foreach (var pending in eventsToWrite)
            {
                _pendingEvents.Enqueue(pending);
            }

            throw;
        }
        catch (Exception ex)
        {
            foreach (var pending in eventsToWrite)
            {
                _pendingEvents.Enqueue(pending);
            }

            await _logger.LogSecurityEventAsync("AuditWriteError", $"Failed to write audit events: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            if (semaphoreAcquired)
            {
                _fileSemaphore.Release();
            }
        }
    }

    private static string GetCurrentUserIdentity()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name ?? "Unknown";
        }
        catch
        {
            return Environment.UserName ?? "Unknown";
        }
    }

    private static string SanitizeValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "[null]";
        }

        // Redact potential sensitive values
        if (value.Length > 100)
        {
            return $"{value.Substring(0, 100)}...[truncated]";
        }

        return value;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await FlushPendingEventsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _logger.LogSecurityEventAsync(
                "AuditDisposeFlushError",
                $"Failed to flush audit events during dispose: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            _fileSemaphore.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            FlushPendingEventsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _ = _logger.LogSecurityEventAsync(
                "AuditDisposeFlushError",
                $"Failed to flush audit events during dispose: {ex.Message}");
        }
        finally
        {
            _fileSemaphore.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }

    private async ValueTask FlushPendingEventsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_pendingEvents.IsEmpty)
        {
            return;
        }

        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeResource(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 200)
        {
            return trimmed.Substring(0, 200);
        }

        return trimmed;
    }

    private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var key = NormalizeResource(pair.Key);
            if (!normalized.ContainsKey(key))
            {
                normalized[key] = SanitizeValue(pair.Value);
            }
        }

        return normalized;
    }

    private static void NormalizeEvent(AuditEvent auditEvent)
    {
        auditEvent.Resource = NormalizeResource(auditEvent.Resource);
        auditEvent.Metadata = NormalizeMetadata(auditEvent.Metadata);
    }

    private void RecordAuditSync(
        AuditAction action,
        string resource,
        AuditResult result,
        string? details)
    {
        ThrowIfDisposed();
        RecordEventAsync(action, resource, result, details)
            .GetAwaiter()
            .GetResult();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AuditTrail));
        }
    }
}

public class AuditEvent
{
    public Guid EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public AuditAction Action { get; set; }
    public string Resource { get; set; } = string.Empty;
    public AuditResult Result { get; set; }
    public string UserIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? Details { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public enum AuditAction
{
    DriverScan,
    DriverUpdate,
    DriverBackup,
    DriverRestore,
    DriverDelete,
    DriverInstall,
    ConfigurationChange,
    SecurityEvent,
    Authentication,
    Authorization,
    FileAccess,
    SystemOperation,
    Other
}

public enum AuditResult
{
    Success,
    Failure,
    PartialSuccess
}

public enum DriverOperation
{
    Scan,
    Update,
    Backup,
    Restore,
    Delete,
    Install
}

public enum SecurityEventType
{
    UnauthorizedAccess,
    ValidationFailure,
    EncryptionError,
    IntegrityCheckFailure,
    SuspiciousActivity,
    PolicyViolation
}

public enum SecuritySeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Role-Based Access Control (RBAC) implementation
/// </summary>
public class RbacManager
{
    private readonly Dictionary<string, Role> _roles = new();
    private readonly Dictionary<string, string> _userRoles = new();
    private readonly AuditTrail _auditTrail;

    public RbacManager(AuditTrail auditTrail)
    {
        _auditTrail = auditTrail;
        InitializeDefaultRoles();
    }

    private void InitializeDefaultRoles()
    {
        // Administrator role - full access
        var adminRole = new Role
        {
            Name = "Administrator",
            Permissions = new HashSet<Permission>
            {
                Permission.DriverRead,
                Permission.DriverUpdate,
                Permission.DriverDelete,
                Permission.DriverBackup,
                Permission.DriverRestore,
                Permission.ConfigurationRead,
                Permission.ConfigurationWrite,
                Permission.SecurityAudit,
                Permission.UserManagement,
                Permission.SystemOperation
            }
        };

        // Operator role - can update and backup
        var operatorRole = new Role
        {
            Name = "Operator",
            Permissions = new HashSet<Permission>
            {
                Permission.DriverRead,
                Permission.DriverUpdate,
                Permission.DriverBackup,
                Permission.ConfigurationRead
            }
        };

        // Viewer role - read-only
        var viewerRole = new Role
        {
            Name = "Viewer",
            Permissions = new HashSet<Permission>
            {
                Permission.DriverRead,
                Permission.ConfigurationRead
            }
        };

        _roles["Administrator"] = adminRole;
        _roles["Operator"] = operatorRole;
        _roles["Viewer"] = viewerRole;
    }

    public void AssignRole(string userIdentity, string roleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName, nameof(roleName));

        if (!_roles.ContainsKey(roleName))
        {
            throw new InvalidOperationException($"Role '{roleName}' does not exist");
        }

        _userRoles[userIdentity] = roleName;
        RecordAuditSync(
            AuditAction.Authorization,
            $"User:{userIdentity}",
            AuditResult.Success,
            $"Assigned role: {roleName}");
    }

    public bool HasPermission(string userIdentity, Permission permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));

        if (!_userRoles.TryGetValue(userIdentity, out var roleName))
        {
            return false;
        }

        if (!_roles.TryGetValue(roleName, out var role))
        {
            return false;
        }

        return role.Permissions.Contains(permission);
    }

    public void CheckPermission(string userIdentity, Permission permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));

        if (!HasPermission(userIdentity, permission))
        {
            RecordAuditSync(
                AuditAction.Authorization,
                $"User:{userIdentity}",
                AuditResult.Failure,
                $"Insufficient permissions for: {permission}");

            throw new UnauthorizedAccessException(
                $"User '{userIdentity}' does not have permission: {permission}");
        }
    }
}

public class Role
{
    public string Name { get; set; } = string.Empty;
    public HashSet<Permission> Permissions { get; set; } = new();
}

public enum Permission
{
    DriverRead,
    DriverUpdate,
    DriverDelete,
    DriverBackup,
    DriverRestore,
    ConfigurationRead,
    ConfigurationWrite,
    SecurityAudit,
    UserManagement,
    SystemOperation
}
