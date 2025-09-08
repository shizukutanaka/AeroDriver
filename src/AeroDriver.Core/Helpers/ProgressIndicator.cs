namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// コンソールプログレスインディケーター
    /// </summary>
    public class ProgressIndicator : IDisposable
    {
        private readonly string _description;
        private readonly int _totalSteps;
        private readonly bool _showPercentage;
        private readonly bool _showSpinner;
        private readonly bool _enabled;
        private int _currentStep;
        private readonly Timer? _spinnerTimer;
        private int _spinnerPosition;
        private readonly string[] _spinnerChars = { "|", "/", "-", "\\" };
        private bool _disposed;

        public ProgressIndicator(string description, int totalSteps = 100, bool showPercentage = true, bool showSpinner = false, bool enabled = true)
        {
            _description = description;
            _totalSteps = totalSteps;
            _showPercentage = showPercentage;
            _showSpinner = showSpinner && enabled;
            _enabled = enabled;

            if (_showSpinner && _enabled)
            {
                _spinnerTimer = new Timer(UpdateSpinner, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            }

            if (_enabled)
            {
                Console.Write($"{_description}... ");
                if (!_showSpinner)
                {
                    Console.Write("[");
                    Console.Write(new string(' ', 20));
                    Console.Write("]");
                    Console.SetCursorPosition(Console.CursorLeft - 21, Console.CursorTop);
                }
            }
        }

        /// <summary>
        /// プログレスを更新
        /// </summary>
        public void Update(int currentStep, string? statusMessage = null)
        {
            if (!_enabled || _disposed) return;

            _currentStep = Math.Min(currentStep, _totalSteps);

            if (_showSpinner)
            {
                var message = string.IsNullOrEmpty(statusMessage) ? _description : $"{_description}: {statusMessage}";
                Console.Write($"\r{message}... {_spinnerChars[_spinnerPosition]} ");
                
                if (_showPercentage)
                {
                    var percentage = (double)_currentStep / _totalSteps * 100;
                    Console.Write($"({percentage:F0}%)");
                }
            }
            else
            {
                var progressLength = (int)((double)_currentStep / _totalSteps * 20);
                var currentPos = Console.CursorLeft;
                var currentTop = Console.CursorTop;
                
                Console.SetCursorPosition(currentPos - 20, currentTop);
                Console.Write(new string('█', progressLength));
                Console.Write(new string(' ', 20 - progressLength));
                Console.SetCursorPosition(currentPos + 1, currentTop);
                
                if (_showPercentage)
                {
                    var percentage = (double)_currentStep / _totalSteps * 100;
                    Console.Write($" {percentage:F0}%");
                }

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    Console.Write($" - {statusMessage}");
                }
            }
        }

        /// <summary>
        /// プログレスを完了
        /// </summary>
        public void Complete(string? completionMessage = null)
        {
            if (!_enabled || _disposed) return;

            _spinnerTimer?.Dispose();

            if (_showSpinner)
            {
                var message = completionMessage ?? "完了";
                Console.WriteLine($"\r{_description}... {message}");
            }
            else
            {
                Console.SetCursorPosition(Console.CursorLeft - 20, Console.CursorTop);
                Console.Write(new string('█', 20));
                Console.WriteLine($"] 100% - {completionMessage ?? "完了"}");
            }
        }

        private void UpdateSpinner(object? state)
        {
            if (_disposed) return;
            _spinnerPosition = (_spinnerPosition + 1) % _spinnerChars.Length;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _spinnerTimer?.Dispose();
                if (_enabled && !_showSpinner)
                {
                    Console.WriteLine();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// プログレス表示の拡張メソッド
    /// </summary>
    public static class ProgressExtensions
    {
        /// <summary>
        /// 非同期タスクでプログレスを表示
        /// </summary>
        public static async Task<T> WithProgressAsync<T>(this Task<T> task, string description, bool enabled = true)
        {
            if (!enabled)
            {
                return await task;
            }

            using var progress = new ProgressIndicator(description, showSpinner: true, enabled: enabled);
            var result = await task;
            progress.Complete();
            return result;
        }

        /// <summary>
        /// 非同期タスクでプログレスを表示（戻り値なし）
        /// </summary>
        public static async Task WithProgressAsync(this Task task, string description, bool enabled = true)
        {
            if (!enabled)
            {
                await task;
                return;
            }

            using var progress = new ProgressIndicator(description, showSpinner: true, enabled: enabled);
            await task;
            progress.Complete();
        }
    }
}