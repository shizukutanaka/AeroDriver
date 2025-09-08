namespace AeroDriver.Core.Models
{
    public class MetricEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public MetricType Type { get; set; }
        public double Value { get; set; }
        public DateTime LastUpdated { get; set; }
        public int Count { get; set; }
        public double Sum { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public Dictionary<string, object>? Tags { get; set; }
    }

    public enum MetricType
    {
        Counter,
        Gauge,
        Timer
    }

    public class MetricsSummary
    {
        public int TotalMetrics { get; set; }
        public int CounterMetrics { get; set; }
        public int GaugeMetrics { get; set; }
        public int TimerMetrics { get; set; }
        public Dictionary<string, int> Categories { get; set; } = new();
    }
}