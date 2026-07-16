namespace AeroDriver.Core.Models
{
    // readonly record struct: class から変更
    // - IProgress<T>.Report() は高頻度呼び出し → class だとGCヒープに毎回allocate
    // - struct はスタック割り当て → GC圧力ゼロ
    // - readonly: 不変性を保証、防御的コピー不要
    // - record: with式 + 値等価比較 + 分解パターン
    public readonly record struct DriverScanProgress
    {
        /// <summary>処理済み件数</summary>
        public int Current { get; init; }

        /// <summary>全件数（ストリーミング中は 0 = 不定）</summary>
        public int Total { get; init; }

        /// <summary>現在処理中のデバイス名</summary>
        public string? CurrentDevice { get; init; }

        // struct のデフォルト値は常に null/0 → null合体演算子でフォールバック
        private readonly string? _phase;
        /// <summary>フェーズ名</summary>
        public string Phase
        {
            get => _phase ?? string.Empty;
            init => _phase = value;
        }

        /// <summary>0〜100 のパーセンテージ（Total が 0 の場合は -1 = 不定）</summary>
        public int Percentage => Total > 0 ? Current * 100 / Total : -1;
    }
}
