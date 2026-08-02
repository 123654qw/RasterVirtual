using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RasterVirtual.Infrastructure;
using RasterVirtual.Models;
using RasterVirtual.Services;
using RasterVirtual.ViewModels;

namespace RasterVirtual.Views;

public partial class VmSettingsWindow : Window
{
    private readonly VirtualMachine _vm;
    private readonly MainViewModel _main;

    private readonly List<VmDisk> _disks;
    private readonly List<PortForward> _forwards;

    private bool _loaded;

    public VmSettingsWindow(VirtualMachine machine, MainViewModel main)
    {
        InitializeComponent();

        _vm = machine;
        _main = main;
        _disks = machine.Disks.Select(CloneDisk).ToList();
        _forwards = machine.Network.PortForwards.Select(CloneForward).ToList();

        SourceInitialized += (_, _) => ApplyDarkTitleBar();

        HeaderTitle.Text = machine.Name;
        HeaderSubtitle.Text = machine.Directory;

        InitializeControls();
        LoadFromModel();
        _loaded = true;

        RefreshDiskList();
        RefreshForwardList();
        UpdateDynamicHints();
    }

    // ================= 初始化 =================

    private void InitializeControls()
    {
        FillEnum(FirmwareCombo, FirmwareType.Uefi);
        FillEnum(AccelCombo, AccelMode.Auto);
        FillEnum(PointerCombo, PointerDevice.UsbTablet);
        FillEnum(BootCombo, BootDevice.CdRom);
        FillEnum(VideoCombo, VideoModel.Std);
        FillEnum(BackendCombo, DisplayBackend.Sdl);
        FillEnum(NetModeCombo, NetworkMode.Nat);
        FillEnum(NicCombo, NicModel.E1000);
        FillEnum(AudioBackendCombo, AudioBackend.DirectSound);
        FillEnum(SoundCardCombo, SoundCard.IntelHda);

        ThreadsCombo.ItemsSource = new[] { "1 线程 / 核", "2 线程 / 核（超线程）" };
        ProtoCombo.ItemsSource = new[] { "tcp", "udp" };
        ProtoCombo.SelectedIndex = 0;

        CpuModelCombo.ItemsSource = new[]
        {
            "max", "host", "qemu64", "Nehalem", "Westmere",
            "SandyBridge", "Haswell", "Skylake-Client", "EPYC"
        };

        // 依主机资源限制上限
        var cores = Math.Max(1, Environment.ProcessorCount);
        CpuSlider.Maximum = Math.Max(2, cores);

        var totalMb = GetTotalPhysicalMemoryMb();
        var maxMem = Math.Max(2048, (int)(totalMb * 0.8 / 256) * 256);
        MemSlider.Maximum = maxMem;
        MemHint.Text = $"主机共有约 {totalMb / 1024.0:0.#} GB 内存，建议不要超过 {maxMem / 1024.0:0.#} GB。";

        CpuSlider.ValueChanged += (_, _) => CpuValue.Text = $"{(int)CpuSlider.Value} 核";
        MemSlider.ValueChanged += (_, _) => MemValue.Text = FormatMemory((int)MemSlider.Value);
        VramSlider.ValueChanged += (_, _) => VramValue.Text = $"{(int)VramSlider.Value} MB";

        FirmwareCombo.SelectionChanged += (_, _) => UpdateDynamicHints();
        BackendCombo.SelectionChanged += (_, _) => UpdateDynamicHints();
        NetModeCombo.SelectionChanged += (_, _) => UpdateDynamicHints();
    }

