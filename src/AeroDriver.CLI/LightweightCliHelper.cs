using System.Runtime.Versioning;
using System.Text;

namespace AeroDriver.CLI
{
    /// <summary>
    /// 軽量CLI共通ヘルパー - 重複コード削減と高速化
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class LightweightCliHelper
    {
        // 高速出力バッファ
        private static readonly StringBuilder _outputBuffer = new();

        /// <summary>
        /// 高速ヘッダー出力
        /// </summary>
        public static void WriteHeader(string title)
        {
            Console.WriteLine($"=== {title} ===");
            Console.WriteLine();
        }

        /// <summary>
        /// 高速セクション出力
        /// </summary>
        public static void WriteSection(string sectionName, Dictionary<string, object> data)
        {
            Console.WriteLine($"[{sectionName}]");
            foreach (var item in data)
            {
                Console.WriteLine($"  {item.Key}: {item.Value}");
            }
            Console.WriteLine();
        }

        /// 高速リスト出力
        /// </summary>
        public static void WriteList(string title, IEnumerable<string> items, int maxItems = 10)
        {
            Console.WriteLine($"[{title}]");
            var count = 0;
            foreach (var item in items)
            {
                if (count >= maxItems) break;
                Console.WriteLine($"  - {item}");
                count++;
            }
            if (!items.Any())
            {
                Console.WriteLine("  項目なし");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 進行状況表示（軽量版）
        /// </summary>
        public static async Task ShowProgressAsync(string message, int durationMs = 1000)
        {
            Console.Write($"{message}");
            var chars = new[] { '|', '/', '-', '\\' };
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < durationMs)
            {
                foreach (var c in chars)
                {
                    Console.Write($"\r{message} {c}");
                    await Task.Delay(100);
                    if ((DateTime.Now - startTime).TotalMilliseconds >= durationMs) break;
                }
            }
            Console.WriteLine($"\r{message} completed");
        }

        /// <summary>
        /// 高度な進行状況バー（％表示付き）
        /// </summary>
        public static void ShowProgressBar(string message, int current, int total, int width = 40)
        {
            var percentage = Math.Min(100, (int)((double)current / total * 100));
            var filled = (int)((double)current / total * width);
            var empty = width - filled;

            var bar = new string('#', filled) + new string('.', empty);
            var timeStamp = DateTime.Now.ToString("HH:mm:ss");

            Console.Write($"\r[{timeStamp}] {message}: [{bar}] {percentage}% ({current}/{total})");

            if (current >= total)
            {
                Console.WriteLine(" completed");
            }
        }

        /// <summary>
        /// タスク実行進行状況（時間経過表示付き）
        /// </summary>
        public static async Task<T> ShowTaskProgressAsync<T>(string message, Task<T> task, int updateIntervalMs = 500)
        {
            var startTime = DateTime.Now;
            var spinner = new[] { "-", "\\", "|", "/" };
            var spinnerIndex = 0;

            Console.CursorVisible = false;
            try
            {
                while (!task.IsCompleted)
                {
                    var elapsed = DateTime.Now - startTime;
                    var elapsedStr = elapsed.TotalSeconds < 60
                        ? $"{elapsed.TotalSeconds:F1}s"
                        : $"{elapsed.Minutes}:{elapsed.Seconds:D2}";

                    Console.Write($"\r{spinner[spinnerIndex]} {message} (実行中: {elapsedStr})");
                    spinnerIndex = (spinnerIndex + 1) % spinner.Length;

                    await Task.Delay(updateIntervalMs);
                }

                var totalTime = DateTime.Now - startTime;
                var totalTimeStr = totalTime.TotalSeconds < 60
                    ? $"{totalTime.TotalSeconds:F1}s"
                    : $"{totalTime.Minutes}:{totalTime.Seconds:D2}";

                Console.WriteLine($"\r{message} 完了 ({totalTimeStr})                    ");
                return await task;
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }

        /// <summary>
        /// 複数タスク進行状況表示
        /// </summary>
        public static async Task<T[]> ShowMultiTaskProgressAsync<T>(string[] taskNames, Task<T>[] tasks)
        {
            var startTime = DateTime.Now;
            var completed = new bool[tasks.Length];
            var results = new T[tasks.Length];

            Console.CursorVisible = false;
            try
            {
                while (!tasks.All(t => t.IsCompleted))
                {
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.WriteLine($"並列実行中... ({DateTime.Now - startTime:mm\\:ss})");

                    for (int i = 0; i < tasks.Length; i++)
                    {
                        var status = tasks[i].IsCompleted ? "DONE" :
                                   tasks[i].IsFaulted ? "FAILED" : "RUNNING";
                        Console.WriteLine($"  [{status}] {taskNames[i]}");

                        if (tasks[i].IsCompleted && !completed[i])
                        {
                            completed[i] = true;
                            if (!tasks[i].IsFaulted)
                                results[i] = await tasks[i];
                        }
                    }

                    // カーソルを先頭に戻す
                    Console.SetCursorPosition(0, Console.CursorTop - tasks.Length - 1);
                    await Task.Delay(200);
                }

                // 最終結果表示
                Console.SetCursorPosition(0, Console.CursorTop + tasks.Length + 1);
                var totalTime = DateTime.Now - startTime;
                Console.WriteLine($"すべてのタスク完了 ({totalTime.TotalMilliseconds:F0}ms)");

                return results;
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }

        /// <summary>
        /// 軽量ファイル処理進行状況
        /// </summary>
        public static void ShowFileProgress(string operation, string fileName, long processedBytes, long totalBytes)
        {
            var percentage = totalBytes > 0 ? (int)((double)processedBytes / totalBytes * 100) : 0;
            var processedMB = processedBytes / (1024.0 * 1024.0);
            var totalMB = totalBytes / (1024.0 * 1024.0);

            var barWidth = 30;
            var filled = (int)((double)processedBytes / totalBytes * barWidth);
            var bar = new string('#', filled) + new string('.', barWidth - filled);

            Console.Write($"\r{operation}: {fileName} [{bar}] {percentage}% ({processedMB:F1}/{totalMB:F1}MB)");

            if (processedBytes >= totalBytes)
            {
                Console.WriteLine(" completed");
            }
        }

        /// <summary>
        /// 成功メッセージ
        /// </summary>
        public static void WriteSuccess(string message)
        {
            Console.WriteLine($"SUCCESS: {message}");
        }

        /// <summary>
        /// 警告メッセージ
        /// </summary>
        public static void WriteWarning(string message)
        {
            Console.WriteLine($"WARNING: {message}");
        }

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public static void WriteError(string message)
        {
            Console.WriteLine($"ERROR: {message}");
        }

        /// <summary>
        /// 情報メッセージ
        /// </summary>
        public static void WriteInfo(string message)
        {
            Console.WriteLine($"INFO: {message}");
        }

        /// <summary>
        /// 統計情報表示
        /// </summary>
        public static void WriteStats(string title, Dictionary<string, int> stats)
        {
            Console.WriteLine($"【{title}】");
            foreach (var stat in stats)
            {
                Console.WriteLine($"  {stat.Key}: {stat.Value:N0}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 健全性スコア表示
        /// </summary>
        public static void WriteHealthScore(double score)
        {
            Console.WriteLine($"システム健全性: {score:F1}%");
        }

        /// <summary>
        /// タイムスタンプ付きメッセージ
        /// </summary>
        public static void WriteTimestamp(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        /// <summary>
        /// ファイル情報表示
        /// </summary>
        public static void WriteFileInfo(string filePath, long sizeBytes)
        {
            var sizeStr = sizeBytes switch
            {
                < 1024 => $"{sizeBytes}B",
                < 1024 * 1024 => $"{sizeBytes / 1024:F1}KB",
                _ => $"{sizeBytes / (1024 * 1024):F1}MB"
            };
            Console.WriteLine($"  ファイル: {Path.GetFileName(filePath)} ({sizeStr})");
            Console.WriteLine($"  パス: {filePath}");
        }

        /// <summary>
        /// 標準フッター
        /// </summary>
        public static void WriteFooter(string additionalInfo = "")
        {
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                Console.WriteLine($"※ {additionalInfo}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// ヘルプメニュー表示
        /// </summary>
        public static void WriteHelpMenu(string commandName, Dictionary<string, string> commands)
        {
            Console.WriteLine($"{commandName}コマンド (軽量版):");
            Console.WriteLine();
            Console.WriteLine("使用法:");
            foreach (var cmd in commands)
            {
                Console.WriteLine($"  {cmd.Key.PadRight(30)} - {cmd.Value}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 簡単なテーブル出力
        /// </summary>
        public static void WriteTable(string[] headers, string[][] rows)
        {
            if (headers.Length == 0 || rows.Length == 0) return;

            var columnWidths = new int[headers.Length];

            // 列幅計算
            for (int i = 0; i < headers.Length; i++)
            {
                columnWidths[i] = headers[i].Length;
                foreach (var row in rows)
                {
                    if (i < row.Length && row[i].Length > columnWidths[i])
                    {
                        columnWidths[i] = row[i].Length;
                    }
                }
            }

            // ヘッダー出力
            for (int i = 0; i < headers.Length; i++)
            {
                Console.Write(headers[i].PadRight(columnWidths[i] + 2));
            }
            Console.WriteLine();

            // 区切り線
            for (int i = 0; i < headers.Length; i++)
            {
                Console.Write(new string('-', columnWidths[i]).PadRight(columnWidths[i] + 2));
            }
            Console.WriteLine();

            // データ行
            foreach (var row in rows)
            {
                for (int i = 0; i < headers.Length && i < row.Length; i++)
                {
                    Console.Write(row[i].PadRight(columnWidths[i] + 2));
                }
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// バッファされた出力（高速化）
        /// </summary>
        public static void StartBufferedOutput()
        {
            _outputBuffer.Clear();
        }

        public static void BufferLine(string line)
        {
            _outputBuffer.AppendLine(line);
        }

        public static void FlushBufferedOutput()
        {
            Console.Write(_outputBuffer.ToString());
            _outputBuffer.Clear();
        }
    }
}