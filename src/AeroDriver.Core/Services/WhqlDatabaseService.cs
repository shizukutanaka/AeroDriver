using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public class WhqlDatabaseService : IWhqlDatabaseService
    {
        private readonly Dictionary<string, DriverUpdateInfo> _knownUpdates;

        public WhqlDatabaseService()
        {
            _knownUpdates = InitializeKnownUpdates();
        }

        public async Task<List<DriverInfo>> CheckForUpdatesAsync()
        {
            await Task.Delay(200); // Simulate network delay

            var availableUpdates = new List<DriverInfo>();

            // Simulate checking common drivers that often have updates
            foreach (var update in _knownUpdates.Values)
            {
                if (IsUpdateRelevant(update))
                {
                    availableUpdates.Add(new DriverInfo
                    {
                        DeviceID = update.DeviceId,
                        DeviceName = update.DeviceName,
                        DriverVersion = update.LatestVersion,
                        DriverProviderName = update.Manufacturer,
                        DeviceClass = update.DeviceClass,
                        Status = "Update Available",
                        IsWHQLCertified = true
                    });
                }
            }

            return availableUpdates;
        }

        public async Task<DriverInfo> FindAvailableUpdateAsync(DriverInfo currentDriver)
        {
            if (currentDriver == null)
                return null;

            await Task.Delay(100);

            // Extract vendor and device IDs from hardware ID
            var vendorId = ExtractVendorId(currentDriver.DeviceID);
            var deviceId = ExtractDeviceId(currentDriver.DeviceID);

            if (string.IsNullOrEmpty(vendorId) || string.IsNullOrEmpty(deviceId))
                return null;

            var key = $"{vendorId}&{deviceId}";
            if (_knownUpdates.TryGetValue(key, out var updateInfo))
            {
                if (IsNewerVersion(updateInfo.LatestVersion, currentDriver.DriverVersion))
                {
                    return new DriverInfo
                    {
                        DeviceID = currentDriver.DeviceID,
                        DeviceName = currentDriver.DeviceName,
                        DriverVersion = updateInfo.LatestVersion,
                        DriverProviderName = updateInfo.Manufacturer,
                        DeviceClass = currentDriver.DeviceClass,
                        Status = "Update Available",
                        IsWHQLCertified = true
                    };
                }
            }

            return null;
        }

        private Dictionary<string, DriverUpdateInfo> InitializeKnownUpdates()
        {
            return new Dictionary<string, DriverUpdateInfo>
            {
                ["VEN_10DE&DEV_1E87"] = new DriverUpdateInfo
                {
                    DeviceId = "PCI\\VEN_10DE&DEV_1E87",
                    DeviceName = "NVIDIA GeForce RTX 2080 Ti",
                    Manufacturer = "NVIDIA Corporation",
                    DeviceClass = "Display",
                    LatestVersion = "31.0.15.4601"
                },
                ["VEN_8086&DEV_15B8"] = new DriverUpdateInfo
                {
                    DeviceId = "PCI\\VEN_8086&DEV_15B8",
                    DeviceName = "Intel Ethernet Connection I219-V",
                    Manufacturer = "Intel Corporation",
                    DeviceClass = "Network",
                    LatestVersion = "12.19.2.45"
                },
                ["VEN_8086&DEV_1F41"] = new DriverUpdateInfo
                {
                    DeviceId = "PCI\\VEN_8086&DEV_1F41",
                    DeviceName = "Intel USB 3.0 eXtensible Host Controller",
                    Manufacturer = "Intel Corporation",
                    DeviceClass = "USB",
                    LatestVersion = "10.0.19041.844"
                },
                ["VEN_1022&DEV_1457"] = new DriverUpdateInfo
                {
                    DeviceId = "PCI\\VEN_1022&DEV_1457",
                    DeviceName = "AMD High Definition Audio Controller",
                    Manufacturer = "Advanced Micro Devices Inc.",
                    DeviceClass = "MEDIA",
                    LatestVersion = "10.0.1.17"
                }
            };
        }

        private bool IsUpdateRelevant(DriverUpdateInfo updateInfo)
        {
            // Simulate some logic to determine if an update is relevant
            // In a real implementation, this would check against actual system hardware
            return DateTime.Now.Millisecond % 3 == 0; // Random relevance check
        }

        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            if (string.IsNullOrEmpty(newVersion) || string.IsNullOrEmpty(currentVersion))
                return false;

            if (currentVersion == "Unknown")
                return true;

            try
            {
                var newParts = newVersion.Split('.').Select(int.Parse).ToArray();
                var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    var newPart = i < newParts.Length ? newParts[i] : 0;
                    var currentPart = i < currentParts.Length ? currentParts[i] : 0;

                    if (newPart > currentPart)
                        return true;
                    if (newPart < currentPart)
                        return false;
                }

                return false; // Versions are equal
            }
            catch
            {
                return false; // Error parsing versions
            }
        }

        private string ExtractVendorId(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
                return null;

            var venIndex = hardwareId.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
            if (venIndex == -1)
                return null;

            var venStart = venIndex + 4;
            var venEnd = hardwareId.IndexOf('&', venStart);
            if (venEnd == -1)
                venEnd = hardwareId.Length;

            return hardwareId.Substring(venIndex, venEnd - venIndex);
        }

        private string ExtractDeviceId(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
                return null;

            var devIndex = hardwareId.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
            if (devIndex == -1)
                return null;

            var devStart = devIndex;
            var devEnd = hardwareId.IndexOf('&', devStart + 4);
            if (devEnd == -1)
                devEnd = hardwareId.Length;

            return hardwareId.Substring(devStart, devEnd - devStart);
        }

        private class DriverUpdateInfo
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string Manufacturer { get; set; }
            public string DeviceClass { get; set; }
            public string LatestVersion { get; set; }
        }
    }
}