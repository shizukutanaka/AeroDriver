using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// Enterprise-grade console output interface for standardized user interaction
    /// </summary>
    public interface IConsoleOutput
    {
        /// <summary>
        /// Write informational message to user
        /// </summary>
        void WriteInfo(string message);
        
        /// <summary>
        /// Write warning message to user
        /// </summary>
        void WriteWarning(string message);
        
        /// <summary>
        /// Write error message to user
        /// </summary>
        void WriteError(string message);
        
        /// <summary>
        /// Write success message to user
        /// </summary>
        void WriteSuccess(string message);
        
        /// <summary>
        /// Write formatted table data
        /// </summary>
        void WriteTable<T>(IEnumerable<T> data, params string[] columns);
        
        /// <summary>
        /// Write progress indicator
        /// </summary>
        void WriteProgress(string operation, int current, int total);
        
        /// <summary>
        /// Write header section
        /// </summary>
        void WriteHeader(string title);
        
        /// <summary>
        /// Write formatted list
        /// </summary>
        void WriteList(IEnumerable<string> items, string? prefix = null);
        
        /// <summary>
        /// Check if output should be suppressed (silent mode)
        /// </summary>
        bool IsSilent { get; }
        
        /// <summary>
        /// Check if colored output is supported
        /// </summary>
        bool SupportsColor { get; }
    }
}