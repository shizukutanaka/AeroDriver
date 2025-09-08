using System.Text;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.CLI.Services
{
    /// <summary>
    /// Enterprise-grade console output service with logging integration
    /// </summary>
    public class EnterpriseConsoleOutput : IConsoleOutput
    {
        private readonly ILogger<EnterpriseConsoleOutput>? _logger;
        private readonly bool _silent;
        private readonly bool _noColor;
        
        public bool IsSilent => _silent;
        public bool SupportsColor => !_noColor && !Console.IsOutputRedirected;

        public EnterpriseConsoleOutput(ILogger<EnterpriseConsoleOutput>? logger = null, 
            bool silent = false, bool noColor = false)
        {
            _logger = logger;
            _silent = silent;
            _noColor = noColor;
        }

        public void WriteInfo(string message)
        {
            if (_silent) return;
            
            _logger?.LogInformation("{Message}", message);
            
            if (SupportsColor)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(message);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(message);
            }
        }

        public void WriteWarning(string message)
        {
            _logger?.LogWarning("{Message}", message);
            
            if (SupportsColor)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ {message}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"WARNING: {message}");
            }
        }

        public void WriteError(string message)
        {
            _logger?.LogError("{Message}", message);
            
            if (SupportsColor)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ {message}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"ERROR: {message}");
            }
        }

        public void WriteSuccess(string message)
        {
            if (_silent) return;
            
            _logger?.LogInformation("Success: {Message}", message);
            
            if (SupportsColor)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ {message}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"SUCCESS: {message}");
            }
        }

        public void WriteHeader(string title)
        {
            if (_silent) return;
            
            _logger?.LogInformation("Section: {Title}", title);
            
            var separator = new string('=', Math.Min(title.Length, 50));
            
            if (SupportsColor)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(title);
                Console.WriteLine(separator);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(title);
                Console.WriteLine(separator);
            }
            Console.WriteLine();
        }

        public void WriteList(IEnumerable<string> items, string? prefix = null)
        {
            if (_silent) return;
            
            var itemList = items.ToList();
            _logger?.LogInformation("Writing list with {Count} items", itemList.Count);
            
            foreach (var item in itemList)
            {
                var displayText = prefix != null ? $"{prefix} {item}" : $"- {item}";
                Console.WriteLine(displayText);
            }
            
            if (itemList.Count > 0)
                Console.WriteLine();
        }

        public void WriteProgress(string operation, int current, int total)
        {
            if (_silent) return;
            
            var percentage = total > 0 ? (current * 100) / total : 0;
            var progressBar = CreateProgressBar(percentage, 20);
            
            Console.Write($"\r{operation}: {progressBar} {current}/{total} ({percentage}%)");
            
            if (current >= total)
            {
                Console.WriteLine();
                _logger?.LogInformation("Completed: {Operation} - {Current}/{Total}", operation, current, total);
            }
        }

        public void WriteTable<T>(IEnumerable<T> data, params string[] columns)
        {
            if (_silent) return;
            
            var items = data.ToList();
            if (!items.Any()) return;
            
            _logger?.LogInformation("Writing table with {Count} rows", items.Count);
            
            // Simple table formatting for enterprise use
            var properties = typeof(T).GetProperties();
            var relevantProps = columns.Length > 0 
                ? properties.Where(p => columns.Contains(p.Name)).ToArray()
                : properties;
            
            // Header
            var header = string.Join(" | ", relevantProps.Select(p => p.Name.PadRight(15)));
            Console.WriteLine(header);
            Console.WriteLine(new string('-', header.Length));
            
            // Data rows
            foreach (var item in items)
            {
                var row = string.Join(" | ", relevantProps.Select(p => 
                    (p.GetValue(item)?.ToString() ?? "").PadRight(15).Substring(0, Math.Min(15, (p.GetValue(item)?.ToString() ?? "").Length))));
                Console.WriteLine(row);
            }
            
            Console.WriteLine();
        }
        
        private static string CreateProgressBar(int percentage, int width)
        {
            var filled = (percentage * width) / 100;
            var sb = new StringBuilder();
            sb.Append('[');
            
            for (var i = 0; i < width; i++)
            {
                sb.Append(i < filled ? '█' : '░');
            }
            
            sb.Append(']');
            return sb.ToString();
        }
    }
}