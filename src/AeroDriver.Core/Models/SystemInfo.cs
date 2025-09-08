namespace AeroDriver.Core.Models
{
    public class SystemInfo
    {
        public string ComputerName { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Model { get; set; } = "";
        public string OperatingSystem { get; set; } = "";
        public string Version { get; set; } = "";
        public string Architecture { get; set; } = "";
        public string BuildNumber { get; set; } = "";
        public string TotalRAM { get; set; } = "";
        public string Processor { get; set; } = "";
        public string Cores { get; set; } = "";
        public string Threads { get; set; } = "";
    }
}