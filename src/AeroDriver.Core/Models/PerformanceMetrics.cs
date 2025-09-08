namespace AeroDriver.Core.Models
{
    public class PerformanceMetrics
    {
        public long WorkingSet { get; set; }
        public long PrivateMemorySize { get; set; }
        public TimeSpan ProcessorTime { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public DateTime MeasuredAt { get; set; }
    }
}