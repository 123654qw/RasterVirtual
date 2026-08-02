using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RasterVirtual.Views;

/// <summary>
/// 一个轻量的单行输入对话框，用纯代码搭建，自动继承应用的深色样式。
/// </summary>
public sealed class TextPromptDialog : Window
{
    private readonly TextBox _input;

    private TextPromptDialog(string title, string message, string defaultValue)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContentOptions();
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        if (Application.Current?.Resources["BrushBackdrop"] is Brush backdrop)
            Background = backdrop;

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };

        var label = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13
        };
        if (Application.Current?.Resources["BrushText"] is Brush text)
            label.Foreground = text;

        _input = new TextBox { Text = defaultValue };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancel = new Button { Content = "取消", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;

        var ok = new Button
        {
            Content = "确定",
            MinWidth = 84,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };
        if (Application.Current?.Resources["PrimaryButton"] is Style primary)
            ok.Style = primary;
        ok.Click += (_, _) => DialogResult = true;

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        root.Children.Add(label);
        root.Children.Add(_input);
        root.Children.Add(buttons);

        Content = root;

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private static SizeToContent SizeToContentOptions() => SizeToContent.Height;

    /// <summary>弹出输入框；用户取消时返回 null。</summary>
    public static string? Show(Window owner, string title, string message, string defaultValue = "")
    {
        var dialog = new TextPromptDialog(title, message, defaultValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._input.Text.Trim() : null;
    }
}
