using System.Collections.Generic;
using System.Threading.Tasks;

namespace AeroDriver.Service;

public interface ITelemetryService
{
    void StartTelemetryCollection(int intervalMinutes);
    void RecordEvent(string name, Dictionary<string, object> properties);
    Task RecordEventAsync(string name, Dictionary<string, object> properties);
    void RecordPerformanceMetric(string name, double value);
    Task RecordPerformanceMetricAsync(string name, double value);
    Task ClearTelemetryDataAsync();
    void StopTelemetryCollection();
}
