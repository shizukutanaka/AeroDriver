using System.Windows;

namespace AeroDriver.UI.Helpers
{
    /// <summary>
    /// <see cref="System.Windows.Controls.DataGridColumn"/> のヘッダーなど、
    /// ビジュアルツリーに属さないため <c>DataContext</c> を継承しない要素から
    /// ViewModel のプロパティへバインドするための中継。
    /// <para>
    /// <see cref="Freezable"/> は要素の <c>Resources</c> に置かれると DataContext を
    /// 継承するという性質を利用する。WPF で確立された定番手法で、
    /// <c>x:Reference</c> のような循環参照の問題を起こさない。
    /// </para>
    /// 詳細ペインのように内側で <c>DataContext</c> を差し替えている箇所からも、
    /// これを経由すれば ViewModel のローカライズ済みラベルに到達できる。
    /// </summary>
    public sealed class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
