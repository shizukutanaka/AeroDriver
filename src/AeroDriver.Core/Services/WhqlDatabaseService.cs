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
        public async Task<List<DriverInfo>> CheckForUpdatesAsync()
        {
            // Simulate driver update check
            await Task.Delay(100); // Simulate network delay

            var mockUpdates = new List<DriverInfo>
            {
                new DriverInfo
                {
                    DeviceID = "PCI\\VEN_10DE&DEV_1234",
                    DeviceName = "NVIDIA Graphics Device",
                    DriverVersion = "31.0.15.1234",
                    DriverProviderName = "NVIDIA Corporation",
                    DeviceClass = "Display",
                    Status = "Update Available",
                    IsWHQLCertified = true
                },
                new DriverInfo
                {
                    DeviceID = "PCI\\VEN_8086&DEV_5678",
                    DeviceName = "Intel Network Adapter",
                    DriverVersion = "12.18.9.45",
                    DriverProviderName = "Intel Corporation",
                    DeviceClass = "Network",
                    Status = "Update Available",
                    IsWHQLCertified = true
                }
            };

            // Return mock updates 30% of the time
            if (new Random().Next(100) < 30)
            {
                return mockUpdates;
            }

            return new List<DriverInfo>();
        }

        public async Task<DriverInfo> FindAvailableUpdateAsync(DriverInfo currentDriver)
        {
            if (currentDriver == null)
                return null;

            await Task.Delay(50); // Simulate search delay

            // Simulate finding an update for some drivers
            if (new Random().Next(100) < 25) // 25% chance of having an update
            {
                return new DriverInfo
                {
                    DeviceID = currentDriver.DeviceID,
                    DeviceName = currentDriver.DeviceName,
                    DriverVersion = IncrementVersion(currentDriver.DriverVersion),
                    DriverProviderName = currentDriver.DriverProviderName,
                    DeviceClass = currentDriver.DeviceClass,
                    Status = "Update Available",
                    IsWHQLCertified = true
                };
            }

            return null;
        }

        private string IncrementVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || version == "Unknown")
                return "1.0.0.1";

            try
            {
                var parts = version.Split('.').Select(int.Parse).ToArray();
                if (parts.Length >= 4)
                {
                    parts[3]++; // Increment build number
                    return string.Join(".", parts);
                }
                else if (parts.Length >= 3)
                {
                    parts[2]++; // Increment patch version
                    return string.Join(".", parts);
                }
                else
                {
                    return version + ".1";
                }
            }
            catch
            {
                return version + "_updated";
            }
        }
    }
}