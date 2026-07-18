using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AeroDriver.UI.Services
{
    /// <summary>
    /// <see cref="IThemeService"/> の WPF 実装。テーマ用 ResourceDictionary を
    /// Application.Resources.MergedDictionaries の末尾で入れ替える。
    /// 既存のテーマ辞書(Themes/ 配下)だけを対象に差し替え、他のマージ辞書
    /// (コンバーター等)には触れない。
    /// </summary>
    public sealed class ThemeService : IThemeService
    {
        private static readonly Uri LightUri =
            new("pack://application:,,,/AeroDriver.UI;component/Themes/Light.xaml", UriKind.Absolute);
        private static readonly Uri DarkUri =
            new("pack://application:,,,/AeroDriver.UI;component/Themes/Dark.xaml", UriKind.Absolute);

        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public IReadOnlyList<AppTheme> AvailableThemes { get; } =
            new[] { AppTheme.Light, AppTheme.Dark };

        public void Apply(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var newUri = theme == AppTheme.Dark ? DarkUri : LightUri;
            var dictionaries = app.Resources.MergedDictionaries;

            // 既存のテーマ辞書(Themes/Light.xaml か Themes/Dark.xaml)を除去
            var existing = dictionaries
                .Where(d => d.Source != null &&
                            (d.Source == LightUri || d.Source == DarkUri))
                .ToList();
            foreach (var d in existing)
                dictionaries.Remove(d);

            dictionaries.Add(new ResourceDictionary { Source = newUri });
            CurrentTheme = theme;
        }
    }
}
