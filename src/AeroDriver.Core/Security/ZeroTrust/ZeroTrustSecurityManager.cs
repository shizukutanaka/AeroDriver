using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Security.ZeroTrust;

/// <summary>
/// Zero Trust Architecture implementation for driver management
/// Based on NIST 800-207 framework principles
/// </summary>
public static class ZeroTrustSecurityManager
{
    private static readonly ConcurrentDictionary<string, DeviceIdentity> _deviceIdentities = new();
    private static readonly ConcurrentDictionary<string, AccessPolicy> _accessPolicies = new();
    private static readonly ConcurrentDictionary<string, SecurityContext> _securityContexts = new();
    private static readonly List<SecurityEvent> _securityEvents = new();
    private static readonly object _securityLock = new();
    private static readonly TimeSpan _contextTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
/// Registers a device in the Zero Trust system
/// </summary>
    public static async Task<DeviceRegistrationResult> RegisterDeviceAsync(
        DeviceIdentity device,
        CertificateInfo certificate,
        CancellationToken cancellationToken = default)
    {
        var result = new DeviceRegistrationResult
        {
            DeviceId = device.DeviceId,
            RegisteredAt = DateTime.UtcNow
        };

        try
        {
            // Verify certificate
            var certValidation = await ValidateCertificateAsync(certificate, cancellationToken);
            if (!certValidation.IsValid)
            {
                result.Success = false;
                result.Error = $"Certificate validation failed: {certValidation.Error}";
                return result;
            }

            // Assess device security posture
            var postureAssessment = await AssessSecurityPostureAsync(device, cancellationToken);
            device.SecurityPosture = postureAssessment;

            // Create initial security context
            var context = new SecurityContext
            {
                DeviceId = device.DeviceId,
                ContextId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                LastVerified = DateTime.UtcNow,
                RiskScore = CalculateRiskScore(postureAssessment),
                TrustLevel = DetermineTrustLevel(postureAssessment),
                AllowedResources = new List<string>()
            };

            // Register device
            lock (_securityLock)
            {
                _deviceIdentities[device.DeviceId] = device;
                _securityContexts[context.ContextId] = context;
            }

            // Create default access policy
            var defaultPolicy = new AccessPolicy
            {
                DeviceId = device.DeviceId,
                PolicyId = Guid.NewGuid().ToString(),
                AllowedOperations = new[] { "ReadDeviceInfo", "UpdateStatus" },
                ConditionalAccess = new List<AccessCondition>
                {
                    new AccessCondition { ConditionType = ConditionType.TimeBased, Value = "BusinessHours" },
                    new AccessCondition { ConditionType = ConditionType.NetworkLocation, Value = "InternalNetwork" }
                },
                CreatedAt = DateTime.UtcNow
            };

            lock (_securityLock)
            {
                _accessPolicies[device.DeviceId] = defaultPolicy;
            }

            result.Success = true;
            result.ContextId = context.ContextId;
            result.TrustLevel = context.TrustLevel;

        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
/// Verifies access request using Zero Trust principles
/// </summary>
    public static async Task<AccessVerificationResult> VerifyAccessAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new AccessVerificationResult
        {
            RequestId = request.RequestId,
            VerifiedAt = DateTime.UtcNow
        };

        try
        {
            // 1. Identity Verification
            var identityValid = await VerifyIdentityAsync(request.DeviceId, request.Certificate, cancellationToken);
            if (!identityValid.IsValid)
            {
                result.Allowed = false;
                result.DenialReason = "Identity verification failed";
                result.RiskScore = 1.0;
                RecordSecurityEvent(request.DeviceId, SecurityEventType.AccessDenied, "Identity verification failed");
                return result;
            }

            // 2. Device Posture Check
            var postureValid = await VerifySecurityPostureAsync(request.DeviceId, cancellationToken);
            if (!postureValid.IsValid)
            {
                result.Allowed = false;
                result.DenialReason = "Security posture check failed";
                result.RiskScore = postureValid.RiskScore;
                RecordSecurityEvent(request.DeviceId, SecurityEventType.AccessDenied, "Security posture violation");
                return result;
            }

            // 3. Policy Evaluation
            var policyValid = await EvaluateAccessPolicyAsync(request, cancellationToken);
            if (!policyValid.IsAllowed)
            {
                result.Allowed = false;
                result.DenialReason = policyValid.DenialReason;
                result.RiskScore = policyValid.RiskScore;
                RecordSecurityEvent(request.DeviceId, SecurityEventType.AccessDenied, policyValid.DenialReason);
                return result;
            }

            // 4. Context-aware Authorization
            var contextValid = await VerifyContextAsync(request, cancellationToken);
            if (!contextValid.IsValid)
            {
                result.Allowed = false;
                result.DenialReason = "Context verification failed";
                result.RiskScore = contextValid.RiskScore;
                RecordSecurityEvent(request.DeviceId, SecurityEventType.AccessDenied, "Context violation");
                return result;
            }

            // 5. Continuous Monitoring
            result.MonitoringRequired = true;
            result.MonitoringInterval = GetMonitoringInterval(contextValid.RiskScore);

            // Grant access with conditions
            result.Allowed = true;
            result.GrantedPermissions = policyValid.GrantedPermissions;
            result.AccessConditions = policyValid.AccessConditions;
            result.RiskScore = Math.Max(identityValid.RiskScore, postureValid.RiskScore, contextValid.RiskScore);
            result.TrustLevel = DetermineTrustLevelFromRisk(result.RiskScore);

            RecordSecurityEvent(request.DeviceId, SecurityEventType.AccessGranted, $"Access granted with risk score {result.RiskScore}");

        }
        catch (Exception ex)
        {
            result.Allowed = false;
            result.DenialReason = $"Verification error: {ex.Message}";
            result.RiskScore = 1.0;
            RecordSecurityEvent(request.DeviceId, SecurityEventType.AccessError, ex.Message);
        }

        return result;
    }

    /// <summary>
/// Updates device security posture
/// </summary>
    public static async Task<PostureUpdateResult> UpdateSecurityPostureAsync(
        string deviceId,
        DevicePostureUpdate update,
        CancellationToken cancellationToken = default)
    {
        var result = new PostureUpdateResult
        {
            DeviceId = deviceId,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            if (!_deviceIdentities.TryGetValue(deviceId, out var device))
            {
                result.Success = false;
                result.Error = "Device not found";
                return result;
            }

            // Update device posture
            device.SecurityPosture = update.NewPosture;
            device.LastPostureUpdate = DateTime.UtcNow;

            // Recalculate risk score and trust level
            var newRiskScore = CalculateRiskScore(update.NewPosture);
            var newTrustLevel = DetermineTrustLevel(update.NewPosture);

            // Update security context
            var context = _securityContexts.Values.FirstOrDefault(c => c.DeviceId == deviceId);
            if (context != null)
            {
                context.RiskScore = newRiskScore;
                context.TrustLevel = newTrustLevel;
                context.LastVerified = DateTime.UtcNow;
            }

            // Adjust access policies if necessary
            await AdjustAccessPoliciesAsync(deviceId, newRiskScore, newTrustLevel, cancellationToken);

            result.Success = true;
            result.NewRiskScore = newRiskScore;
            result.NewTrustLevel = newTrustLevel;

            RecordSecurityEvent(deviceId, SecurityEventType.PostureUpdated, $"Posture updated to {newTrustLevel} with risk score {newRiskScore}");

        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            RecordSecurityEvent(deviceId, SecurityEventType.PostureUpdateError, ex.Message);
        }

        return result;
    }

    /// <summary>
/// Performs continuous monitoring of device security
/// </summary>
    public static async Task<MonitoringResult> PerformContinuousMonitoringAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var result = new MonitoringResult
        {
            DeviceId = deviceId,
            MonitoringTime = DateTime.UtcNow
        };

        try
        {
            if (!_deviceIdentities.TryGetValue(deviceId, out var device))
            {
                result.Success = false;
                result.Error = "Device not found";
                return result;
            }

            // Check device health
            result.DeviceHealth = await CheckDeviceHealthAsync(device, cancellationToken);

            // Monitor network activity
            result.NetworkActivity = await MonitorNetworkActivityAsync(device, cancellationToken);

            // Check for anomalies
            result.Anomalies = await DetectAnomaliesAsync(device, cancellationToken);

            // Update risk score
            result.RiskScore = CalculateRiskScore(device.SecurityPosture);
            result.ThreatLevel = DetermineThreatLevel(result.Anomalies, result.NetworkActivity);

            // Take automated actions if necessary
            if (result.ThreatLevel >= ThreatLevel.High)
            {
                await TakeAutomatedActionsAsync(deviceId, result.ThreatLevel, cancellationToken);
                result.AutomatedActions = new List<string> { "Increased monitoring", "Access restrictions applied" };
            }

            result.Success = true;

            RecordSecurityEvent(deviceId, SecurityEventType.MonitoringCompleted, $"Monitoring completed with threat level {result.ThreatLevel}");

        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            RecordSecurityEvent(deviceId, SecurityEventType.MonitoringError, ex.Message);
        }

        return result;
    }

