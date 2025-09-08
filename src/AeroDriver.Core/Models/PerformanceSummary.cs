namespace AeroDriver.Core.Models
{
    public class PerformanceSummary
    {
        public int TotalOperations { get; set; }
        public int TotalSuccessful { get; set; }
        public int TotalFailed { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public string SlowestOperation { get; set; } = "";
        public string MostFrequentOperation { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
    }
}