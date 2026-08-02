using System.IO;
using System.Windows.Media;
using RasterVirtual.Infrastructure;
using RasterVirtual.Models;
using RasterVirtual.Services;

namespace RasterVirtual.ViewModels;

/// <summary>列表中的一台虚拟机。持有对应的运行会话。</summary>
public sealed class VmItemViewModel : ObservableObject
{
    private readonly QemuLocator _locator;
    private readonly QemuArgsBuilder _argsBuilder;
    private VmSession? _session;

    public VirtualMachine Machine { get; }

    public event EventHandler<string>? LogAppended;

    public VmItemViewModel(VirtualMachine machine, QemuLocator locator, QemuArgsBuilder argsBuilder)
    {
        Machine = machine;
        _locator = locator;
        _argsBuilder = argsBuilder;
    }

    public string Name => Machine.Name;

    public string Id => Machine.Id;

    public VmState State => _session?.State ?? Machine.State;

    public bool IsRunning => State is VmState.Running or VmState.Paused or VmState.Starting;

    public bool IsStopped => State == VmState.Stopped;

    public bool IsPaused => State == VmState.Paused;

    public string StateText => State switch
    {
        VmState.Running => "运行中",
        VmState.Paused => "已暂停",
        VmState.Starting => "启动中",
        VmState.Stopping => "关机中",
        VmState.Faulted => "启动失败",
        _ => "已关闭"
    };

    public Brush StateBrush => State switch
    {
        VmState.Running => new SolidColorBrush(Color.FromRgb(0x54, 0xB3, 0x7E)),
        VmState.Starting => new SolidColorBrush(Color.FromRgb(0xE5, 0x84, 0x3C)),
        VmState.Paused => new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41)),
        VmState.Stopping => new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41)),
        VmState.Faulted => new SolidColorBrush(Color.FromRgb(0xD7, 0x5F, 0x4B)),
        _ => new SolidColorBrush(Color.FromRgb(0x69, 0x70, 0x7A))
    };

    public string OsText => Machine.OsFamily switch
    {
        GuestOsFamily.WindowsModern => "Windows 10 / 11",
        GuestOsFamily.WindowsLegacy => "Windows 7 / XP",
        GuestOsFamily.Linux => "Linux",
        GuestOsFamily.BsdOrOther => "BSD / 其它",
        GuestOsFamily.Dos => "DOS",
        _ => "未知"
    };

    public string SpecText
    {
        get
        {
            var hw = Machine.Hardware;
            var mem = hw.MemoryMb >= 1024
                ? $"{hw.MemoryMb / 1024.0:0.#} GB"
                : $"{hw.MemoryMb} MB";
            return $"{hw.CpuCores} 核 · {mem}";
        }
    }

    public string DiskText
    {
        get
        {
            if (Machine.Disks.Count == 0) return "无硬盘";
            var total = Machine.Disks.Sum(d => (long)d.CapacityGb);
            var actual = Machine.Disks.Sum(d => d.GetActualSizeBytes(Machine.Directory));
            return $"{total} GB（已占用 {DiskInfo.FormatBytes(actual)}）";
        }
    }

    public string IsoText => string.IsNullOrWhiteSpace(Machine.IsoPath)
        ? "未挂载"
        : Path.GetFileName(Machine.IsoPath);

    public string FirmwareText => Machine.Hardware.Firmware == FirmwareType.Uefi ? "UEFI" : "传统 BIOS";

    public string NetworkText => Machine.Network.Mode switch
    {
        NetworkMode.Nat => "网络地址转换（NAT）",
        NetworkMode.Bridged => "桥接网络",
        NetworkMode.HostOnly => "仅主机",
        _ => "未连接"
    };

    public string AccelText => Machine.Hardware.Accel switch
    {
        AccelMode.Whpx => "强制硬件加速",
        AccelMode.Tcg => "软件模拟",
        _ => "自动"
    };

    public string LastStartedText => Machine.LastStartedAt is null
        ? "从未启动"
        : Machine.LastStartedAt.Value.ToString("yyyy-MM-dd HH:mm");

    public string RuntimeText
    {
        get
        {
            var t = TimeSpan.FromSeconds(Machine.TotalRuntimeSeconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours} 小时 {t.Minutes} 分钟";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} 分钟";
            return "不足 1 分钟";
        }
    }

    /// <summary>虚拟机首字母，用作列表头像。</summary>
    public string Initial => string.IsNullOrWhiteSpace(Machine.Name)
        ? "?"
        : Machine.Name.Trim()[..1].ToUpperInvariant();

    // ---------------------------------------------------------------

    private VmSession EnsureSession()
    {
        if (_session is not null && _session.State != VmState.Stopped)
            return _session;

        _session?.Dispose();
        _session = new VmSession(Machine, _locator, _argsBuilder);
        _session.StateChanged += (_, _) => RaiseStateProperties();
        _session.LogAppended += (_, line) => LogAppended?.Invoke(this, $"[{Machine.Name}] {line}");
        return _session;
    }

    public async Task<bool> StartAsync(bool accelAvailable)
    {
        var session = EnsureSession();
        var ok = await session.StartAsync(accelAvailable);
        RaiseStateProperties();
        return ok;
    }

    public async Task PauseOrResumeAsync()
    {
        if (_session is null) return;
        if (_session.State == VmState.Running) await _session.PauseAsync();
        else if (_session.State == VmState.Paused) await _session.ResumeAsync();
        RaiseStateProperties();
    }

    public async Task ShutdownAsync()
    {
        if (_session is null) return;
        await _session.ShutdownAsync();
        RaiseStateProperties();
    }

    public async Task PowerOffAsync()
    {
        if (_session is null) return;
        await _session.PowerOffAsync();
        RaiseStateProperties();
    }

    public async Task ResetAsync()
    {
        if (_session is null) return;
        await _session.ResetAsync();
    }

    public Task<(bool ok, string message)> SaveSnapshotAsync(string tag)
    {
        if (_session is null || _session.State == VmState.Stopped)
            return Task.FromResult((false, "虚拟机未运行。"));
        return _session.SaveSnapshotAsync(tag);
    }

    public Task<(bool ok, string message)> RestoreSnapshotAsync(string tag)
    {
        if (_session is null || _session.State == VmState.Stopped)
            return Task.FromResult((false, "虚拟机未运行。"));
        return _session.RestoreSnapshotAsync(tag);
    }

    public Task<(bool ok, string message)> DeleteSnapshotAsync(string tag)
    {
        if (_session is null || _session.State == VmState.Stopped)
            return Task.FromResult((false, "虚拟机未运行。"));
        return _session.DeleteSnapshotAsync(tag);
    }

    public Task<string?> CaptureScreenshotAsync(string path)
    {
        if (_session is null || _session.State == VmState.Stopped)
            return Task.FromResult<string?>(null);
        return _session.CaptureScreenshotAsync(path);
    }

    public void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(LastStartedText));
        OnPropertyChanged(nameof(RuntimeText));
        Infrastructure.RelayCommand.RaiseCanExecuteChanged();
    }

    public void RaiseAllProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(OsText));
        OnPropertyChanged(nameof(SpecText));
        OnPropertyChanged(nameof(DiskText));
        OnPropertyChanged(nameof(IsoText));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(AccelText));
        OnPropertyChanged(nameof(Initial));
        RaiseStateProperties();
    }

    public void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
    }
}