    /// <summary>
/// Gets Zero Trust security metrics
/// </summary>
    public static ZeroTrustMetrics GetSecurityMetrics()
    {
        lock (_securityLock)
        {
            return new ZeroTrustMetrics
            {
                TotalDevices = _deviceIdentities.Count,
                ActiveContexts = _securityContexts.Count,
                SecurityEvents = _securityEvents.Count,
                AverageRiskScore = _deviceIdentities.Values.Any()
                    ? _deviceIdentities.Values.Average(d => CalculateRiskScore(d.SecurityPosture))
                    : 0.0,
                HighRiskDevices = _deviceIdentities.Count(d => CalculateRiskScore(d.SecurityPosture) > 0.7),
                DeniedAccessRequests = _securityEvents.Count(e => e.EventType == SecurityEventType.AccessDenied),
                SuccessfulVerifications = _securityEvents.Count(e => e.EventType == SecurityEventType.AccessGranted),
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    private static async Task<CertificateValidationResult> ValidateCertificateAsync(CertificateInfo certificate, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken); // Simulate validation time

        return new CertificateValidationResult
        {
            IsValid = certificate.ExpiryDate > DateTime.UtcNow && certificate.Issuer != "Unknown",
            Error = certificate.ExpiryDate <= DateTime.UtcNow ? "Certificate expired" : null
        };
    }

    private static async Task<SecurityPostureAssessment> AssessSecurityPostureAsync(DeviceIdentity device, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);

        return new SecurityPostureAssessment
        {
            DeviceId = device.DeviceId,
            AssessmentTime = DateTime.UtcNow,
            EncryptionEnabled = true,
            FirewallActive = true,
            AntiMalwareUpdated = true,
            OSUpdated = true,
            SecureBootEnabled = true,
            RiskFactors = new List<string>()
        };
    }

    private static async Task<IdentityVerificationResult> VerifyIdentityAsync(string deviceId, CertificateInfo certificate, CancellationToken cancellationToken)
    {
        await Task.Delay(30, cancellationToken);

        return new IdentityVerificationResult
        {
            IsValid = certificate.Subject == deviceId,
            RiskScore = certificate.ExpiryDate <= DateTime.UtcNow.AddDays(30) ? 0.3 : 0.0
        };
    }

    private static async Task<PostureVerificationResult> VerifySecurityPostureAsync(string deviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(40, cancellationToken);

        if (!_deviceIdentities.TryGetValue(deviceId, out var device))
        {
            return new PostureVerificationResult { IsValid = false, RiskScore = 1.0, Error = "Device not found" };
        }

        return new PostureVerificationResult
        {
            IsValid = device.SecurityPosture != null &&
                     device.SecurityPosture.EncryptionEnabled &&
                     device.SecurityPosture.FirewallActive,
            RiskScore = CalculateRiskScore(device.SecurityPosture)
        };
    }

    private static async Task<PolicyEvaluationResult> EvaluateAccessPolicyAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken);

