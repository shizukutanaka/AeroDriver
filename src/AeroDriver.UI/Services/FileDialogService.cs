using Microsoft.Win32;

namespace AeroDriver.UI.Services
{
    /// <summary>
    /// <see cref="IFileDialogService"/> の WPF 実装。<see cref="OpenFileDialog"/> を使用。
    /// </summary>
    public sealed class FileDialogService : IFileDialogService
    {
        public string? PickDriverFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "ドライバーファイルを選択",
                Filter = "ドライバーファイル (*.inf;*.exe;*.msi;*.cab)|*.inf;*.exe;*.msi;*.cab|すべてのファイル (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
