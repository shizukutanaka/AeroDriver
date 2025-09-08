namespace AeroDriver.Core.Models
{
    public class CleanupResult
    {
        public string OperationType { get; set; } = "";
        public bool Success { get; set; }
        public int FilesDeleted { get; set; }
        public long BytesFreed { get; set; }
        public List<string> Errors { get; set; } = new();
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        public string GetFormattedSize()
        {
            if (BytesFreed < 1024)
                return $"{BytesFreed} B";
            if (BytesFreed < 1024 * 1024)
                return $"{BytesFreed / 1024.0:F1} KB";
            if (BytesFreed < 1024 * 1024 * 1024)
                return $"{BytesFreed / (1024.0 * 1024):F1} MB";
            return $"{BytesFreed / (1024.0 * 1024 * 1024):F1} GB";
        }
    }
}