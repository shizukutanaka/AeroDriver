using AeroDriver.Languages.Services;
using Microsoft.Win32;

namespace AeroDriver.UI.Services
{
    /// <summary>
    /// <see cref="IFileDialogService"/> の WPF 実装。<see cref="OpenFileDialog"/> を使用。
    /// タイトルとフィルターの表示名はローカライズする(以前は日本語直書きで、
    /// 非日本語環境ではダイアログだけ日本語のままだった)。
    /// </summary>
    public sealed class FileDialogService : IFileDialogService
    {
        // 対応形式は DriverService.InstallCustomDriverAsync の判定と対で維持すること
        private const string DriverPatterns = "*.inf;*.exe;*.msi;*.cab";

        private readonly ILanguageService _lang;

        public FileDialogService(ILanguageService lang)
        {
            _lang = lang ?? throw new System.ArgumentNullException(nameof(lang));
        }

        public string? PickDriverFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = _lang.GetString("FileDialog_Title"),
                // Filter の書式: 表示名|パターン|表示名|パターン…(表示名のみ翻訳する)
                Filter = $"{_lang.GetString("FileDialog_FilterDrivers")} ({DriverPatterns})|{DriverPatterns}|" +
                         $"{_lang.GetString("FileDialog_FilterAll")} (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