    private void LoadFromModel()
    {
        var hw = _vm.Hardware;

        CpuSlider.Value = Math.Clamp(hw.CpuCores, 1, (int)CpuSlider.Maximum);
        CpuValue.Text = $"{(int)CpuSlider.Value} 核";

        ThreadsCombo.SelectedIndex = hw.ThreadsPerCore >= 2 ? 1 : 0;

        MemSlider.Value = Math.Clamp(hw.MemoryMb, (int)MemSlider.Minimum, (int)MemSlider.Maximum);
        MemValue.Text = FormatMemory((int)MemSlider.Value);

        CpuModelCombo.Text = string.IsNullOrWhiteSpace(hw.CpuModel) ? "max" : hw.CpuModel;

        Select(FirmwareCombo, hw.Firmware);
        Select(AccelCombo, hw.Accel);
        Select(PointerCombo, hw.Pointer);
        Select(BootCombo, hw.BootOrder.Count > 0 ? hw.BootOrder[0] : BootDevice.CdRom);

        BootMenuCheck.IsChecked = hw.ShowBootMenu;
        RtcLocalCheck.IsChecked = hw.RtcLocalTime;
        HyperVCheck.IsChecked = hw.HyperVEnlightenments;

        IsoBox.Text = _vm.IsoPath ?? string.Empty;
        Iso2Box.Text = _vm.SecondaryIsoPath ?? string.Empty;

        Select(VideoCombo, _vm.Display.Video);
        Select(BackendCombo, _vm.Display.Backend);
        VramSlider.Value = Math.Clamp(_vm.Display.VideoMemoryMb, 16, 512);
        VramValue.Text = $"{(int)VramSlider.Value} MB";
        FullScreenCheck.IsChecked = _vm.Display.FullScreen;
        VncBox.Text = _vm.Display.VncDisplayNumber.ToString();

        Select(NetModeCombo, _vm.Network.Mode);
        Select(NicCombo, _vm.Network.Model);
        MacBox.Text = _vm.Network.MacAddress ?? string.Empty;
        TapBox.Text = _vm.Network.TapInterfaceName ?? string.Empty;

        Select(AudioBackendCombo, _vm.Audio.Backend);
        Select(SoundCardCombo, _vm.Audio.Card);

        ShareCheck.IsChecked = _vm.SharedFolder.Enabled;
        ShareBox.Text = _vm.SharedFolder.HostPath;
        ShareWriteCheck.IsChecked = _vm.SharedFolder.Writable;

        ExtraArgsBox.Text = _vm.ExtraArguments;
        NotesBox.Text = _vm.Notes;
        StopOnExitCheck.IsChecked = _vm.StopOnExit;
    }

    private void ApplyToModel()
    {
        var hw = _vm.Hardware;

        hw.CpuCores = (int)CpuSlider.Value;
        hw.ThreadsPerCore = ThreadsCombo.SelectedIndex >= 1 ? 2 : 1;
        hw.MemoryMb = (int)MemSlider.Value;
        hw.CpuModel = string.IsNullOrWhiteSpace(CpuModelCombo.Text) ? "max" : CpuModelCombo.Text.Trim();
        hw.Firmware = Read(FirmwareCombo, FirmwareType.Uefi);
        hw.Accel = Read(AccelCombo, AccelMode.Auto);
        hw.Pointer = Read(PointerCombo, PointerDevice.UsbTablet);
        hw.ShowBootMenu = BootMenuCheck.IsChecked == true;
        hw.RtcLocalTime = RtcLocalCheck.IsChecked == true;
        hw.HyperVEnlightenments = HyperVCheck.IsChecked == true;

        var first = Read(BootCombo, BootDevice.CdRom);
        var order = new List<BootDevice> { first };
        foreach (var d in new[] { BootDevice.CdRom, BootDevice.HardDisk, BootDevice.Network })
            if (d != first) order.Add(d);
        hw.BootOrder = order;

        _vm.Disks = _disks;
        _vm.IsoPath = Blank(IsoBox.Text);
        _vm.SecondaryIsoPath = Blank(Iso2Box.Text);

        _vm.Display.Video = Read(VideoCombo, VideoModel.Std);
        _vm.Display.Backend = Read(BackendCombo, DisplayBackend.Sdl);
        _vm.Display.VideoMemoryMb = (int)VramSlider.Value;
        _vm.Display.FullScreen = FullScreenCheck.IsChecked == true;
        _vm.Display.VncDisplayNumber = int.TryParse(VncBox.Text, out var vnc) ? Math.Clamp(vnc, 0, 99) : 1;

        _vm.Network.Mode = Read(NetModeCombo, NetworkMode.Nat);
        _vm.Network.Model = Read(NicCombo, NicModel.E1000);
        _vm.Network.MacAddress = Blank(MacBox.Text);
        _vm.Network.TapInterfaceName = Blank(TapBox.Text);
        _vm.Network.PortForwards = _forwards;

        _vm.Audio.Backend = Read(AudioBackendCombo, AudioBackend.DirectSound);
        _vm.Audio.Card = Read(SoundCardCombo, SoundCard.IntelHda);

        _vm.SharedFolder.Enabled = ShareCheck.IsChecked == true;
        _vm.SharedFolder.HostPath = ShareBox.Text.Trim();
        _vm.SharedFolder.Writable = ShareWriteCheck.IsChecked == true;

        _vm.ExtraArguments = ExtraArgsBox.Text.Trim();
        _vm.Notes = NotesBox.Text.Trim();
        _vm.StopOnExit = StopOnExitCheck.IsChecked == true;
    }

