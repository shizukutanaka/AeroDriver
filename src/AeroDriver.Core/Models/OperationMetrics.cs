namespace AeroDriver.Core.Models
{
    public class OperationMetrics
    {
        private readonly object _lock = new();
        private long _totalMs;
        private long _minMs = long.MaxValue;
        private long _maxMs;
        private int _successCount;
        private int _failureCount;
        
        public string OperationName { get; }
        public int TotalCount => SuccessCount + FailureCount;
        public int SuccessCount => _successCount;
        public int FailureCount => _failureCount;
        public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;
        public double AverageMs => TotalCount > 0 ? (double)_totalMs / TotalCount : 0;
        public long MinMs => _minMs == long.MaxValue ? 0 : _minMs;
        public long MaxMs => _maxMs;
        
        public OperationMetrics(string operationName)
        {
            OperationName = operationName;
        }
        
        public void Record(long elapsedMs, bool success)
        {
            lock (_lock)
            {
                _totalMs += elapsedMs;
                _minMs = Math.Min(_minMs, elapsedMs);
                _maxMs = Math.Max(_maxMs, elapsedMs);
                
                if (success)
                    _successCount++;
                else
                    _failureCount++;
            }
        }
    }
}