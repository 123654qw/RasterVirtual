using System.IO;
using System.Windows;
using System.Windows.Controls;
using RasterVirtual.Infrastructure;
using RasterVirtual.Models;
using RasterVirtual.Services;
using RasterVirtual.ViewModels;

namespace RasterVirtual.Views;

public partial class DiskCreateDialog : Window
{
    private readonly VirtualMachine _vm;
    private readonly MainViewModel _main;

    public VmDisk? CreatedDisk { get; private set; }

    public DiskCreateDialog(VirtualMachine machine, MainViewModel main)
    {
        InitializeComponent();

        _vm = machine;
        _main = main;

        TargetHint.Text = "将创建在：" + machine.Directory;

        var formats = EnumText.OptionsFor<DiskFormat>();
        FormatCombo.ItemsSource = formats;
        FormatCombo.SelectedItem = formats.First(f => f.Value == DiskFormat.Qcow2);

        var buses = EnumText.OptionsFor<DiskBus>();
        BusCombo.ItemsSource = buses;
        BusCombo.SelectedItem = buses.First(b => b.Value == DiskBus.Sata);

        NameBox.Text = SuggestName();

        SizeSlider.ValueChanged += (_, _) => SizeValue.Text = $"{(int)SizeSlider.Value} GB";
        FormatCombo.SelectionChanged += (_, _) => UpdateSpaceHint();

        UpdateSpaceHint();
    }

    private string SuggestName()
    {
        var index = _vm.Disks.Count + 1;
        while (true)
        {
            var candidate = $"disk-{index}";
            var exists = _vm.Disks.Any(d =>
                Path.GetFileNameWithoutExtension(d.FileName)
                    .Equals(candidate, StringComparison.OrdinalIgnoreCase));

            if (!exists) return candidate;
            index++;
        }
    }

    private void UpdateSpaceHint()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_vm.Directory));
            if (string.IsNullOrEmpty(root)) return;

            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;

            SizeSlider.Maximum = Math.Max(4, Math.Min(2048, (int)freeGb + 64));

            var format = ReadFormat();
            SpaceHint.Text = format == DiskFormat.Raw
                ? $"raw 会立即占满全部容量。{root} 剩余 {freeGb:0.#} GB。"
                : $"qcow2 等动态格式只在写入时增长，实际占用远小于标称容量。{root} 剩余 {freeGb:0.#} GB。";
        }
        catch
        {
            SpaceHint.Text = "动态格式只在写入时增长，实际占用远小于标称容量。";
        }
    }

    private DiskFormat ReadFormat() =>
        FormatCombo.SelectedItem is EnumOption<DiskFormat> o ? o.Value : DiskFormat.Qcow2;

    private DiskBus ReadBus() =>
        BusCombo.SelectedItem is EnumOption<DiskBus> o ? o.Value : DiskBus.Sata;

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        var baseName = NameBox.Text.Trim();
        if (baseName.Length == 0)
        {
            MessageBox.Show("请填写文件名。", "Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var c in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(c, '_');

        var format = ReadFormat();
        var fileName = baseName + VmDisk.ExtensionFor(format);
        var fullPath = Path.Combine(_vm.Directory, fileName);

        if (File.Exists(fullPath))
        {
            MessageBox.Show("同名文件已存在，请换一个名字。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sizeGb = (int)SizeSlider.Value;

        IsEnabled = false;
        StatusText.Text = "正在创建磁盘……";

        var (ok, message) = await _main.Disks.CreateDiskAsync(
            fullPath, format, sizeGb, PreallocCheck.IsChecked == true);

        IsEnabled = true;

        if (!ok)
        {
            StatusText.Text = "创建失败";
            MessageBox.Show("创建磁盘失败：\n" + message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        CreatedDisk = new VmDisk
        {
            Path = fileName,
            Format = format,
            Bus = ReadBus(),
            CapacityGb = sizeGb,
            Ssd = SsdCheck.IsChecked == true,
            Discard = format == DiskFormat.Qcow2
        };

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
