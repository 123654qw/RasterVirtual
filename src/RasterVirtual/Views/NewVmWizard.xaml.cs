using System.IO;
using System.Windows;
using System.Windows.Controls;
using RasterVirtual.Infrastructure;
using RasterVirtual.Models;
using RasterVirtual.Services;
using RasterVirtual.ViewModels;

namespace RasterVirtual.Views;

public partial class NewVmWizard : Window
{
    private readonly MainViewModel _main;
    private int _step = 1;
    private const int TotalSteps = 5;

    public VirtualMachine? CreatedMachine { get; private set; }

    public NewVmWizard(MainViewModel main)
    {
        InitializeComponent();
        _main = main;

        InitializeCombos();
        LocationBox.Text = _main.Settings.MachinesRoot;
        UpdateSpaceHint();
        UpdateCpuHint();
        UpdateMemHint();
        UpdateAccelHint();
        UpdateStepUi();

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void InitializeCombos()
    {
        BindCombo(OsCombo, EnumText.OptionsFor<GuestOsFamily>(), GuestOsFamily.WindowsModern);
        BindCombo(FirmwareCombo, EnumText.OptionsFor<FirmwareType>(), FirmwareType.Uefi);
        BindCombo(AccelCombo, EnumText.OptionsFor<AccelMode>(), AccelMode.Auto);
        BindCombo(DiskFormatCombo, EnumText.OptionsFor<DiskFormat>(), DiskFormat.Qcow2);
        BindCombo(DiskBusCombo, EnumText.OptionsFor<DiskBus>(), DiskBus.Sata);

        OsCombo.SelectionChanged += OnOsChanged;

        // 根据主机资源限制上限
        var logicalCores = Environment.ProcessorCount;
        CpuSlider.Maximum = Math.Max(2, logicalCores);
        CpuSlider.Value = Math.Clamp(logicalCores / 2, 1, CpuSlider.Maximum);

        var totalMemMb = GetTotalPhysicalMemoryMb();
        if (totalMemMb > 0)
        {
            // 最多允许分配主机内存的 75%
            MemSlider.Maximum = Math.Max(2048, (int)(totalMemMb * 0.75 / 256) * 256);
            MemSlider.Value = Math.Clamp(4096, 512, MemSlider.Maximum);
        }

        UpdateCpuValueText();
        UpdateMemValueText();
        UpdateDiskValueText();
    }

    private static void BindCombo<T>(ComboBox combo, List<EnumOption<T>> options, T selected)
        where T : struct, Enum
    {
        combo.ItemsSource = options;
        combo.DisplayMemberPath = nameof(EnumOption<T>.Text);
        combo.SelectedItem = options.FirstOrDefault(o => o.Value.Equals(selected)) ?? options[0];
    }

    private static T GetSelected<T>(ComboBox combo, T fallback) where T : struct, Enum =>
        combo.SelectedItem is EnumOption<T> option ? option.Value : fallback;

    // ---------------------------------------------------------------
    // 步骤导航

    private void UpdateStepUi()
    {
        Page1.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Page4.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Page5.Visibility = _step == 5 ? Visibility.Visible : Visibility.Collapsed;

        (StepTitle.Text, StepSubtitle.Text) = _step switch
        {
            1 => ("基本信息", "给虚拟机起个名字，并选择要安装的操作系统类型"),
            2 => ("处理器与内存", "分配虚拟机可以使用的计算资源"),
            3 => ("虚拟硬盘", "系统将安装在这块虚拟硬盘上"),
            4 => ("安装介质", "挂载系统安装光盘，虚拟机开机后会从这里引导"),
            _ => ("确认配置", "检查无误后即可创建虚拟机")
        };

        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5 };
        for (var i = 0; i < dots.Length; i++)
        {
            var active = i + 1 <= _step;
            dots[i].Background = active
                ? (System.Windows.Media.Brush)FindResource("BrushAccent")
                : (System.Windows.Media.Brush)FindResource("BrushSurfaceRaised");
            dots[i].BorderBrush = active
                ? (System.Windows.Media.Brush)FindResource("BrushAccent")
                : (System.Windows.Media.Brush)FindResource("BrushBorder");

            if (dots[i].Child is TextBlock tb)
                tb.Foreground = active
                    ? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x1A, 0x12, 0x06))
                    : (System.Windows.Media.Brush)FindResource("BrushTextMuted");
        }

        BackButton.IsEnabled = _step > 1;
        NextButton.Content = _step == TotalSteps ? "创建虚拟机" : "下一步";

        if (_step == TotalSteps) BuildSummary();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step <= 1) return;
        _step--;
        UpdateStepUi();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (!ValidateStep()) return;

        if (_step < TotalSteps)
        {
            _step++;
            UpdateStepUi();
            return;
        }

        await CreateAsync();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool ValidateStep()
    {
        switch (_step)
        {
            case 1:
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                {
                    Warn("请输入虚拟机名称。");
                    NameBox.Focus();
                    return false;
                }
                if (string.IsNullOrWhiteSpace(LocationBox.Text))
                {
                    Warn("请选择虚拟机的存放位置。");
                    return false;
                }
                return true;

            case 3:
                if (DiskExistingRadio.IsChecked == true &&
                    (string.IsNullOrWhiteSpace(ExistingDiskBox.Text) || !File.Exists(ExistingDiskBox.Text)))
                {
                    Warn("请选择一个有效的虚拟硬盘文件。");
                    return false;
                }
                return true;

            case 4:
                if (DiskNoneRadio.IsChecked == true && string.IsNullOrWhiteSpace(IsoBox.Text))
                {
                    Warn("既没有硬盘也没有安装映像，虚拟机将无法启动。请至少选择其中一项。");
                    return false;
                }
                return true;

            default:
                return true;
        }
    }

    private void Warn(string message) =>
        MessageBox.Show(message, "新建虚拟机", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ---------------------------------------------------------------
    // 事件

    private void OnOsChanged(object sender, SelectionChangedEventArgs e)
    {
        var os = GetSelected(OsCombo, GuestOsFamily.WindowsModern);

        // 按客户机类型套用一组经过验证的默认硬件
        switch (os)
        {
            case GuestOsFamily.WindowsModern:
                SetCombo(FirmwareCombo, FirmwareType.Uefi);
                SetCombo(DiskBusCombo, DiskBus.Sata);
                MemSlider.Value = Math.Min(4096, MemSlider.Maximum);
                DiskSlider.Value = 64;
                break;

            case GuestOsFamily.WindowsLegacy:
                SetCombo(FirmwareCombo, FirmwareType.Bios);
                SetCombo(DiskBusCombo, DiskBus.Ide);
                MemSlider.Value = Math.Min(2048, MemSlider.Maximum);
                DiskSlider.Value = 40;
                break;

            case GuestOsFamily.Linux:
                SetCombo(FirmwareCombo, FirmwareType.Uefi);
                SetCombo(DiskBusCombo, DiskBus.VirtioBlk);
                MemSlider.Value = Math.Min(4096, MemSlider.Maximum);
                DiskSlider.Value = 40;
                break;

            case GuestOsFamily.BsdOrOther:
                SetCombo(FirmwareCombo, FirmwareType.Uefi);
                SetCombo(DiskBusCombo, DiskBus.Sata);
                MemSlider.Value = Math.Min(2048, MemSlider.Maximum);
                DiskSlider.Value = 32;
                break;

            case GuestOsFamily.Dos:
                SetCombo(FirmwareCombo, FirmwareType.Bios);
                SetCombo(DiskBusCombo, DiskBus.Ide);
                MemSlider.Value = 512;
                DiskSlider.Value = 8;
                CpuSlider.Value = 1;
                break;
        }

        UpdateMemValueText();
        UpdateDiskValueText();
        UpdateCpuValueText();
    }

    private static void SetCombo<T>(ComboBox combo, T value) where T : struct, Enum
    {
        if (combo.ItemsSource is not List<EnumOption<T>> options) return;
        combo.SelectedItem = options.FirstOrDefault(o => o.Value.Equals(value));
    }

    private void OnCpuChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateCpuValueText();
        UpdateCpuHint();
    }

    private void OnMemChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateMemValueText();
        UpdateMemHint();
    }

    private void OnDiskSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateDiskValueText();

    private void UpdateCpuValueText()
    {
        if (CpuValue is not null) CpuValue.Text = $"{(int)CpuSlider.Value} 核";
    }

    private void UpdateMemValueText()
    {
        if (MemValue is null) return;
        var mb = (int)MemSlider.Value;
        MemValue.Text = mb >= 1024 ? $"{mb / 1024.0:0.#} GB" : $"{mb} MB";
    }

    private void UpdateDiskValueText()
    {
        if (DiskValue is not null) DiskValue.Text = $"{(int)DiskSlider.Value} GB";
    }

    private void UpdateCpuHint()
    {
        if (CpuHint is null) return;
        var total = Environment.ProcessorCount;
        CpuHint.Text = $"主机共有 {total} 个逻辑核心。分配过多会让主机变卡，建议不超过一半。";
    }

    private void UpdateMemHint()
    {
        if (MemHint is null) return;
        var total = GetTotalPhysicalMemoryMb();
        MemHint.Text = total > 0
            ? $"主机物理内存约 {total / 1024.0:0.#} GB。虚拟机运行期间这部分内存会被独占。"
            : "虚拟机运行期间这部分内存会被独占。";
    }

    private void UpdateAccelHint()
    {
        if (AccelHint is null) return;
        AccelHint.Text = _main.Accel.CanAccelerate
            ? "已检测到硬件加速可用，虚拟机性能接近原生。"
            : $"当前硬件加速不可用（{_main.Accel.Summary}）。{_main.Accel.Advice}";
    }

    private void UpdateSpaceHint()
    {
        try
        {
            var root = Path.GetPathRoot(LocationBox.Text);
            if (string.IsNullOrEmpty(root)) return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;

            SpaceHint.Text = $"所在磁盘 {drive.Name} 可用空间 " +
                             $"{DiskInfo.FormatBytes(drive.AvailableFreeSpace)}。";
        }
        catch
        {
            SpaceHint.Text = string.Empty;
        }
    }

    private void OnBrowseLocation(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择虚拟机存放位置",
            InitialDirectory = Directory.Exists(LocationBox.Text) ? LocationBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == true)
        {
            LocationBox.Text = dialog.FolderName;
            UpdateSpaceHint();
        }
    }

    private void OnDiskModeChanged(object sender, RoutedEventArgs e)
    {
        if (NewDiskPanel is null || ExistingDiskPanel is null) return;
        NewDiskPanel.IsEnabled = DiskNewRadio.IsChecked == true;
        NewDiskPanel.Opacity = DiskNewRadio.IsChecked == true ? 1.0 : 0.45;
        ExistingDiskPanel.IsEnabled = DiskExistingRadio.IsChecked == true;
    }

    private void OnBrowseExistingDisk(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择虚拟硬盘文件",
            Filter = "虚拟硬盘 (*.qcow2;*.img;*.vmdk;*.vhdx;*.vdi;*.raw)|*.qcow2;*.img;*.vmdk;*.vhdx;*.vdi;*.raw|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
            ExistingDiskBox.Text = dialog.FileName;
    }

    private void OnBrowseIso(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择系统安装映像",
            Filter = "光盘映像 (*.iso;*.img)|*.iso;*.img|所有文件 (*.*)|*.*",
            InitialDirectory = _main.Settings.LastIsoDirectory ?? string.Empty
        };

        if (dialog.ShowDialog() != true) return;

        IsoBox.Text = dialog.FileName;
        _main.Settings.LastIsoDirectory = Path.GetDirectoryName(dialog.FileName);

        try
        {
            var fi = new FileInfo(dialog.FileName);
            IsoInfo.Text = $"映像大小 {DiskInfo.FormatBytes(fi.Length)}。";
        }
        catch
        {
            IsoInfo.Text = string.Empty;
        }
    }

    private void OnClearIso(object sender, RoutedEventArgs e)
    {
        IsoBox.Text = string.Empty;
        IsoInfo.Text = string.Empty;
    }

    private void OnBrowseSecondIso(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择附加驱动光盘",
            Filter = "光盘映像 (*.iso)|*.iso|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
            SecondIsoBox.Text = dialog.FileName;
    }

    // ---------------------------------------------------------------
    // 摘要与创建

    private void BuildSummary()
    {
        SummaryPanel.Children.Clear();

        void AddRow(string label, string value, bool highlight = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 11) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lb = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushTextMuted")
            };
            Grid.SetColumn(lb, 0);

            var vb = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = highlight
                    ? (System.Windows.Media.Brush)FindResource("BrushAccent")
                    : (System.Windows.Media.Brush)FindResource("BrushText")
            };
            Grid.SetColumn(vb, 1);

            grid.Children.Add(lb);
            grid.Children.Add(vb);
            SummaryPanel.Children.Add(grid);
        }

        var os = GetSelected(OsCombo, GuestOsFamily.WindowsModern);
        var mem = (int)MemSlider.Value;

        AddRow("名称", NameBox.Text.Trim(), true);
        AddRow("客户机系统", EnumText.Describe(os));
        AddRow("存放位置", Path.Combine(LocationBox.Text, VmRepository.SanitizeName(NameBox.Text)));
        AddRow("处理器", $"{(int)CpuSlider.Value} 核");
        AddRow("内存", mem >= 1024 ? $"{mem / 1024.0:0.#} GB" : $"{mem} MB");
        AddRow("固件", EnumText.Describe(GetSelected(FirmwareCombo, FirmwareType.Uefi)));
        AddRow("加速模式", EnumText.Describe(GetSelected(AccelCombo, AccelMode.Auto)));

        if (DiskNewRadio.IsChecked == true)
        {
            AddRow("虚拟硬盘",
                $"新建 {(int)DiskSlider.Value} GB · " +
                $"{EnumText.Describe(GetSelected(DiskFormatCombo, DiskFormat.Qcow2))}");
            AddRow("磁盘控制器", EnumText.Describe(GetSelected(DiskBusCombo, DiskBus.Sata)));
        }
        else if (DiskExistingRadio.IsChecked == true)
        {
            AddRow("虚拟硬盘", "使用已有文件 · " + Path.GetFileName(ExistingDiskBox.Text));
        }
        else
        {
            AddRow("虚拟硬盘", "无（仅从光盘运行）");
        }

        AddRow("安装映像", string.IsNullOrWhiteSpace(IsoBox.Text)
            ? "未挂载"
            : Path.GetFileName(IsoBox.Text));

        if (!string.IsNullOrWhiteSpace(SecondIsoBox.Text))
            AddRow("驱动光盘", Path.GetFileName(SecondIsoBox.Text));

        AddRow("引导顺序", BootFromIsoCheck.IsChecked == true ? "光驱 → 硬盘" : "硬盘 → 光驱");
    }

    private async Task CreateAsync()
    {
        NextButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Text = "正在创建虚拟机……";

        try
        {
            var name = NameBox.Text.Trim();
            var os = GetSelected(OsCombo, GuestOsFamily.WindowsModern);

            // 允许用户在向导里改存放根目录
            if (!string.Equals(LocationBox.Text, _main.Settings.MachinesRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                _main.Settings.MachinesRoot = LocationBox.Text;
            }
            _main.Settings.Save();

            var directory = _main.Repository.AllocateDirectory(name);
            Directory.CreateDirectory(directory);

            var vm = new VirtualMachine
            {
                Name = name,
                OsFamily = os,
                Directory = directory,
                Hardware =
                {
                    CpuCores = (int)CpuSlider.Value,
                    MemoryMb = (int)MemSlider.Value,
                    Firmware = GetSelected(FirmwareCombo, FirmwareType.Uefi),
                    Accel = GetSelected(AccelCombo, AccelMode.Auto),
                    ShowBootMenu = BootMenuCheck.IsChecked == true,
                    RtcLocalTime = os is GuestOsFamily.WindowsModern
                        or GuestOsFamily.WindowsLegacy or GuestOsFamily.Dos,
                    BootOrder = BootFromIsoCheck.IsChecked == true
                        ? new List<BootDevice> { BootDevice.CdRom, BootDevice.HardDisk }
                        : new List<BootDevice> { BootDevice.HardDisk, BootDevice.CdRom }
                }
            };

            // 网卡型号按系统类型选择兼容性最好的
            vm.Network.Model = os switch
            {
                GuestOsFamily.Linux => NicModel.VirtioNet,
                GuestOsFamily.WindowsLegacy => NicModel.Rtl8139,
                GuestOsFamily.Dos => NicModel.Rtl8139,
                _ => NicModel.E1000
            };

            vm.Display.Video = os == GuestOsFamily.Dos ? VideoModel.Cirrus : VideoModel.Std;

            if (!string.IsNullOrWhiteSpace(IsoBox.Text))
                vm.IsoPath = IsoBox.Text;

            if (!string.IsNullOrWhiteSpace(SecondIsoBox.Text))
                vm.SecondaryIsoPath = SecondIsoBox.Text;

            // ---- 磁盘 ----
            if (DiskNewRadio.IsChecked == true)
            {
                var format = GetSelected(DiskFormatCombo, DiskFormat.Qcow2);
                var sizeGb = (int)DiskSlider.Value;
                var fileName = VmRepository.SanitizeName(name) + VmDisk.ExtensionFor(format);
                var fullPath = Path.Combine(directory, fileName);

                StatusText.Text = $"正在创建 {sizeGb} GB 虚拟硬盘……";

                var (ok, message) = await _main.Disks.CreateDiskAsync(fullPath, format, sizeGb);
                if (!ok)
                {
                    MessageBox.Show("创建虚拟硬盘失败：\n\n" + message,
                        "新建虚拟机", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                vm.Disks.Add(new VmDisk
                {
                    Path = fileName,
                    Format = format,
                    Bus = GetSelected(DiskBusCombo, DiskBus.Sata),
                    CapacityGb = sizeGb
                });
            }
            else if (DiskExistingRadio.IsChecked == true)
            {
                var path = ExistingDiskBox.Text;
                var info = await _main.Disks.GetInfoAsync(path);

                var format = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".qcow2" => DiskFormat.Qcow2,
                    ".vmdk" => DiskFormat.Vmdk,
                    ".vhdx" => DiskFormat.Vhdx,
                    ".vdi" => DiskFormat.Vdi,
                    _ => DiskFormat.Raw
                };

                if (info is not null)
                {
                    format = info.Format switch
                    {
                        "qcow2" => DiskFormat.Qcow2,
                        "vmdk" => DiskFormat.Vmdk,
                        "vhdx" => DiskFormat.Vhdx,
                        "vdi" => DiskFormat.Vdi,
                        _ => DiskFormat.Raw
                    };
                }

                vm.Disks.Add(new VmDisk
                {
                    Path = path,
                    Format = format,
                    Bus = GetSelected(DiskBusCombo, DiskBus.Sata),
                    CapacityGb = info is null ? 0 : (int)(info.VirtualSizeBytes / 1024 / 1024 / 1024)
                });
            }

            _main.Repository.Save(vm);
            CreatedMachine = vm;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("创建失败：\n\n" + ex.Message,
                "新建虚拟机", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            NextButton.IsEnabled = true;
            BackButton.IsEnabled = _step > 1;
            CancelButton.IsEnabled = true;
            StatusText.Text = string.Empty;
        }
    }

    /// <summary>读取主机物理内存总量（MB）。</summary>
    private static long GetTotalPhysicalMemoryMb()
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
        return 0;
    }
}