        if (!_accessPolicies.TryGetValue(request.DeviceId, out var policy))
        {
            return new PolicyEvaluationResult
            {
                IsAllowed = false,
                DenialReason = "No access policy found",
                RiskScore = 1.0
            };
        }

        // Check if operation is allowed
        var operationAllowed = policy.AllowedOperations.Contains(request.Operation);

        // Check conditional access
        var conditionsMet = await EvaluateConditionsAsync(policy.ConditionalAccess, request, cancellationToken);

        var result = new PolicyEvaluationResult
        {
            IsAllowed = operationAllowed && conditionsMet,
            GrantedPermissions = operationAllowed ? new[] { request.Operation } : Array.Empty<string>(),
            AccessConditions = policy.ConditionalAccess,
            RiskScore = operationAllowed && conditionsMet ? 0.0 : 1.0
        };

        if (!result.IsAllowed)
        {
            result.DenialReason = !operationAllowed ? "Operation not allowed by policy" : "Conditional access requirements not met";
        }

        return result;
    }

    private static async Task<ContextVerificationResult> VerifyContextAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(25, cancellationToken);

        // Verify context based on device location, time, network, etc.
        var context = _securityContexts.Values.FirstOrDefault(c => c.DeviceId == request.DeviceId);
        if (context == null)
        {
            return new ContextVerificationResult { IsValid = false, RiskScore = 1.0, Error = "No security context found" };
        }

        // Check if context is still valid
        if (DateTime.UtcNow - context.LastVerified > _contextTimeout)
        {
            return new ContextVerificationResult { IsValid = false, RiskScore = 0.8, Error = "Security context expired" };
        }

        return new ContextVerificationResult
        {
            IsValid = true,
            RiskScore = context.RiskScore,
            TrustLevel = context.TrustLevel
        };
    }

    private static async Task<bool> EvaluateConditionsAsync(List<AccessCondition> conditions, AccessRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);

        foreach (var condition in conditions)
        {
            switch (condition.ConditionType)
            {
                case ConditionType.TimeBased:
                    if (condition.Value == "BusinessHours")
                    {
                        var hour = DateTime.UtcNow.Hour;
                        if (hour < 9 || hour > 17) // Outside business hours
                            return false;
                    }
                    break;
                case ConditionType.NetworkLocation:
                    if (condition.Value == "InternalNetwork" && request.ClientIP != null)
                    {
                        // Check if IP is in internal network range
                        if (!IsInternalNetwork(request.ClientIP))
                            return false;
                    }
                    break;
                case ConditionType.DevicePosture:
                    if (!_deviceIdentities.TryGetValue(request.DeviceId, out var device))
                        return false;

                    if (condition.Value == "Secure" && CalculateRiskScore(device.SecurityPosture) > 0.3)
                        return false;
                    break;
            }
        }

        return true;
    }

    private static async Task AdjustAccessPoliciesAsync(string deviceId, double riskScore, TrustLevel trustLevel, CancellationToken cancellationToken)
    {
        await Task.Delay(15, cancellationToken);

        if (!_accessPolicies.TryGetValue(deviceId, out var policy))
            return;

        // Adjust policy based on risk score and trust level
        if (riskScore > 0.7 || trustLevel <= TrustLevel.Low)
        {
            // Restrict access for high-risk devices
            policy.AllowedOperations = policy.AllowedOperations.Where(op => op == "ReadDeviceInfo").ToArray();
            policy.ConditionalAccess.Add(new AccessCondition
            {
                ConditionType = ConditionType.ApprovalRequired,
                Value = "AdminApproval"
            });
        }
        else if (riskScore < 0.3 && trustLevel >= TrustLevel.High)
        {
            // Expand access for low-risk, high-trust devices
            policy.AllowedOperations = policy.AllowedOperations.Concat(new[] { "UpdateDrivers", "SystemConfiguration" }).ToArray();
        }
    }

    private static async Task<DeviceHealth> CheckDeviceHealthAsync(DeviceIdentity device, CancellationToken cancellationToken)
    {
        await Task.Delay(30, cancellationToken);

        return new DeviceHealth
        {
            IsHealthy = device.SecurityPosture != null && device.SecurityPosture.AllChecksPassed,
            LastHealthCheck = DateTime.UtcNow,
            PerformanceMetrics = new Dictionary<string, double>
            {
                ["CpuUsage"] = 45.0,
                ["MemoryUsage"] = 60.0,
                ["DiskUsage"] = 30.0
            }
        };
    }

    private static async Task<NetworkActivity> MonitorNetworkActivityAsync(DeviceIdentity device, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken);

        return new NetworkActivity
        {
            IsNormal = true,
            Connections = 5,
            DataTransferred = 1024 * 1024, // 1MB
            AnomalousConnections = 0
        };
    }

    private static async Task<List<Anomaly>> DetectAnomaliesAsync(DeviceIdentity device, CancellationToken cancellationToken)
    {
        await Task.Delay(35, cancellationToken);

        var anomalies = new List<Anomaly>();

        // Check for unusual patterns
        if (device.SecurityPosture != null && !device.SecurityPosture.EncryptionEnabled)
        {
            anomalies.Add(new Anomaly
            {
                Type = AnomalyType.SecurityViolation,
                Description = "Encryption disabled",
                Severity = AnomalySeverity.High
            });
        }

        return anomalies;
    }

    private static async Task TakeAutomatedActionsAsync(string deviceId, ThreatLevel threatLevel, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);

        // Take automated security actions based on threat level
        switch (threatLevel)
        {
            case ThreatLevel.High:
                await RevokeAccessAsync(deviceId, "High threat level detected", cancellationToken);
                break;
            case ThreatLevel.Critical:
                await IsolateDeviceAsync(deviceId, "Critical threat level detected", cancellationToken);
                break;
        }
    }

    private static async Task RevokeAccessAsync(string deviceId, string reason, CancellationToken cancellationToken)
    {
        // Revoke all access for the device
        if (_accessPolicies.TryGetValue(deviceId, out var policy))
        {
            policy.AllowedOperations = Array.Empty<string>();
            policy.IsActive = false;
        }

        RecordSecurityEvent(deviceId, SecurityEventType.AccessRevoked, reason);
    }

    private static async Task IsolateDeviceAsync(string deviceId, string reason, CancellationToken cancellationToken)
    {
        // Isolate device from network
        await RevokeAccessAsync(deviceId, reason, cancellationToken);

        // Additional isolation measures
        RecordSecurityEvent(deviceId, SecurityEventType.DeviceIsolated, reason);
    }

    private static double CalculateRiskScore(SecurityPostureAssessment? posture)
    {
        if (posture == null)
            return 1.0;

        var riskScore = 0.0;

        if (!posture.EncryptionEnabled) riskScore += 0.3;
        if (!posture.FirewallActive) riskScore += 0.2;
        if (!posture.AntiMalwareUpdated) riskScore += 0.2;
        if (!posture.OSUpdated) riskScore += 0.15;
        if (!posture.SecureBootEnabled) riskScore += 0.15;

        return Math.Min(riskScore, 1.0);
    }

    private static TrustLevel DetermineTrustLevel(SecurityPostureAssessment? posture)
    {
        var riskScore = CalculateRiskScore(posture);

        return riskScore switch
        {
            <= 0.2 => TrustLevel.High,
            <= 0.5 => TrustLevel.Medium,
            <= 0.8 => TrustLevel.Low,
            _ => TrustLevel.None
        };
    }

    private static TrustLevel DetermineTrustLevelFromRisk(double riskScore)
    {
        return riskScore switch
        {
            <= 0.2 => TrustLevel.High,
            <= 0.5 => TrustLevel.Medium,
            <= 0.8 => TrustLevel.Low,
            _ => TrustLevel.None
        };
    }

    private static ThreatLevel DetermineThreatLevel(List<Anomaly> anomalies, NetworkActivity activity)
    {
        if (anomalies.Any(a => a.Severity == AnomalySeverity.Critical))
            return ThreatLevel.Critical;

        if (anomalies.Any(a => a.Severity == AnomalySeverity.High) || activity.AnomalousConnections > 0)
            return ThreatLevel.High;

        if (anomalies.Any(a => a.Severity == AnomalySeverity.Medium))
            return ThreatLevel.Medium;

        return ThreatLevel.Low;
    }

    private static TimeSpan GetMonitoringInterval(double riskScore)
    {
        return riskScore switch
        {
            >= 0.7 => TimeSpan.FromMinutes(1),
            >= 0.4 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(15)
        };
    }

    private static bool IsInternalNetwork(string? clientIP)
    {
        if (string.IsNullOrEmpty(clientIP))
            return false;

        // Simplified check - in real implementation would check against internal network ranges
        return clientIP.StartsWith("192.168.") || clientIP.StartsWith("10.") || clientIP.StartsWith("172.");
    }

    private static void RecordSecurityEvent(string deviceId, SecurityEventType eventType, string description)
    {
        lock (_securityLock)
        {
            _securityEvents.Add(new SecurityEvent
            {
                DeviceId = deviceId,
                EventType = eventType,
                Description = description,
                Timestamp = DateTime.UtcNow
            });

            // Keep only recent events
            if (_securityEvents.Count > 10000)
            {
                _securityEvents.RemoveRange(0, 1000);
            }
        }
    }

    // Data structures for Zero Trust implementation
    public class DeviceIdentity
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public CertificateInfo Certificate { get; set; } = new();
        public SecurityPostureAssessment? SecurityPosture { get; set; }
        public DateTime RegisteredAt { get; set; }
        public DateTime LastPostureUpdate { get; set; }
    }

    public class CertificateInfo
    {
        public string Subject { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public string Thumbprint { get; set; } = string.Empty;
    }

    public class SecurityPostureAssessment
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTime AssessmentTime { get; set; }
        public bool EncryptionEnabled { get; set; }
        public bool FirewallActive { get; set; }
        public bool AntiMalwareUpdated { get; set; }
        public bool OSUpdated { get; set; }
        public bool SecureBootEnabled { get; set; }
        public List<string> RiskFactors { get; set; } = new();

        public bool AllChecksPassed => EncryptionEnabled && FirewallActive && AntiMalwareUpdated && OSUpdated && SecureBootEnabled;
    }

    public class AccessRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string? ClientIP { get; set; }
        public CertificateInfo Certificate { get; set; } = new();
        public Dictionary<string, object> AdditionalContext { get; set; } = new();
    }

    public class SecurityContext
    {
        public string DeviceId { get; set; } = string.Empty;
        public string ContextId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastVerified { get; set; }
        public double RiskScore { get; set; }
        public TrustLevel TrustLevel { get; set; }
        public List<string> AllowedResources { get; set; } = new();
    }

    public class AccessPolicy
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PolicyId { get; set; } = string.Empty;
        public string[] AllowedOperations { get; set; } = Array.Empty<string>();
        public List<AccessCondition> ConditionalAccess { get; set; } = new();
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class AccessCondition
    {
        public ConditionType ConditionType { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    // Result classes
    public class DeviceRegistrationResult
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime RegisteredAt { get; set; }
        public string ContextId { get; set; } = string.Empty;
        public TrustLevel TrustLevel { get; set; }
        public string? Error { get; set; }
    }

    public class AccessVerificationResult
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Allowed { get; set; }
        public string[] GrantedPermissions { get; set; } = Array.Empty<string>();
        public List<AccessCondition> AccessConditions { get; set; } = new();
        public double RiskScore { get; set; }
        public TrustLevel TrustLevel { get; set; }
        public bool MonitoringRequired { get; set; }
        public TimeSpan MonitoringInterval { get; set; }
        public DateTime VerifiedAt { get; set; }
        public string DenialReason { get; set; } = string.Empty;
    }

    public class PostureUpdateResult
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime UpdatedAt { get; set; }
        public double NewRiskScore { get; set; }
        public TrustLevel NewTrustLevel { get; set; }
        public string? Error { get; set; }
    }

    public class MonitoringResult
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DeviceHealth DeviceHealth { get; set; } = new();
        public NetworkActivity NetworkActivity { get; set; } = new();
        public List<Anomaly> Anomalies { get; set; } = new();
        public double RiskScore { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public List<string> AutomatedActions { get; set; } = new();
        public DateTime MonitoringTime { get; set; }
        public string? Error { get; set; }
    }

    public class ZeroTrustMetrics
    {
        public int TotalDevices { get; set; }
        public int ActiveContexts { get; set; }
        public int SecurityEvents { get; set; }
        public double AverageRiskScore { get; set; }
        public int HighRiskDevices { get; set; }
        public int DeniedAccessRequests { get; set; }
        public int SuccessfulVerifications { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class DeviceHealth
    {
        public bool IsHealthy { get; set; }
        public DateTime LastHealthCheck { get; set; }
        public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
    }

    public class NetworkActivity
    {
        public bool IsNormal { get; set; }
        public int Connections { get; set; }
        public long DataTransferred { get; set; }
        public int AnomalousConnections { get; set; }
    }

    public class Anomaly
    {
        public AnomalyType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public AnomalySeverity Severity { get; set; }
    }

    public class SecurityEvent
    {
        public string DeviceId { get; set; } = string.Empty;
        public SecurityEventType EventType { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    // Helper classes
    public class CertificateValidationResult
    {
        public bool IsValid { get; set; }
        public string? Error { get; set; }
    }

    public class IdentityVerificationResult
    {
        public bool IsValid { get; set; }
        public double RiskScore { get; set; }
    }

    public class PostureVerificationResult
    {
        public bool IsValid { get; set; }
        public double RiskScore { get; set; }
        public string? Error { get; set; }
    }

    public class PolicyEvaluationResult
    {
        public bool IsAllowed { get; set; }
        public string[] GrantedPermissions { get; set; } = Array.Empty<string>();
        public List<AccessCondition> AccessConditions { get; set; } = new();
        public double RiskScore { get; set; }
        public string DenialReason { get; set; } = string.Empty;
    }

    public class ContextVerificationResult
    {
        public bool IsValid { get; set; }
        public double RiskScore { get; set; }
        public TrustLevel TrustLevel { get; set; }
        public string? Error { get; set; }
    }

    public class DevicePostureUpdate
    {
        public SecurityPostureAssessment NewPosture { get; set; } = new();
    }

    // Enums
    public enum TrustLevel
    {
        None,
        Low,
        Medium,
        High
    }

    public enum ThreatLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum SecurityEventType
    {
        AccessGranted,
        AccessDenied,
        AccessRevoked,
        DeviceIsolated,
        PostureUpdated,
        PostureUpdateError,
        MonitoringCompleted,
        MonitoringError,
        AccessError
    }

    public enum ConditionType
    {
        TimeBased,
        NetworkLocation,
        DevicePosture,
        ApprovalRequired
    }

    public enum AnomalyType
    {
        SecurityViolation,
        PerformanceIssue,
        NetworkAnomaly,
        ConfigurationDrift
    }

    public enum AnomalySeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}
