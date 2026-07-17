namespace AeroDriver.UI.Services
{
    /// <summary>
    /// ファイル選択ダイアログの抽象。ViewModel を WPF (Microsoft.Win32) 依存から切り離し、
    /// テスト時に差し替え可能にするために導入する。
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>
        /// ドライバーファイル(.inf/.exe/.msi/.cab)を1つ選択させる。
        /// キャンセルされた場合は null を返す。
        /// </summary>
        string? PickDriverFile();
    }
}
