using System.Collections.Generic;

namespace AeroDriver.UI.Services
{
    public enum AppTheme
    {
        Light,
        Dark,
    }

    /// <summary>
    /// 実行時のテーマ(ライト/ダーク)切替を担う。Application.Resources の
    /// マージ済みディクショナリを差し替えることで、DynamicResource を使う要素が
    /// 即座に再テーマされる。
    /// </summary>
    public interface IThemeService
    {
        AppTheme CurrentTheme { get; }
        IReadOnlyList<AppTheme> AvailableThemes { get; }
        void Apply(AppTheme theme);
    }
}