    private void UpdateDynamicHints()
    {
        if (!_loaded) return;

        FirmwareHint.Text = Read(FirmwareCombo, FirmwareType.Uefi) == FirmwareType.Uefi
            ? "UEFI 需要 OVMF 固件文件，Windows 11 必须使用该模式。首次启动会在虚拟机目录生成 NVRAM 变量文件。"
            : "传统 BIOS 兼容性最好，适合 Windows 7 及更早的系统。";

        var backend = Read(BackendCombo, DisplayBackend.Sdl);
        VncPanel.Visibility = backend == DisplayBackend.Vnc ? Visibility.Visible : Visibility.Collapsed;
        if (backend == DisplayBackend.Vnc)
        {
            var n = int.TryParse(VncBox.Text, out var v) ? v : 1;
            VncHint.Text = $"启动后用 VNC 客户端连接 127.0.0.1:{5900 + n}。";
        }

        var mode = Read(NetModeCombo, NetworkMode.Nat);
        TapPanel.Visibility = mode == NetworkMode.Bridged ? Visibility.Visible : Visibility.Collapsed;
        NetHint.Text = mode switch
        {
            NetworkMode.Nat => "NAT 开箱即用，客户机可访问外网，但外部无法主动连入（可用下方端口转发解决）。",
            NetworkMode.Bridged => "桥接后客户机会从局域网路由器直接获取 IP，与主机同级。",
            NetworkMode.HostOnly => "仅主机模式下客户机只能与主机通信，无法访问外网。",
            _ => "不连接网络，客户机中不会出现任何网卡。"
        };
    }

    // ================= 磁盘 =================

    private sealed record DiskRow(VmDisk Disk, string FileName, string Detail, string SizeText);

    private void RefreshDiskList()
    {
        var rows = new List<DiskRow>();

        foreach (var d in _disks)
        {
            var name = string.IsNullOrWhiteSpace(d.FileName) ? "（未指定文件）" : d.FileName;
            var detail = $"{EnumText.Describe(d.Format)} · {EnumText.Describe(d.Bus)}" +
                         (d.ReadOnly ? " · 只读" : string.Empty) +
                         (d.Ssd ? " · SSD" : string.Empty);

            var actual = d.GetActualSizeBytes(_vm.Directory);
            var size = actual > 0
                ? $"{d.CapacityGb} GB（占用 {DiskInfo.FormatBytes(actual)}）"
                : $"{d.CapacityGb} GB（文件缺失）";

            rows.Add(new DiskRow(d, name, detail, size));
        }

        DiskList.ItemsSource = rows;
        if (rows.Count > 0 && DiskList.SelectedIndex < 0) DiskList.SelectedIndex = 0;
    }

    private VmDisk? SelectedDisk => (DiskList.SelectedItem as DiskRow)?.Disk;

    private async void OnCreateDisk(object sender, RoutedEventArgs e)
    {
        var dialog = new DiskCreateDialog(_vm, _main) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.CreatedDisk is null) return;

