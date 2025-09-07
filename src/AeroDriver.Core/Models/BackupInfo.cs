using System;

namespace AeroDriver.Core.Models
{
    public class BackupInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string BackupPath { get; set; }
        public DateTime BackupDate { get; set; }
        public string DriverVersion { get; set; }
        public long BackupSize { get; set; }
        public string Description { get; set; }
    }
}