using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RasterVirtual.Models;
using RasterVirtual.ViewModels;

namespace RasterVirtual.Views;

public partial class SnapshotWindow : Window
{
    private readonly VmItemViewModel _item;
    private readonly MainViewModel _main;

    private sealed record SnapshotRow(string Tag, string Detail, string SizeText);

    public SnapshotWindow(VmItemViewModel item, MainViewModel main)
    {
        InitializeComponent();

        _item = item;
        _main = main;

        SourceInitialized += (_, _) => ApplyDarkTitleBar();

        HeaderTitle.Text = item.Name + " · 快照";
        HeaderSubtitle.Text = item.Machine.PrimaryDisk is null
            ? "这台虚拟机没有虚拟硬盘，无法使用快照"
            : "快照保存在主硬盘 " + item.Machine.PrimaryDisk.FileName + " 内部";

        UpdateModeUi();
        _ = LoadAsync();
    }

    private void UpdateModeUi()
    {
        var running = _item.IsRunning;

        StateChipText.Text = _item.StateText;
        StateChipText.Foreground = _item.StateBrush;
        StateChip.BorderBrush = _item.StateBrush;

        ModeHint.Text = running
            ? "虚拟机正在运行：创建的快照会连同内存状态一起保存，恢复后可以从当前时刻继续，就像什么都没发生过。"
            : "虚拟机已关闭：只能创建磁盘快照（不含内存）。恢复后相当于把硬盘回滚到那一刻，需要重新开机。";

        var hasDisk = _item.Machine.PrimaryDisk is not null;
        CreateButton.IsEnabled = hasDisk;
        RestoreButton.IsEnabled = hasDisk;
        DeleteButton.IsEnabled = hasDisk;

        if (!hasDisk)
            StatusText.Text = "缺少虚拟硬盘，快照功能不可用。";
    }

    private string? PrimaryDiskPath
    {
        get
        {
            var disk = _item.Machine.PrimaryDisk;
            if (disk is null) return null;

            var path = disk.ResolvePath(_item.Machine.Directory);
            return File.Exists(path) ? path : null;
        }
    }

    private async Task LoadAsync()
    {
        var path = PrimaryDiskPath;
        if (path is null)
        {
            SnapshotList.ItemsSource = Array.Empty<SnapshotRow>();
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        StatusText.Text = "正在读取快照列表……";

        var snapshots = await _main.Disks.ListSnapshotsAsync(path);

        var rows = snapshots
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SnapshotRow(
                s.Tag,
                s.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                s.SizeText))
            .ToList();

        SnapshotList.ItemsSource = rows;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = rows.Count == 0 ? "暂无快照" : $"共 {rows.Count} 个快照";

        if (rows.Count > 0) SnapshotList.SelectedIndex = 0;
    }

    private string? SelectedTag => (SnapshotList.SelectedItem as SnapshotRow)?.Tag;

    // ================= 操作 =================

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        var suggested = "snap-" + DateTime.Now.ToString("MMdd-HHmm");
        var tag = TextPromptDialog.Show(this, "创建快照",
            "给这个快照起个名字（只能包含字母、数字、连字符和下划线）：", suggested);

        if (tag is null) return;

        tag = SanitizeTag(tag);
        if (tag.Length == 0)
        {
            MessageBox.Show("名称不能为空。", "Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsEnabled = false;
        StatusText.Text = "正在创建快照，请稍候……";

        (bool ok, string message) result;

        if (_item.IsRunning)
        {
            result = await _item.SaveSnapshotAsync(tag);
        }
        else
        {
            var path = PrimaryDiskPath;
            result = path is null
                ? (false, "找不到主硬盘文件。")
                : await _main.Disks.CreateSnapshotOfflineAsync(path, tag);
        }

        IsEnabled = true;

        if (!result.ok)
        {
            StatusText.Text = "创建失败";
            MessageBox.Show("创建快照失败：\n" + result.message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _main.AppendLog($"[{_item.Name}] 已创建快照「{tag}」");
        await LoadAsync();
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        var tag = SelectedTag;
        if (tag is null)
        {
            MessageBox.Show("请先选择一个快照。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var warning = _item.IsRunning
            ? $"将把虚拟机恢复到快照「{tag}」的状态。\n\n当前运行中的所有未保存改动都会丢失，是否继续？"
            : $"将把主硬盘回滚到快照「{tag}」。\n\n该快照之后写入的数据都会丢失，是否继续？";

        if (MessageBox.Show(warning, "恢复快照", MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        IsEnabled = false;
        StatusText.Text = "正在恢复……";

        (bool ok, string message) result;

        if (_item.IsRunning)
        {
            result = await _item.RestoreSnapshotAsync(tag);
        }
        else
        {
            var path = PrimaryDiskPath;
            result = path is null
                ? (false, "找不到主硬盘文件。")
                : await _main.Disks.RestoreSnapshotOfflineAsync(path, tag);
        }

        IsEnabled = true;

        if (!result.ok)
        {
            StatusText.Text = "恢复失败";
            MessageBox.Show("恢复快照失败：\n" + result.message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText.Text = result.message;
        _main.AppendLog($"[{_item.Name}] 已恢复到快照「{tag}」");
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var tag = SelectedTag;
        if (tag is null) return;

        if (MessageBox.Show($"确定要删除快照「{tag}」吗？此操作不可撤销。", "删除快照",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        IsEnabled = false;
        StatusText.Text = "正在删除……";

        (bool ok, string message) result;

        if (_item.IsRunning)
        {
            result = await _item.DeleteSnapshotAsync(tag);
        }
        else
        {
            var path = PrimaryDiskPath;
            result = path is null
                ? (false, "找不到主硬盘文件。")
                : await _main.Disks.DeleteSnapshotOfflineAsync(path, tag);
        }

        IsEnabled = true;

        if (!result.ok)
        {
            StatusText.Text = "删除失败";
            MessageBox.Show("删除快照失败：\n" + result.message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _main.AppendLog($"[{_item.Name}] 已删除快照「{tag}」");
        await LoadAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        UpdateModeUi();
        await LoadAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static string SanitizeTag(string input)
    {
        var chars = input.Trim()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray();
        return new string(chars);
    }

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
