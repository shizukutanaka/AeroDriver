namespace AeroDriver.Core.Models
{
    public class HealthReport
    {
        public DateTime GeneratedAt { get; set; }
        public bool IsAdministrator { get; set; }
        public SystemInfo? SystemInfo { get; set; }
        public int TotalDrivers { get; set; }
        public int WorkingDrivers { get; set; }
        public int ProblematicDrivers { get; set; }
        public int UnknownStatusDrivers { get; set; }
        public int AvailableUpdates { get; set; }
        public Dictionary<string, int> DriverClasses { get; set; } = new();
        public Dictionary<string, int> TopManufacturers { get; set; } = new();
        public int HealthScore { get; set; }
        public string[] Recommendations { get; set; } = Array.Empty<string>();
        public int GenerationTimeMs { get; set; }
    }
}