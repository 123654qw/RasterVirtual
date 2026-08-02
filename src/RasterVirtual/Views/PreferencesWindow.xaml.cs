using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RasterVirtual.Models;
using RasterVirtual.Services;
using RasterVirtual.ViewModels;

namespace RasterVirtual.Views;

public partial class PreferencesWindow : Window
{
    private readonly MainViewModel _main;

    public PreferencesWindow(MainViewModel main)
    {
        InitializeComponent();

        _main = main;

        SourceInitialized += (_, _) => ApplyDarkTitleBar();

        QemuBox.Text = main.Settings.QemuDirectoryOverride ?? string.Empty;
        RootBox.Text = main.Settings.MachinesRoot;
        StopAllCheck.IsChecked = main.Settings.StopAllOnExit;
        ShowCmdCheck.IsChecked = main.Settings.ShowFullCommandLine;

        ConfigHint.Text = "配置文件：" + AppSettings.ConfigPath;

        AccelSummary.Text = main.Accel.Summary;
        AccelAdvice.Text = main.Accel.Advice ?? "当前主机可以使用 Windows Hypervisor Platform 进行硬件加速。";

        UpdateRuntimeStatus();
        UpdateRootHint();

        RootBox.TextChanged += (_, _) => UpdateRootHint();
    }

    // ================= 运行时 =================

    private void UpdateRuntimeStatus()
    {
        var available = _main.Locator.IsAvailable;

        RuntimeStatus.Text = available ? _main.QemuStatus : "未找到 QEMU 运行时";
        RuntimePath.Text = available
            ? _main.Locator.QemuDirectory ?? string.Empty
            : "请把 QEMU 便携版放到程序目录下的 runtime\\qemu，或在下方手动指定。";

        RuntimeDot.Fill = available
            ? new SolidColorBrush(Color.FromRgb(0x54, 0xB3, 0x7E))
            : new SolidColorBrush(Color.FromRgb(0xD7, 0x5F, 0x4B));
    }

    private void OnBrowseQemu(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择包含 qemu-system-x86_64.exe 的目录"
        };

        if (dialog.ShowDialog() != true) return;

        QemuBox.Text = dialog.FolderName;
        OnDetectQemu(sender, e);
    }

    private async void OnDetectQemu(object sender, RoutedEventArgs e)
    {
        var candidate = QemuBox.Text.Trim();

        var probe = new QemuLocator();
        var found = probe.Locate(string.IsNullOrWhiteSpace(candidate) ? null : candidate);

        if (!found)
        {
            StatusText.Text = "未能在该目录找到 QEMU";
            RuntimeDot.Fill = new SolidColorBrush(Color.FromRgb(0xD7, 0x5F, 0x4B));
            RuntimeStatus.Text = "未找到 QEMU 运行时";
            RuntimePath.Text = "目录中缺少 qemu-system-x86_64.exe。";
            return;
        }

        RuntimeDot.Fill = new SolidColorBrush(Color.FromRgb(0x54, 0xB3, 0x7E));
        RuntimePath.Text = probe.QemuDirectory ?? string.Empty;
        RuntimeStatus.Text = "正在读取版本……";

        var version = await probe.GetVersionAsync();
        RuntimeStatus.Text = version is null
            ? "运行时就绪"
            : version.Replace("QEMU emulator version", "QEMU").Trim();

        StatusText.Text = "检测成功";
    }

    // ================= 存放目录 =================

    private void UpdateRootHint()
    {
        var path = RootBox.Text.Trim();

        if (path.Length == 0)
        {
            RootHint.Text = "留空将使用默认位置：" + AppSettings.DefaultMachinesRoot;
            return;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                var drive = new DriveInfo(root);
                var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                RootHint.Text = Directory.Exists(path)
                    ? $"该目录所在磁盘剩余 {freeGb:0.#} GB。虚拟硬盘会写在这里，建议留足空间。"
                    : $"目录尚不存在，保存时会自动创建。所在磁盘剩余 {freeGb:0.#} GB。";
                return;
            }
        }
        catch
        {
            // 路径非法时落到下面的提示
        }

        RootHint.Text = "路径格式不正确。";
    }

    private void OnBrowseRoot(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择虚拟机默认存放目录"
        };

        if (dialog.ShowDialog() == true)
            RootBox.Text = dialog.FolderName;
    }

    private void OnOpenRoot(object sender, RoutedEventArgs e)
    {
        var path = RootBox.Text.Trim();
        if (path.Length == 0) path = AppSettings.DefaultMachinesRoot;

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开失败：" + ex.Message;
        }
    }

    // ================= 维护 =================

    private void OnOpenConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppSettings.ConfigDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开失败：" + ex.Message;
        }
    }

    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("要把所有全局设置恢复为默认值吗？已创建的虚拟机不受影响。",
                "恢复默认设置", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        QemuBox.Text = string.Empty;
        RootBox.Text = AppSettings.DefaultMachinesRoot;
        StopAllCheck.IsChecked = true;
        ShowCmdCheck.IsChecked = true;
        StatusText.Text = "已恢复默认值，点击保存生效。";
    }

    // ================= 保存 =================

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var root = RootBox.Text.Trim();
        if (root.Length == 0) root = AppSettings.DefaultMachinesRoot;

        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法创建存放目录：\n" + ex.Message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var qemu = QemuBox.Text.Trim();
        if (qemu.Length > 0 && !File.Exists(Path.Combine(qemu, QemuLocator.SystemBinary)))
        {
            var confirm = MessageBox.Show(
                $"在 {qemu} 中没有找到 {QemuLocator.SystemBinary}。\n\n仍要保存这个路径吗？",
                "Raster Virtual", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.OK) return;
        }

        _main.Settings.MachinesRoot = root;
        _main.Settings.QemuDirectoryOverride = qemu.Length == 0 ? null : qemu;
        _main.Settings.StopAllOnExit = StopAllCheck.IsChecked == true;
        _main.Settings.ShowFullCommandLine = ShowCmdCheck.IsChecked == true;

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var useDark = 1;
            if (DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref useDark, sizeof(int));
        }
        catch
        {
            // 旧系统忽略
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
