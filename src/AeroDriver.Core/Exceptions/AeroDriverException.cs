using System;

namespace AeroDriver.Core.Exceptions
{
    public class AeroDriverException : Exception
    {
        public string ErrorCode { get; }
        public string? DeviceId { get; }

        public AeroDriverException(string message) : base(message)
        {
            ErrorCode = "GENERAL_ERROR";
        }

        public AeroDriverException(string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = "GENERAL_ERROR";
        }

        public AeroDriverException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public AeroDriverException(string errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public AeroDriverException(string errorCode, string message, string? deviceId) : base(message)
        {
            ErrorCode = errorCode;
            DeviceId = deviceId;
        }

        public AeroDriverException(string errorCode, string message, string? deviceId, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
            DeviceId = deviceId;
        }
    }

    public class DriverNotFoundException : AeroDriverException
    {
        public DriverNotFoundException(string deviceId) 
            : base("DRIVER_NOT_FOUND", $"Driver not found: {deviceId}", deviceId)
        {
        }

        public DriverNotFoundException(string deviceId, Exception innerException) 
            : base("DRIVER_NOT_FOUND", $"Driver not found: {deviceId}", deviceId, innerException)
        {
        }
    }

    public class BackupException : AeroDriverException
    {
        public BackupException(string message) 
            : base("BACKUP_ERROR", message)
        {
        }

        public BackupException(string message, Exception innerException) 
            : base("BACKUP_ERROR", message, innerException)
        {
        }

        public BackupException(string message, string deviceId) 
            : base("BACKUP_ERROR", message, deviceId)
        {
        }

        public BackupException(string message, string deviceId, Exception innerException) 
            : base("BACKUP_ERROR", message, deviceId, innerException)
        {
        }
    }

    public class UpdateException : AeroDriverException
    {
        public UpdateException(string message) 
            : base("UPDATE_ERROR", message)
        {
        }

        public UpdateException(string message, Exception innerException) 
            : base("UPDATE_ERROR", message, innerException)
        {
        }

        public UpdateException(string message, string deviceId) 
            : base("UPDATE_ERROR", message, deviceId)
        {
        }

        public UpdateException(string message, string deviceId, Exception innerException) 
            : base("UPDATE_ERROR", message, deviceId, innerException)
        {
        }
    }

    public class InsufficientPrivilegesException : AeroDriverException
    {
        public InsufficientPrivilegesException() 
            : base("INSUFFICIENT_PRIVILEGES", "Administrator privileges required for driver operations")
        {
        }

        public InsufficientPrivilegesException(string operation) 
            : base("INSUFFICIENT_PRIVILEGES", $"Administrator privileges required for {operation}")
        {
        }
    }

    public class WmiException : AeroDriverException
    {
        public WmiException(string message) 
            : base("WMI_ERROR", message)
        {
        }

        public WmiException(string message, Exception innerException) 
            : base("WMI_ERROR", message, innerException)
        {
        }
    }
}