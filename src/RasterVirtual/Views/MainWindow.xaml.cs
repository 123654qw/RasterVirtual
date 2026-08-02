using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RasterVirtual.ViewModels;

namespace RasterVirtual.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        Width = _vm.Settings.WindowWidth;
        Height = _vm.Settings.WindowHeight;
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;

        _vm.LogLines.CollectionChanged += OnLogChanged;

        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Closing += OnClosing;
    }

    /// <summary>把系统标题栏也切换成深色，避免顶部出现一条白边。</summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var useDark = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 2004+）
            if (DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int)) != 0)
            {
                // 19 = 早期 Win10 版本使用的属性号
                DwmSetWindowAttribute(handle, 19, ref useDark, sizeof(int));
            }
        }
        catch
        {
            // 旧系统不支持时忽略
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            LogScroll?.ScrollToEnd();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        // 记住窗口尺寸
        try
        {
            _vm.Settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _vm.Settings.WindowWidth = Width;
                _vm.Settings.WindowHeight = Height;
            }
            _vm.Settings.Save();
        }
        catch
        {
            // 保存失败不阻止关闭
        }

        if (_forceClose || !_vm.HasRunningMachines) return;

        e.Cancel = true;

        var result = MessageBox.Show(
            "还有虚拟机正在运行。\n\n" +
            "选择「是」会强制断电并退出，客户机中未保存的数据会丢失。\n" +
            "选择「否」返回程序。",
            "Raster Virtual", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsEnabled = false;
        await _vm.ShutdownAllAsync();

        _forceClose = true;
        Close();
    }
}
