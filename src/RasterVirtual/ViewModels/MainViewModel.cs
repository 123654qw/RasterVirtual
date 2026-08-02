using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using RasterVirtual.Infrastructure;
using RasterVirtual.Models;
using RasterVirtual.Services;

namespace RasterVirtual.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public AppSettings Settings { get; }
    public QemuLocator Locator { get; }
    public QemuArgsBuilder ArgsBuilder { get; }
    public DiskService Disks { get; }
    public VmRepository Repository { get; }
    public AccelStatus Accel { get; private set; }

    public ObservableCollection<VmItemViewModel> Machines { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    private VmItemViewModel? _selected;
    public VmItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                // EmptyStateVisible 与 DetailVisible 均由 Selected 派生，二者必须同步通知，
                // 否则从「有选中」切到「无选中」（如删除唯一虚拟机）时，详情面板会残留为 Visible，
                // 与空状态页重叠；反之从「无选中」切到「有选中」时，详情面板会残留为 Collapsed 不显示。
                OnPropertyChanged(nameof(EmptyStateVisible));
                OnPropertyChanged(nameof(DetailVisible));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public Visibility EmptyStateVisible => Selected is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DetailVisible => Selected is null ? Visibility.Collapsed : Visibility.Visible;

    private string _qemuStatus = "正在检测运行时……";
    public string QemuStatus
    {
        get => _qemuStatus;
        private set => SetProperty(ref _qemuStatus, value);
    }

    private string _accelStatus = string.Empty;
    public string AccelStatusText
    {
        get => _accelStatus;
        private set => SetProperty(ref _accelStatus, value);
    }

    private bool _qemuReady;
    public bool QemuReady
    {
        get => _qemuReady;
        private set
        {
            if (SetProperty(ref _qemuReady, value))
                RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
        }
    }

    private readonly List<VmItemViewModel> _allMachines = new();

    // ---------------- 命令 ----------------
    public ICommand NewVmCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ShutdownCommand { get; }
    public ICommand PowerOffCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand SnapshotCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CloneCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PreferencesCommand { get; }
    public ICommand MountIsoCommand { get; }
    public ICommand ScreenshotCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand AboutCommand { get; }

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        Locator = new QemuLocator();
        ArgsBuilder = new QemuArgsBuilder(Locator);
        Disks = new DiskService(Locator);
        Repository = new VmRepository(Settings);
        Accel = AccelDetector.Detect();

        NewVmCommand = new AsyncRelayCommand(NewVmAsync);
        StartCommand = new AsyncRelayCommand(StartAsync, () => Selected is { IsStopped: true } && QemuReady);
        PauseCommand = new AsyncRelayCommand(PauseAsync, () => Selected is { IsRunning: true });
        ShutdownCommand = new AsyncRelayCommand(ShutdownAsync, () => Selected is { IsRunning: true });
        PowerOffCommand = new AsyncRelayCommand(PowerOffAsync, () => Selected is { IsRunning: true });
        ResetCommand = new AsyncRelayCommand(ResetAsync, () => Selected is { IsRunning: true });
        SettingsCommand = new RelayCommand(OpenSettings, () => Selected is not null);
        SnapshotCommand = new RelayCommand(OpenSnapshots, () => Selected is not null);
        DeleteCommand = new RelayCommand(DeleteVm, () => Selected is { IsStopped: true });
        CloneCommand = new AsyncRelayCommand(CloneVmAsync, () => Selected is { IsStopped: true });
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Selected is not null);
        RefreshCommand = new RelayCommand(() => ReloadMachines());
        PreferencesCommand = new RelayCommand(OpenPreferences);
        MountIsoCommand = new RelayCommand(MountIso, () => Selected is { IsStopped: true });
        ScreenshotCommand = new AsyncRelayCommand(ScreenshotAsync, () => Selected is { IsRunning: true });
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
        AboutCommand = new RelayCommand(ShowAbout);

        Initialize();
    }

    // ---------------------------------------------------------------

    private void Initialize()
    {
        var found = Locator.Locate(Settings.QemuDirectoryOverride);
        QemuReady = found;

        if (found)
        {
            QemuStatus = "运行时就绪";
            _ = RefreshQemuVersionAsync();
        }
        else
        {
            QemuStatus = "未找到 QEMU 运行时";
            AppendLog("未能定位 QEMU 运行时。请在「首选项」中手动指定 qemu-system-x86_64.exe 所在目录。");
        }

        AccelStatusText = Accel.Summary;
        if (!Accel.CanAccelerate && Accel.Advice is not null)
            AppendLog("硬件加速不可用：" + Accel.Advice);

        ReloadMachines();
    }

    private async Task RefreshQemuVersionAsync()
    {
        var version = await Locator.GetVersionAsync();
        if (version is not null)
        {
            QemuStatus = version.Replace("QEMU emulator version", "QEMU").Trim();
            AppendLog($"已加载运行时：{version}");
            AppendLog($"运行时路径：{Locator.QemuDirectory}");
        }
    }

    public void ReloadMachines()
    {
        var previousId = Selected?.Id;

        foreach (var m in _allMachines)
        {
            if (m.IsRunning) continue;
            m.DisposeSession();
        }

        var running = _allMachines.Where(m => m.IsRunning).ToDictionary(m => m.Id);

        _allMachines.Clear();

        foreach (var vm in Repository.LoadAll())
        {
            if (running.TryGetValue(vm.Id, out var existing))
            {
                _allMachines.Add(existing);
                continue;
            }

            var item = new VmItemViewModel(vm, Locator, ArgsBuilder);
            item.LogAppended += (_, line) => AppendLog(line);
            _allMachines.Add(item);
        }

        ApplyFilter();

        Selected = previousId is not null
            ? Machines.FirstOrDefault(m => m.Id == previousId) ?? Machines.FirstOrDefault()
            : Machines.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        Machines.Clear();
        var keyword = SearchText?.Trim() ?? string.Empty;

        foreach (var m in _allMachines)
        {
            if (keyword.Length == 0 ||
                m.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                m.OsText.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            {
                Machines.Add(m);
            }
        }

        OnPropertyChanged(nameof(MachineCountText));
    }

    public string MachineCountText => _allMachines.Count == 0
        ? "暂无虚拟机"
        : $"共 {_allMachines.Count} 台虚拟机";

    // ---------------------------------------------------------------
    // 命令实现

    private async Task NewVmAsync()
    {
        if (!QemuReady)
        {
            MessageBox.Show("尚未找到 QEMU 运行时，无法创建虚拟机。\n请先在「首选项」中指定运行时目录。",
                "Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var wizard = new Views.NewVmWizard(this) { Owner = Application.Current.MainWindow };
        if (wizard.ShowDialog() == true && wizard.CreatedMachine is not null)
        {
            await Task.Yield();
            ReloadMachines();
            Selected = Machines.FirstOrDefault(m => m.Id == wizard.CreatedMachine.Id);
            AppendLog($"已创建虚拟机「{wizard.CreatedMachine.Name}」。");
        }
    }

    private async Task StartAsync()
    {
        if (Selected is null) return;

        if (Selected.Machine.Disks.Count == 0 && string.IsNullOrWhiteSpace(Selected.Machine.IsoPath))
        {
            MessageBox.Show("这台虚拟机既没有硬盘也没有挂载 ISO，无法启动。",
                "Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppendLog($"正在启动「{Selected.Name}」……");
        await Selected.StartAsync(Accel.CanAccelerate);
        Repository.Save(Selected.Machine);
    }

    private async Task PauseAsync()
    {
        if (Selected is null) return;
        await Selected.PauseOrResumeAsync();
    }

    private async Task ShutdownAsync()
    {
        if (Selected is null) return;
        await Selected.ShutdownAsync();
        Repository.Save(Selected.Machine);
    }

    private async Task PowerOffAsync()
    {
        if (Selected is null) return;

        var confirm = MessageBox.Show(
            $"确定要强制关闭「{Selected.Name}」吗？\n\n这相当于直接拔掉电源，客户机中未保存的数据会丢失。",
            "强制断电", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await Selected.PowerOffAsync();
        Repository.Save(Selected.Machine);
    }

    private async Task ResetAsync()
    {
        if (Selected is null) return;

        var confirm = MessageBox.Show(
            $"确定要重启「{Selected.Name}」吗？未保存的数据会丢失。",
            "重启虚拟机", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;
        await Selected.ResetAsync();
    }

    private void OpenSettings()
    {
        if (Selected is null) return;

        if (Selected.IsRunning)
        {
            MessageBox.Show("请先关闭虚拟机再修改设置。", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new Views.VmSettingsWindow(Selected.Machine, this)
        {
            Owner = Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            Repository.Save(Selected.Machine);
            Selected.RaiseAllProperties();
            AppendLog($"已保存「{Selected.Name}」的设置。");
        }
    }

    private void OpenSnapshots()
    {
        if (Selected is null) return;

        var window = new Views.SnapshotWindow(Selected, this)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void DeleteVm()
    {
        if (Selected is null) return;

        var vm = Selected.Machine;
        var result = MessageBox.Show(
            $"确定要删除虚拟机「{vm.Name}」吗？\n\n" +
            $"选择「是」会把整个虚拟机目录（含虚拟硬盘）移入回收站：\n{vm.Directory}\n\n" +
            "选择「否」只从列表中移除，磁盘文件保留。",
            "删除虚拟机", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel) return;

        var (ok, message) = Repository.Delete(vm, deleteFiles: result == MessageBoxResult.Yes);

        if (!ok)
        {
            MessageBox.Show("删除失败：" + message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AppendLog($"已删除虚拟机「{vm.Name}」：{message}");
        Selected.DisposeSession();
        Selected = null;
        ReloadMachines();
    }

    private async Task CloneVmAsync()
    {
        if (Selected is null) return;

        var source = Selected.Machine;
        var confirm = MessageBox.Show(
            $"将完整复制「{source.Name}」，包括全部虚拟硬盘。\n磁盘较大时可能需要几分钟，是否继续？",
            "克隆虚拟机", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var clone = source.Clone();
            clone.Id = Guid.NewGuid().ToString("N");
            clone.Name = source.Name + " 的副本";
            clone.Directory = Repository.AllocateDirectory(clone.Name);
            clone.CreatedAt = DateTime.Now;
            clone.LastStartedAt = null;
            clone.TotalRuntimeSeconds = 0;

            Directory.CreateDirectory(clone.Directory);
            AppendLog($"正在克隆「{source.Name}」……");

            foreach (var disk in clone.Disks)
            {
                var srcPath = disk.ResolvePath(source.Directory);
                if (!File.Exists(srcPath)) continue;

                var fileName = Path.GetFileName(srcPath);
                var dstPath = Path.Combine(clone.Directory, fileName);

                await Task.Run(() => File.Copy(srcPath, dstPath, overwrite: true));
                disk.Path = fileName;
                AppendLog($"  已复制磁盘 {fileName}");
            }

            Repository.Save(clone);
            ReloadMachines();
            Selected = Machines.FirstOrDefault(m => m.Id == clone.Id);
            AppendLog($"克隆完成：「{clone.Name}」");
        }
        catch (Exception ex)
        {
            MessageBox.Show("克隆失败：" + ex.Message, "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolder()
    {
        if (Selected is null) return;
        try
        {
            if (!Directory.Exists(Selected.Machine.Directory))
                Directory.CreateDirectory(Selected.Machine.Directory);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Selected.Machine.Directory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog("打开目录失败：" + ex.Message);
        }
    }

    private void MountIso()
    {
        if (Selected is null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择光盘映像",
            Filter = "光盘映像 (*.iso;*.img)|*.iso;*.img|所有文件 (*.*)|*.*",
            InitialDirectory = Settings.LastIsoDirectory ?? string.Empty
        };

        if (dialog.ShowDialog() != true) return;

        Selected.Machine.IsoPath = dialog.FileName;
        Settings.LastIsoDirectory = Path.GetDirectoryName(dialog.FileName);
        Settings.Save();
        Repository.Save(Selected.Machine);
        Selected.RaiseAllProperties();
        AppendLog($"已挂载映像：{Path.GetFileName(dialog.FileName)}");
    }

    private async Task ScreenshotAsync()
    {
        if (Selected is null) return;

        var dir = Path.Combine(Selected.Machine.Directory, "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"screen-{DateTime.Now:yyyyMMdd-HHmmss}.ppm");

        var saved = await Selected.CaptureScreenshotAsync(path);
        if (saved is not null)
        {
            MessageBox.Show($"截图已保存到：\n{saved}", "Raster Virtual",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenPreferences()
    {
        var window = new Views.PreferencesWindow(this) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            Settings.Save();
            var found = Locator.Locate(Settings.QemuDirectoryOverride);
            QemuReady = found;
            QemuStatus = found ? "运行时就绪" : "未找到 QEMU 运行时";
            if (found) _ = RefreshQemuVersionAsync();
            ReloadMachines();
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "Raster Virtual 1.0\n\n" +
            "桌面级虚拟机管理器，基于 QEMU 引擎。\n" +
            "支持从 ISO 映像引导并安装完整的客户机操作系统。\n\n" +
            $"运行时：{QemuStatus}\n" +
            $"硬件加速：{AccelStatusText}",
            "关于 Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------

    public void AppendLog(string message)
    {
        var app = Application.Current;
        if (app is null) return;

        app.Dispatcher.Invoke(() =>
        {
            var line = message.StartsWith('[')
                ? message
                : $"[{DateTime.Now:HH:mm:ss}] {message}";

            LogLines.Add(line);

            while (LogLines.Count > 2000)
                LogLines.RemoveAt(0);
        });
    }

    /// <summary>窗口关闭时清理所有会话。</summary>
    public async Task ShutdownAllAsync()
    {
        foreach (var m in _allMachines.Where(m => m.IsRunning).ToList())
        {
            try
            {
                await m.PowerOffAsync();
                Repository.Save(m.Machine);
            }
            catch
            {
                // 忽略退出阶段的异常
            }
        }

        foreach (var m in _allMachines)
            m.DisposeSession();
    }

    public bool HasRunningMachines => _allMachines.Any(m => m.IsRunning);
}