        _disks.Add(dialog.CreatedDisk);
        RefreshDiskList();
        StatusText.Text = $"已创建磁盘 {dialog.CreatedDisk.FileName}";
        await Task.CompletedTask;
    }

    private void OnAttachDisk(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择已有的虚拟磁盘",
            Filter = "虚拟磁盘 (*.qcow2;*.img;*.vmdk;*.vhdx;*.vdi;*.raw)|*.qcow2;*.img;*.vmdk;*.vhdx;*.vdi;*.raw|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(_vm.Directory) ? _vm.Directory : string.Empty
        };

        if (dialog.ShowDialog() != true) return;

        var path = dialog.FileName;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".raw" or ".img" => DiskFormat.Raw,
            ".vmdk" => DiskFormat.Vmdk,
            ".vhdx" => DiskFormat.Vhdx,
            ".vdi" => DiskFormat.Vdi,
            _ => DiskFormat.Qcow2
        };

        // 位于虚拟机目录内则保存为相对路径，便于整体搬迁
        var vmDir = Path.GetFullPath(_vm.Directory);
        var stored = Path.GetFullPath(path).StartsWith(vmDir + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(path)
            : path;

        long bytes = 0;
        try { bytes = new FileInfo(path).Length; } catch { /* 忽略 */ }

        _disks.Add(new VmDisk
        {
            Path = stored,
            Format = format,
            Bus = _disks.Count == 0 ? DiskBus.Sata : DiskBus.Sata,
            CapacityGb = Math.Max(1, (int)(bytes / 1024 / 1024 / 1024))
        });

        RefreshDiskList();
        StatusText.Text = "已附加磁盘 " + Path.GetFileName(path);
    }

    private async void OnResizeDisk(object sender, RoutedEventArgs e)
    {
        var disk = SelectedDisk;
        if (disk is null)
        {
            MessageBox.Show("请先在列表中选择一块磁盘。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = TextPromptDialog.Show(this, "扩容磁盘",
            $"当前标称容量 {disk.CapacityGb} GB。请输入新的容量（GB，只能变大）：",
            (disk.CapacityGb + 10).ToString());

        if (input is null) return;

        if (!int.TryParse(input, out var newSize) || newSize <= disk.CapacityGb)
        {
            MessageBox.Show("请输入一个比当前容量更大的整数。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsEnabled = false;
        StatusText.Text = "正在扩容……";

        var (ok, message) = await _main.Disks.ResizeAsync(disk.ResolvePath(_vm.Directory), newSize);

        IsEnabled = true;

        if (ok)
        {
            disk.CapacityGb = newSize;
            RefreshDiskList();
            StatusText.Text = message;
        }
        else
        {
            StatusText.Text = "扩容失败";
            MessageBox.Show("扩容失败：\n" + message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnDiskProperties(object sender, RoutedEventArgs e)
    {
        var disk = SelectedDisk;
        if (disk is null) return;

        var full = disk.ResolvePath(_vm.Directory);
        var info = await _main.Disks.GetInfoAsync(full);

        var busOptions = EnumText.OptionsFor<DiskBus>();
        var current = busOptions.FirstOrDefault(o => o.Value == disk.Bus)?.Text ?? disk.Bus.ToString();

        var text =
            $"文件：{full}\n" +
            $"格式：{info?.Format ?? disk.FormatToQemuString()}\n" +
            $"虚拟容量：{info?.VirtualSizeText ?? disk.CapacityGb + " GB"}\n" +
            $"实际占用：{info?.ActualSizeText ?? "未知"}\n" +
            $"总线：{current}\n" +
            $"缓存模式：{disk.CacheMode}\n" +
            $"SSD 模拟：{(disk.Ssd ? "开启" : "关闭")}\n" +
            $"TRIM：{(disk.Discard ? "开启" : "关闭")}";

        MessageBox.Show(text, "磁盘属性", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnRemoveDisk(object sender, RoutedEventArgs e)
    {
        var disk = SelectedDisk;
        if (disk is null) return;

        var result = MessageBox.Show(
            $"要从这台虚拟机上移除磁盘「{disk.FileName}」吗？\n\n" +
            "选择「是」同时把磁盘文件移入回收站；\n" +
            "选择「否」只解除挂载，文件保留在磁盘上。",
            "移除磁盘", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel) return;

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var full = disk.ResolvePath(_vm.Directory);
                if (File.Exists(full))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        full,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除文件失败：" + ex.Message, "Raster Virtual",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _disks.Remove(disk);
        RefreshDiskList();
        StatusText.Text = "已移除磁盘";
    }

    // ================= ISO =================

    private void OnBrowseIso(object sender, RoutedEventArgs e) => PickIso(IsoBox);

    private void OnBrowseIso2(object sender, RoutedEventArgs e) => PickIso(Iso2Box);

    private void PickIso(TextBox target)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择光盘映像",
            Filter = "光盘映像 (*.iso;*.img)|*.iso;*.img|所有文件 (*.*)|*.*",
            InitialDirectory = _main.Settings.LastIsoDirectory ?? string.Empty
        };

        if (dialog.ShowDialog() != true) return;

        target.Text = dialog.FileName;
        _main.Settings.LastIsoDirectory = Path.GetDirectoryName(dialog.FileName);
        _main.Settings.Save();
    }

    private void OnEjectIso(object sender, RoutedEventArgs e) => IsoBox.Text = string.Empty;

    private void OnEjectIso2(object sender, RoutedEventArgs e) => Iso2Box.Text = string.Empty;

    // ================= 端口转发 =================

    private sealed record ForwardRow(PortForward Forward, string Text);

    private void RefreshForwardList()
    {
        ForwardList.ItemsSource = _forwards
            .Select(f => new ForwardRow(f, $"{f.Protocol.ToUpperInvariant()}  主机 {f.HostPort}  →  客户机 {f.GuestPort}"))
            .ToList();
    }

    private void OnAddForward(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HostPortBox.Text, out var host) || host is < 1 or > 65535 ||
            !int.TryParse(GuestPortBox.Text, out var guest) || guest is < 1 or > 65535)
        {
            MessageBox.Show("端口号必须是 1 到 65535 之间的整数。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var proto = ProtoCombo.SelectedItem as string ?? "tcp";

        if (_forwards.Any(f => f.HostPort == host && f.Protocol == proto))
        {
            MessageBox.Show("已经存在使用该主机端口的规则。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _forwards.Add(new PortForward
        {
            Name = $"{proto}-{host}",
            Protocol = proto,
            HostPort = host,
            GuestPort = guest
        });

        RefreshForwardList();
    }

    private void OnRemoveForward(object sender, RoutedEventArgs e)
    {
        if (ForwardList.SelectedItem is not ForwardRow row) return;
        _forwards.Remove(row.Forward);
        RefreshForwardList();
    }

    // ================= 共享文件夹 =================

    private void OnBrowseShare(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要共享给客户机的目录"
        };

        if (dialog.ShowDialog() == true)
            ShareBox.Text = dialog.FolderName;
    }

    // ================= 预览 / 保存 =================

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        try
        {
            var probe = _vm.Clone();
            ApplyToProbe(probe);

            var result = _main.ArgsBuilder.Build(probe, _main.Accel.CanAccelerate, 4444);
            var binary = _main.Locator.SystemBinaryPath ?? "qemu-system-x86_64.exe";

            var text = $"\"{binary}\"\n  " + string.Join("\n  ", result.Arguments);

            if (result.Warnings.Count > 0)
                text += "\n\n提示：\n  " + string.Join("\n  ", result.Warnings);

            PreviewText.Text = text;
        }
        catch (Exception ex)
        {
            PreviewText.Text = "生成预览失败：" + ex.Message;
        }
    }

    /// <summary>把当前界面上的值套用到一个临时副本，用于命令行预览。</summary>
    private void ApplyToProbe(VirtualMachine probe)
    {
        probe.Hardware.CpuCores = (int)CpuSlider.Value;
        probe.Hardware.ThreadsPerCore = ThreadsCombo.SelectedIndex >= 1 ? 2 : 1;
        probe.Hardware.MemoryMb = (int)MemSlider.Value;
        probe.Hardware.CpuModel = string.IsNullOrWhiteSpace(CpuModelCombo.Text) ? "max" : CpuModelCombo.Text.Trim();
        probe.Hardware.Firmware = Read(FirmwareCombo, FirmwareType.Uefi);
        probe.Hardware.Accel = Read(AccelCombo, AccelMode.Auto);
        probe.Hardware.Pointer = Read(PointerCombo, PointerDevice.UsbTablet);
        probe.Hardware.ShowBootMenu = BootMenuCheck.IsChecked == true;
        probe.Hardware.RtcLocalTime = RtcLocalCheck.IsChecked == true;
        probe.Hardware.HyperVEnlightenments = HyperVCheck.IsChecked == true;
        probe.Hardware.BootOrder = new List<BootDevice> { Read(BootCombo, BootDevice.CdRom) };

        probe.Disks = _disks.Select(CloneDisk).ToList();
        probe.IsoPath = Blank(IsoBox.Text);
        probe.SecondaryIsoPath = Blank(Iso2Box.Text);

        probe.Display.Video = Read(VideoCombo, VideoModel.Std);
        probe.Display.Backend = Read(BackendCombo, DisplayBackend.Sdl);
        probe.Display.VideoMemoryMb = (int)VramSlider.Value;
        probe.Display.FullScreen = FullScreenCheck.IsChecked == true;
        probe.Display.VncDisplayNumber = int.TryParse(VncBox.Text, out var v) ? v : 1;

        probe.Network.Mode = Read(NetModeCombo, NetworkMode.Nat);
        probe.Network.Model = Read(NicCombo, NicModel.E1000);
        probe.Network.MacAddress = Blank(MacBox.Text);
        probe.Network.TapInterfaceName = Blank(TapBox.Text);
        probe.Network.PortForwards = _forwards.Select(CloneForward).ToList();

        probe.Audio.Backend = Read(AudioBackendCombo, AudioBackend.DirectSound);
        probe.Audio.Card = Read(SoundCardCombo, SoundCard.IntelHda);

        probe.SharedFolder.Enabled = ShareCheck.IsChecked == true;
        probe.SharedFolder.HostPath = ShareBox.Text.Trim();
        probe.SharedFolder.Writable = ShareWriteCheck.IsChecked == true;

        probe.ExtraArguments = ExtraArgsBox.Text.Trim();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (ShareCheck.IsChecked == true && !Directory.Exists(ShareBox.Text.Trim()))
        {
            MessageBox.Show("共享文件夹指向的目录不存在，请重新选择。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mac = MacBox.Text.Trim();
        if (mac.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(
                mac, "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$"))
        {
            MessageBox.Show("MAC 地址格式不正确，应形如 52:54:00:12:34:56。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyToModel();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    // ================= 工具方法 =================

    private static VmDisk CloneDisk(VmDisk d) => new()
    {
        Id = d.Id,
        Path = d.Path,
        Format = d.Format,
        Bus = d.Bus,
        CapacityGb = d.CapacityGb,
        ReadOnly = d.ReadOnly,
        Ssd = d.Ssd,
        CacheMode = d.CacheMode,
        Discard = d.Discard
    };

    private static PortForward CloneForward(PortForward f) => new()
    {
        Name = f.Name,
        Protocol = f.Protocol,
        HostPort = f.HostPort,
        GuestPort = f.GuestPort
    };

    private static void FillEnum<T>(ComboBox box, T fallback) where T : struct, Enum
    {
        var options = EnumText.OptionsFor<T>();
        box.ItemsSource = options;
        box.SelectedItem = options.FirstOrDefault(o => o.Value.Equals(fallback)) ?? options.FirstOrDefault();
    }

    private static void Select<T>(ComboBox box, T value) where T : struct, Enum
    {
        if (box.ItemsSource is not IEnumerable<EnumOption<T>> options) return;
        var match = options.FirstOrDefault(o => o.Value.Equals(value));
        if (match is not null) box.SelectedItem = match;
    }

    private static T Read<T>(ComboBox box, T fallback) where T : struct, Enum =>
        box.SelectedItem is EnumOption<T> option ? option.Value : fallback;

    private static string? Blank(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string FormatMemory(int mb) =>
        mb >= 1024 ? $"{mb / 1024.0:0.#} GB" : $"{mb} MB";

    internal static long GetTotalPhysicalMemoryMb()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return info.TotalAvailableMemoryBytes / 1024 / 1024;
        }
        catch
        {
            // 忽略
        }
        return 8192;
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
