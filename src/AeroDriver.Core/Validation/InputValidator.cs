using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace AeroDriver.Core.Validation;

/// <summary>
/// Input validation is performed to ensure only properly formed data is entering the workflow in an information system, 
/// preventing malformed data from persisting in the database and triggering malfunction of various downstream components. 
/// Input validation should happen as early as possible in the data flow, preferably as soon as the data is received from the external party.
/// Data from all potentially untrusted sources should be subject to input validation, including not only Internet-facing web clients 
/// but also backend feeds over extranets, from suppliers, partners, vendors or regulators, each of which may be compromised on their own 
/// and start sending malformed data.
/// Input Validation should not be used as the primary method of preventing XSS, SQL Injection and other attacks which are covered in respective 
/// cheat sheets but can significantly contribute to reducing their impact if implemented properly.
/// </summary>
public static class InputValidator
{
    private static readonly int MaxInputLength = 10000;
    private static readonly int MaxPathLength = 500;
    private static readonly int MaxIdLength = 256;

    // Enhanced security patterns with caching for better performance
    private static readonly Regex PathTraversalPattern = new(@"\.\.[\\/]|^[\\/]|[\\/]\.\.[\\/]|[\\/]\.\.$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SqlInjectionPattern = new(@"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|SCRIPT|JAVASCRIPT|ONERROR|ONCLICK)\b|;|--|\b(XP_|SP_)\w+|\/\*|\*\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex XssPattern = new(@"<\s*script|javascript:|onerror\s*=|onclick\s*=|onload\s*=|<\s*iframe|eval\s*\(|expression\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommandInjectionPattern = new(@"[;&|`$(){}[\]<>]|\b(cmd|powershell|bash|sh|exec|system|passthru|shell_exec)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LdapInjectionPattern = new(@"(\(|\)|\*|\||&|!|=|<|>|\+|-|\^|~|:|,|;)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HeaderInjectionPattern = new(@"(\r|\n|:)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DeviceIdPattern = new(@"^[A-Z0-9\\&_\-\.]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SafeFilenamePattern = new(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // OWASP-compliant Unicode category patterns for allowlist validation
    private static readonly Regex UnicodeLetterPattern = new(@"^[A-Za-zÀ-ÿĀ-ſƀ-ɏḀ-ỿⱠ-Ɀ꜠-ꟿꬰ-꭯]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UnicodeDigitPattern = new(@"^[0-9٠-٩۰-۹߀-߉०-९০-৯੦-੯૦-૯୦-୯௦-௯౦-౯೦-೯൦-൯෦-෯๐-๙໐-໙༠-༩၀-၉႐-႙០-៩᠐-᠙᥆-᥏᧐-᧙᪀-᪉᪐-᪙᭐-᭙᮰-᮹᱀-᱉᱐-᱙꘠-꘩꣐-꣙꤀-꤉꧐-꧙꧰-꧹꩐-꩙꯰-꯹０-９]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UnicodeNormalizationPattern = new(@"[\u00AD\u034F\u1806\u180B\u180C\u180D\u200B\u200C\u200D\u2060\uFEFF]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Enhanced Unicode-safe patterns with OWASP compliance
    private static readonly Regex UnicodeControlCharsPattern = new(@"[\p{Cc}\p{Cf}\p{Co}\p{Cs}\p{Cn}]", RegexOptions.Compiled);

    // Performance-optimized pattern cache
    private static readonly ConcurrentDictionary<string, Regex> _compiledPatternsCache = new();
    private static readonly int _maxPatternCacheSize = 100;

    /// <summary>
    /// Validates device ID with comprehensive security checks
    /// </summary>
    public static ValidationResult ValidateDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return ValidationResult.Failure("Device ID cannot be empty");
        }

        if (deviceId.Length > MaxIdLength)
        {
            return ValidationResult.Failure($"Device ID exceeds maximum length of {MaxIdLength} characters");
        }

        // Check for null bytes
        if (deviceId.Contains('\0'))
        {
            return ValidationResult.Failure("Device ID contains null bytes");
        }

        // Check for control characters
        if (deviceId.Any(c => char.IsControl(c)))
        {
            return ValidationResult.Failure("Device ID contains control characters");
        }

        // Validate against device ID pattern
        if (!DeviceIdPattern.IsMatch(deviceId))
        {
            return ValidationResult.Failure("Device ID contains invalid characters");
        }

        // Check for path traversal attempts
        if (PathTraversalPattern.IsMatch(deviceId))
        {
            return ValidationResult.Failure("Device ID contains path traversal patterns", ValidationSeverity.Critical);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates file path with security restrictions
    /// </summary>
    public static ValidationResult ValidateFilePath(string? filePath, params string[] allowedDirectories)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ValidationResult.Failure("File path cannot be empty");
        }

        if (filePath.Length > MaxPathLength)
        {
            return ValidationResult.Failure($"File path exceeds maximum length of {MaxPathLength} characters");
        }

        // Check for null bytes
        if (filePath.Contains('\0'))
        {
            return ValidationResult.Failure("File path contains null bytes", ValidationSeverity.Critical);
        }

        // Check for path traversal
        if (PathTraversalPattern.IsMatch(filePath))
        {
            return ValidationResult.Failure("File path contains path traversal patterns", ValidationSeverity.Critical);
        }

        // Normalize and validate path
        try
        {
            var fullPath = System.IO.Path.GetFullPath(filePath);

            // If allowed directories specified, ensure path is within them
            if (allowedDirectories.Length > 0)
            {
                var isAllowed = allowedDirectories.Any(dir =>
                    fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));

                if (!isAllowed)
                {
                    return ValidationResult.Failure("File path is outside allowed directories", ValidationSeverity.Critical);
                }
            }
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure($"Invalid file path: {ex.Message}");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates filename with security restrictions
    /// </summary>
    public static ValidationResult ValidateFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return ValidationResult.Failure("Filename cannot be empty");
        }

        if (filename.Length > 255)
        {
            return ValidationResult.Failure("Filename exceeds maximum length of 255 characters");
        }

        // Check for path separators
        if (filename.Contains('/') || filename.Contains('\\'))
        {
            return ValidationResult.Failure("Filename cannot contain path separators");
        }

        // Check for null bytes
        if (filename.Contains('\0'))
        {
            return ValidationResult.Failure("Filename contains null bytes", ValidationSeverity.Critical);
        }

        // Validate against safe filename pattern
        if (!SafeFilenamePattern.IsMatch(filename))
        {
            return ValidationResult.Failure("Filename contains invalid characters");
        }

    /// <summary>
    /// OWASP-compliant Unicode text validation using allowlist approach
    /// Validates input against Unicode character categories for international text support
    /// </summary>
    public static ValidationResult ValidateUnicodeText(string? input, UnicodeCategory allowedCategories = UnicodeCategory.All, string fieldName = "Input")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ValidationResult.Failure($"{fieldName} cannot be empty");
        }

        // Normalize Unicode first
        string normalizedInput;
        try
        {
            normalizedInput = input.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return ValidationResult.Failure("Input contains invalid Unicode sequences", ValidationSeverity.Critical);
        }

        // Check for null bytes (critical security issue)
        if (normalizedInput.Contains('\0'))
        {
            return ValidationResult.Failure("Input contains null bytes", ValidationSeverity.Critical);
        }

        // Validate against allowed Unicode categories
        foreach (char c in normalizedInput)
        {
            var category = char.GetUnicodeCategory(c);
            if (!IsCategoryAllowed(category, allowedCategories))
            {
                return ValidationResult.Failure($"{fieldName} contains character '{c}' (Unicode category: {category}) which is not allowed", ValidationSeverity.High);
            }
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// OWASP-compliant semantic validation for structured data
    /// Validates business logic constraints beyond syntax
    /// </summary>
    public static ValidationResult ValidateSemanticInput<T>(T value, Func<T, bool> semanticRule, string fieldName = "Value", string ruleDescription = "semantic constraint")
    {
        if (value == null)
        {
            return ValidationResult.Failure($"{fieldName} cannot be null");
        }

        if (!semanticRule(value))
        {
            return ValidationResult.Failure($"{fieldName} violates {ruleDescription}", ValidationSeverity.High);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Determines if a Unicode category is allowed based on OWASP allowlist approach
    /// </summary>
    private static bool IsCategoryAllowed(UnicodeCategory category, UnicodeCategory allowedCategories)
    {
        if (allowedCategories == UnicodeCategory.All)
            return true;

        // Define safe Unicode categories for international text support
        switch (category)
        {
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
                return (allowedCategories & UnicodeCategory.Letter) != 0;

            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
                return (allowedCategories & UnicodeCategory.Number) != 0;

            case UnicodeCategory.SpaceSeparator:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
                return (allowedCategories & UnicodeCategory.Separator) != 0;

            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.EnclosingMark:
                return (allowedCategories & UnicodeCategory.Mark) != 0;

            case UnicodeCategory.ConnectorPunctuation:
            case UnicodeCategory.DashPunctuation:
            case UnicodeCategory.OpenPunctuation:
            case UnicodeCategory.ClosePunctuation:
            case UnicodeCategory.InitialQuotePunctuation:
            case UnicodeCategory.FinalQuotePunctuation:
            case UnicodeCategory.OtherPunctuation:
                return (allowedCategories & UnicodeCategory.Punctuation) != 0;

            case UnicodeCategory.MathSymbol:
            case UnicodeCategory.CurrencySymbol:
            case UnicodeCategory.ModifierSymbol:
            case UnicodeCategory.OtherSymbol:
                return (allowedCategories & UnicodeCategory.Symbol) != 0;

            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.Surrogate:
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.OtherNotAssigned:
            default:
                // Control characters and other potentially dangerous categories are generally not allowed
                return false;
        }
    }

    [Flags]
    private enum UnicodeCategory
    {
        None = 0,
        All = Letter | Number | Separator | Mark | Punctuation | Symbol,
        Letter = 1,
        Number = 2,
        Separator = 4,
        Mark = 8,
        Punctuation = 16,
        Symbol = 32
    }

    /// <summary>
    /// Validates general text input with comprehensive security checks and Unicode support
    /// </summary>
    public static ValidationResult ValidateTextInput(string? input, int maxLength = -1, bool allowUnicode = true)
    {
        if (input == null)
        {
            return ValidationResult.Failure("Input cannot be null");
        }

        var length = maxLength > 0 ? maxLength : MaxInputLength;
        if (input.Length > length)
        {
            return ValidationResult.Failure($"Input exceeds maximum length of {length} characters", ValidationSeverity.High);
        }

        // Normalize Unicode for consistent processing
        string normalizedInput;
        try
        {
            normalizedInput = allowUnicode ? input.Normalize(NormalizationForm.FormC) : input;
        }
        catch (ArgumentException)
        {
            return ValidationResult.Failure("Input contains invalid Unicode sequences", ValidationSeverity.Critical);
        }

        // Check for null bytes (critical security issue)
        if (normalizedInput.Contains('\0'))
        {
            return ValidationResult.Failure("Input contains null bytes", ValidationSeverity.Critical);
        }

        // Enhanced control character detection (excluding common whitespace)
        if (UnicodeControlCharsPattern.IsMatch(normalizedInput))
        {
            return ValidationResult.Failure("Input contains control characters", ValidationSeverity.Critical);
        }

        // Check for SQL injection patterns
        if (SqlInjectionPattern.IsMatch(normalizedInput))
        {
            return ValidationResult.Failure("Input contains potential SQL injection patterns", ValidationSeverity.Critical);
        }

        // Check for XSS patterns
        if (XssPattern.IsMatch(normalizedInput))
        {
            return ValidationResult.Failure("Input contains potential XSS patterns", ValidationSeverity.Critical);
        }

        // Check for command injection patterns
        if (CommandInjectionPattern.IsMatch(normalizedInput))
        {
            return ValidationResult.Failure("Input contains potential command injection patterns", ValidationSeverity.Critical);
        }

        // Check for header injection patterns
        if (HeaderInjectionPattern.IsMatch(normalizedInput))
        {
            return ValidationResult.Failure("Input contains potential header injection patterns", ValidationSeverity.High);
        }

    /// <summary>
    /// Validates input against a whitelist pattern with caching for performance
    /// </summary>
    public static ValidationResult ValidateWithWhitelist(string? input, string pattern, string fieldName = "Input")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ValidationResult.Failure($"{fieldName} cannot be empty");
        }

        // Use cached compiled regex for better performance
        var regex = _compiledPatternsCache.GetOrAdd(pattern, p =>
            new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        if (!regex.IsMatch(input))
        {
            return ValidationResult.Failure($"{fieldName} contains invalid characters or format", ValidationSeverity.High);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates input against a set of allowed values (strict whitelist)
    /// </summary>
    public static ValidationResult ValidateWithAllowedValues(string? input, IEnumerable<string> allowedValues, string fieldName = "Input")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ValidationResult.Failure($"{fieldName} cannot be empty");
        }

        if (!allowedValues.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Failure($"{fieldName} must be one of: {string.Join(", ", allowedValues)}", ValidationSeverity.High);
        }

        return ValidationResult.Success();
    }
    public static ValidationResult ValidateNumeric(int value, int min, int max, string fieldName = "Value")
    {
        if (value < min || value > max)
        {
            return ValidationResult.Failure($"{fieldName} must be between {min} and {max}");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates time span is within acceptable range
    /// </summary>
    public static ValidationResult ValidateTimeSpan(TimeSpan value, TimeSpan min, TimeSpan max, string fieldName = "Time span")
    {
        if (value < min || value > max)
        {
            return ValidationResult.Failure($"{fieldName} must be between {min} and {max}");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Enhanced sanitization for safe logging with comprehensive sensitive data detection
    /// </summary>
    public static string SanitizeForLogging(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Truncate long inputs for performance and readability
        if (input.Length > 500)
        {
            input = input.Substring(0, 500) + "...[truncated]";
        }

        string sanitized = input;

        // Remove comprehensive sensitive data patterns
        sanitized = Regex.Replace(sanitized, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", "****-****-****-****"); // Credit cards
        sanitized = Regex.Replace(sanitized, @"\b\d{3}-\d{2}-\d{4}\b", "***-**-****"); // SSN
        sanitized = Regex.Replace(sanitized, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "***@***.***"); // Email

        // Enhanced API key and token detection
        sanitized = Regex.Replace(sanitized, @"(api[_-]?key|access[_-]?token|refresh[_-]?token|session[_-]?id|auth[_-]?token|bearer)\s*[:=]\s*['""\[]?([A-Za-z0-9_\-+/=]{10,})['""\]]?", "$1=***", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"['""\[]?([A-Za-z0-9_\-+/=]{20,})['""\]]?", match => IsLikelySensitiveData(match.Value) ? "***" : match.Value);

        // Enhanced connection string detection
        sanitized = Regex.Replace(sanitized, @"(connection[_-]?string|conn[_-]?str|database[_-]?url|db[_-]?url|server|host)\s*[:=]\s*['""\[]?([^'""\]]+)['""\]]?", "$1=***", RegexOptions.IgnoreCase);

        // Enhanced file path sanitization
        sanitized = Regex.Replace(sanitized, @"([A-Za-z]:\\|\\\\|/)(users|home|documents|desktop|downloads|programdata|windows|system32|syswow64|temp|tmp|appdata)[\\\/][^'""\s]*", "$1$2\\***", RegexOptions.IgnoreCase);

        // IP address and port sanitization
        sanitized = Regex.Replace(sanitized, @"(\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b)(?::\d+)?", match =>
        {
            var parts = match.Value.Split(':');
            return parts.Length > 1 ? "***:***" : "***";
        });

        // Database credentials
        sanitized = Regex.Replace(sanitized, @"(user[_-]?id|username|login|uid|db[_-]?user|database[_-]?user)\s*[:=]\s*['""\[]?([^'""\]]+)['""\]]?", "$1=***", RegexOptions.IgnoreCase);

        // Enhanced encryption and certificate data detection
        sanitized = Regex.Replace(sanitized, @"[A-Fa-f0-9]{40,}", (match) => match.Length > 64 ? "***[encrypted]***" : match.Value);
        sanitized = Regex.Replace(sanitized, @"(thumbprint|certificate|cert|private[_-]?key|public[_-]?key|rsa|aes|des)\s*[:=]\s*['""\[]?([A-Fa-f0-9\s]{20,})['""\]]?", "$1=***", RegexOptions.IgnoreCase);

        // JWT tokens (3 parts separated by dots)
        sanitized = Regex.Replace(sanitized, @"[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", "***[jwt-token]***");

        // Password patterns (enhanced detection)
        sanitized = Regex.Replace(sanitized, @"(password|pwd|passwd|secret|token|key)\s*[:=]\s*\S+", "$1=***", RegexOptions.IgnoreCase);

        return sanitized;
    }

    /// <summary>
    /// Determines if a string value appears to be sensitive data
    /// </summary>
    private static bool IsLikelySensitiveData(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Long base64-like strings
        if (value.Length > 20 && Regex.IsMatch(value, @"^[A-Za-z0-9+/=]+$"))
            return true;

        // Long hex strings
        if (Regex.IsMatch(value, @"^[A-Fa-f0-9]{32,}$") && value.Length > 50)
            return true;

        // Contains sensitive keywords
        var sensitiveKeywords = new[] { "password", "secret", "token", "key", "credential", "auth" };
        if (sensitiveKeywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return true;

        // UUID/GUID patterns
        if (Regex.IsMatch(value, @"^[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12}$"))
            return true;

    // Rate limiting for validation operations
    private static readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits = new();
    private static readonly int _maxValidationRequestsPerMinute = 1000;
    private static readonly TimeSpan _rateLimitWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Rate limit information for validation operations
    /// </summary>
    private class RateLimitInfo
    {
        public int RequestCount { get; set; }
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if validation requests are within rate limits
    /// </summary>
    private static ValidationResult CheckRateLimit(string clientId = "default")
    {
        var now = DateTime.UtcNow;
        var rateLimit = _rateLimits.GetOrAdd(clientId, _ => new RateLimitInfo());

        // Reset window if expired
        if (now - rateLimit.WindowStart > _rateLimitWindow)
        {
            rateLimit.RequestCount = 0;
            rateLimit.WindowStart = now;
        }

        rateLimit.RequestCount++;

        if (rateLimit.RequestCount > _maxValidationRequestsPerMinute)
        {
            return ValidationResult.Failure($"Rate limit exceeded for client {clientId}. Maximum {_maxValidationRequestsPerMinute} requests per minute.", ValidationSeverity.High);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates input with comprehensive security checks including rate limiting
    /// </summary>
    public static ValidationResult ValidateInputSecure(string? input, string fieldName = "Input", string clientId = "default", int maxLength = -1, bool allowUnicode = true)
    {
        // Check rate limits first
        var rateLimitResult = CheckRateLimit(clientId);
        if (!rateLimitResult.IsValid)
        {
            return rateLimitResult;
        }

        // Perform standard validation
        return ValidateTextInput(input, maxLength, allowUnicode);
    }
    public static string SanitizeForStructuredLogging(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sanitized = SanitizeForLogging(input);

        // For structured logging, also handle JSON-like structures
        try
        {
            // If input looks like JSON, parse and sanitize values
            if (input.TrimStart().StartsWith("{") && input.TrimEnd().EndsWith("}"))
            {
                return SanitizeJsonForLogging(input);
            }
        }
        catch
        {
            // If JSON parsing fails, return the basic sanitized version
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes JSON strings for safe logging
    /// </summary>
    private static string SanitizeJsonForLogging(string jsonInput)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonInput);
            var sanitizedObj = SanitizeJsonElement(doc.RootElement);
            return System.Text.Json.JsonSerializer.Serialize(sanitizedObj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch
        {
            return SanitizeForLogging(jsonInput);
        }
    }

    /// <summary>
    /// Recursively sanitizes JSON elements
    /// </summary>
    private static object SanitizeJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => SanitizeJsonObject(element),
            System.Text.Json.JsonValueKind.Array => SanitizeJsonArray(element),
            System.Text.Json.JsonValueKind.String => SanitizeForLogging(element.GetString()),
            _ => element
        };
    }

    /// <summary>
    /// Sanitizes JSON object properties
    /// </summary>
    private static Dictionary<string, object> SanitizeJsonObject(System.Text.Json.JsonElement element)
    {
        var result = new Dictionary<string, object>();

        foreach (var property in element.EnumerateObject())
        {
            var key = property.Name.ToLowerInvariant();
            var value = SanitizeJsonElement(property.Value);

            // Sanitize sensitive property values based on key names
            if (IsSensitiveKey(key))
            {
                result[property.Name] = "***";
            }
            else if (value is string stringValue && IsSensitiveValue(stringValue))
            {
                result[property.Name] = "***";
            }
            else
            {
                result[property.Name] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Sanitizes JSON array elements
    /// </summary>
    private static List<object> SanitizeJsonArray(System.Text.Json.JsonElement element)
    {
        var result = new List<object>();

        foreach (var item in element.EnumerateArray())
        {
            result.Add(SanitizeJsonElement(item));
        }

        return result;
    }

    /// <summary>
    /// Checks if a key name indicates sensitive data
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        var sensitiveKeys = new[]
        {
            "password", "pwd", "passwd", "secret", "token", "key", "apikey", "accesskey",
            "credential", "credentials", "auth", "authorization", "bearer", "sessionid",
            "connectionstring", "connstr", "database", "dburl", "server", "host",
            "userid", "username", "login", "uid", "thumbprint", "certificate", "cert",
            "privatekey", "publickey", "encrypted", "encrypteddata"
        };

        return sensitiveKeys.Contains(key.ToLowerInvariant());
    }

    /// <summary>
    /// Checks if a value appears to be sensitive data
    /// </summary>
    private static bool IsSensitiveValue(string value)
    {
        // Check for patterns that might indicate sensitive data
        if (value.Length > 100 && Regex.IsMatch(value, @"^[A-Za-z0-9+/=]+$")) // Long base64-like strings
        {
            return true;
        }

        if (value.Contains("=") && (value.Contains("password") || value.Contains("secret") || value.Contains("token")))
        {
            return true;
        }

        if (Regex.IsMatch(value, @"[A-Fa-f0-9]{32,}") && value.Length > 50) // Long hex strings
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates batch operation size to prevent resource exhaustion
    /// </summary>
    public static ValidationResult ValidateBatchSize(int count, int maxBatchSize = 100)
    {
        if (count <= 0)
        {
            return ValidationResult.Failure("Batch size must be positive");
        }

        if (count > maxBatchSize)
            return ValidationResult.Failure($"Batch size {count} exceeds maximum allowed size of {maxBatchSize}");
        }

        return ValidationResult.Success();
    }

    #region Supporting Types

    public enum SanitizationLevel
    {
        Minimal,
        Standard,
        Strict,
        Custom
        }

    }
    public static SanitizationResult SanitizeInput(string? input, SanitizationLevel level = SanitizationLevel.Standard)
    {
        if (string.IsNullOrEmpty(input))
            return new SanitizationResult { SanitizedInput = input, ChangesMade = false };

        var result = new SanitizationResult
        {
            OriginalInput = input,
            SanitizedInput = input,
            ChangesMade = false
        };

        switch (level)
        {
            case SanitizationLevel.Minimal:
                // 基本的なサニタイズのみ
                result.SanitizedInput = RemoveNullBytes(result.SanitizedInput);
                break;

            case SanitizationLevel.Standard:
                // XSSと基本的な攻撃パターンの除去
                result.SanitizedInput = RemoveNullBytes(result.SanitizedInput);
                result.SanitizedInput = RemoveXssPatterns(result.SanitizedInput, ref result.ChangesMade);
                result.SanitizedInput = RemoveControlCharacters(result.SanitizedInput, ref result.ChangesMade);
                break;

            case SanitizationLevel.Strict:
                // 厳格なサニタイズ（英数字と基本記号のみ許可）
                result.SanitizedInput = RemoveNullBytes(result.SanitizedInput);
                result.SanitizedInput = RemoveXssPatterns(result.SanitizedInput, ref result.ChangesMade);
                result.SanitizedInput = RemoveControlCharacters(result.SanitizedInput, ref result.ChangesMade);
                result.SanitizedInput = RemoveNonAlphanumeric(result.SanitizedInput, ref result.ChangesMade);
                break;

            case SanitizationLevel.Custom:
                // カスタムルール適用
                result = ApplyCustomSanitizationRules(input);
                break;
        }

        return result;
    }

    #region Enhanced Input Validation

    private static readonly ConcurrentDictionary<string, ValidationRule> _customRules = new();
    private static readonly ConcurrentQueue<ValidationRecord> _validationHistory = new();
    private static readonly ConcurrentDictionary<ValidationFailureType, ValidationMetrics> _validationMetrics = new();
    private static int _maxValidationHistorySize = 1000;

    /// <summary>
    /// カスタムバリデーションルールを追加
    /// </summary>
    public static void AddCustomValidationRule(string ruleName, Func<string, ValidationResult> rule)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            throw new ArgumentNullException(nameof(ruleName));

        _customRules[ruleName] = new ValidationRule
        {
            Name = ruleName,
            Validator = rule,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// カスタムバリデーションルールを実行
    /// </summary>
    public static ValidationResult ValidateWithCustomRule(string ruleName, string input)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            throw new ArgumentNullException(nameof(ruleName));

        if (_customRules.TryGetValue(ruleName, out var rule))
        {
            var result = rule.Validator(input);
            RecordValidation(result, ruleName, input);
            return result;
        }

        return ValidationResult.Failure($"Custom validation rule '{ruleName}' not found");
    }

    /// <summary>
    /// バリデーション統計を取得
    /// </summary>
    public static ValidationStatisticsReport GetValidationStatistics()
    {
        var report = new ValidationStatisticsReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalValidations = _validationHistory.Count,
            MetricsByFailureType = _validationMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        // 最近のバリデーションエラーを集計
        var recentValidations = _validationHistory.Where(v =>
            (DateTime.UtcNow - v.Timestamp) < TimeSpan.FromHours(1)).ToList();

        report.RecentValidationCount = recentValidations.Count;
        report.RecentFailureCount = recentValidations.Count(v => !v.Result.IsValid);
        report.RecentFailureRate = recentValidations.Count > 0
            ? (double)report.RecentFailureCount / recentValidations.Count
            : 0;

        return report;
    }

    /// <summary>
    /// JSON入力のバリデーション（深さ制限含む）
    /// </summary>
    public static ValidationResult ValidateJsonInput(string? jsonInput, int maxDepth = 100)
    {
        if (string.IsNullOrWhiteSpace(jsonInput))
            return ValidationResult.Failure("JSON input cannot be empty");

        if (jsonInput.Length > MaxInputLength)
            return ValidationResult.Failure($"JSON input exceeds maximum length of {MaxInputLength} characters");

        try
        {
            // JSONの深さをチェック
            var depth = 0;
            foreach (var c in jsonInput)
            {
                if (c == '{' || c == '[')
                {
                    depth++;
                    if (depth > maxDepth)
                        return ValidationResult.Failure($"JSON depth exceeds maximum allowed depth of {maxDepth}");
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                }
            }

            if (depth != 0)
                return ValidationResult.Failure("Invalid JSON structure - unmatched brackets");

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure($"JSON validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// XML入力のバリデーション（XXE対策含む）
    /// </summary>
    public static ValidationResult ValidateXmlInput(string? xmlInput)
    {
        if (string.IsNullOrWhiteSpace(xmlInput))
            return ValidationResult.Failure("XML input cannot be empty");

        if (xmlInput.Length > MaxInputLength)
            return ValidationResult.Failure($"XML input exceeds maximum length of {MaxInputLength} characters");

        // XXE攻撃パターンのチェック
        var xxePatterns = new[]
        {
            @"<!ENTITY", @"<!DOCTYPE", @"SYSTEM\s+", @"PUBLIC\s+", @"<!\[CDATA\["
        };

        foreach (var pattern in xxePatterns)
        {
            if (Regex.IsMatch(xmlInput, pattern, RegexOptions.IgnoreCase))
            {
                return ValidationResult.Failure("XML contains potentially dangerous constructs", ValidationSeverity.Critical);
            }
        }

        // 基本的なXML構造チェック（簡易）
        var openTags = Regex.Matches(xmlInput, @"<[^/?][^>]*>");
        var closeTags = Regex.Matches(xmlInput, @"</[^>]+>");

        if (openTags.Count != closeTags.Count)
        {
            return ValidationResult.Failure("XML structure appears malformed - tag count mismatch");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates JSON Web Token (JWT) format and structure
    /// </summary>
    public static ValidationResult ValidateJwtToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ValidationResult.Failure("JWT token cannot be empty");
        }

        if (token.Length > 5000) // Reasonable JWT limit
        {
            return ValidationResult.Failure("JWT token exceeds maximum length");
        }

        // Check for null bytes
        if (token.Contains('\0'))
        {
            return ValidationResult.Failure("JWT token contains null bytes", ValidationSeverity.Critical);
        }

        // JWT should have 3 parts separated by dots
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return ValidationResult.Failure("Invalid JWT format - must have 3 parts");
        }

        // Each part should be non-empty
        if (parts.Any(string.IsNullOrEmpty))
        {
            return ValidationResult.Failure("Invalid JWT format - empty parts");
        }

        // Check for invalid Base64 characters
        foreach (var part in parts)
        {
            if (!IsValidBase64String(part))
            {
                return ValidationResult.Failure("Invalid JWT format - invalid Base64 encoding");
            }
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates Base64 encoded string format
    /// </summary>
    public static ValidationResult ValidateBase64String(string? base64String)
    {
        if (string.IsNullOrWhiteSpace(base64String))
        {
            return ValidationResult.Failure("Base64 string cannot be empty");
        }

        if (!IsValidBase64String(base64String))
        {
            return ValidationResult.Failure("Invalid Base64 format");
        }

        return ValidationResult.Success();
    }

    private static string RemoveNullBytes(string input)
    {
        return input.Replace("\0", "");
    }

    private static string RemoveXssPatterns(string input, ref bool changesMade)
    {
        var patterns = new[]
        {
            @"<\s*script[^>]*>.*?</script>", @"javascript:", @"on\w+\s*=",
            @"<\s*iframe[^>]*>", @"<\s*object[^>]*>", @"<\s*embed[^>]*>",
            @"eval\s*\(", @"expression\s*\("
        };

        var result = input;
        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (regex.IsMatch(result))
            {
                changesMade = true;
                result = regex.Replace(result, "[REMOVED]");
            }
        }

        return result;
    }

    private static string RemoveControlCharacters(string input, ref bool changesMade)
    {
        var hasControlChars = input.Any(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r');
        if (hasControlChars)
        {
            changesMade = true;
            return new string(input.Where(c => !char.IsControl(c) || c == '\t' || c == '\n' || c == '\r').ToArray());
        }
        return input;
    }

    private static string RemoveNonAlphanumeric(string input, ref bool changesMade)
    {
        var result = Regex.Replace(input, @"[^a-zA-Z0-9\s\.\-_]", "");
        if (result != input)
        {
            changesMade = true;
        }
        return result;
    }

    private static SanitizationResult ApplyCustomSanitizationRules(string input)
    {
        // カスタムサニタイズルールの適用（将来の実装用）
        return new SanitizationResult
        {
            OriginalInput = input,
            SanitizedInput = input,
            ChangesMade = false
        };
    }

    private static void RecordValidation(ValidationResult result, string ruleName, string input)
    {
        var record = new ValidationRecord
        {
            Timestamp = DateTime.UtcNow,
            RuleName = ruleName,
            Input = input.Length > 100 ? input.Substring(0, 100) + "..." : input,
            Result = result,
            FailureType = result.IsValid ? ValidationFailureType.None :
                        result.Severity >= ValidationSeverity.Critical ? ValidationFailureType.Critical :
                        result.Severity >= ValidationSeverity.High ? ValidationFailureType.High :
                        ValidationFailureType.Standard
        };

        _validationHistory.Enqueue(record);

        // 履歴サイズを制限
        while (_validationHistory.Count > _maxValidationHistorySize)
        {
            _validationHistory.TryDequeue(out _);
        }

        // メトリクス更新
        if (!result.IsValid)
        {
            var metrics = _validationMetrics.GetOrAdd(record.FailureType, _ => new ValidationMetrics
            {
                FailureType = record.FailureType
            });

            metrics.Count++;
            metrics.LastOccurrence = record.Timestamp;
        }
    }

    private static void CleanupValidationHistory()
    {
        while (_validationHistory.Count > _maxValidationHistorySize)
        {
            _validationHistory.TryDequeue(out _);
        }
    }

    #endregion

    #region Core Validation Types

    public enum ValidationSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class ValidationResult
    {
        public bool IsValid { get; }
        public string Message { get; }
        public ValidationSeverity Severity { get; }
        public List<string> Errors { get; }

        private ValidationResult(bool isValid, string message, ValidationSeverity severity, List<string>? errors = null)
        {
            IsValid = isValid;
            Message = message;
            Severity = severity;
            Errors = errors ?? new List<string>();
        }

        public static ValidationResult Success() => new(true, "Validation passed", ValidationSeverity.Low);

        public static ValidationResult Failure(string message, ValidationSeverity severity = ValidationSeverity.Medium)
            => new(false, message, severity);

        public static ValidationResult Failure(string message, List<string> errors, ValidationSeverity severity = ValidationSeverity.Medium)
            => new(false, message, severity, errors);

        public void ThrowIfInvalid()
        {
            if (!IsValid)
            {
                throw new ValidationException(Message, Severity, Errors);
            }
        }
    }

    public class ValidationException : Exception
    {
        public ValidationSeverity Severity { get; }
        public List<string> ValidationErrors { get; }

        public ValidationException(string message, ValidationSeverity severity, List<string> errors)
            : base(message)
        {
            Severity = severity;
            ValidationErrors = errors;
        }
    }

    #endregion
