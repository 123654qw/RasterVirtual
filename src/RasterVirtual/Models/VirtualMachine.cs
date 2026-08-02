using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;

namespace RasterVirtual.Models;

/// <summary>
/// 一台虚拟机的完整定义。该对象会被序列化为虚拟机目录下的 machine.json。
/// </summary>
public sealed class VirtualMachine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新建虚拟机";

    public string Notes { get; set; } = string.Empty;

    public GuestOsFamily OsFamily { get; set; } = GuestOsFamily.WindowsModern;

    /// <summary>虚拟机所在目录（绝对路径），磁盘与快照都存放于此。</summary>
    public string Directory { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastStartedAt { get; set; }

    /// <summary>累计运行秒数。</summary>
    public long TotalRuntimeSeconds { get; set; }

    // ---------- 系统 ----------
    /// <summary>CPU / 内存 / 固件等系统级设置。</summary>
    public SystemConfig Hardware { get; set; } = new();

    // ---------- 存储 ----------
    public List<VmDisk> Disks { get; set; } = new();

    /// <summary>光驱中的 ISO 镜像路径，为空表示未挂载。</summary>
    public string? IsoPath { get; set; }

    /// <summary>第二个光驱，通常用于挂载驱动盘（如 virtio-win.iso）。</summary>
    public string? SecondaryIsoPath { get; set; }

    // ---------- 显示 ----------
    public DisplayConfig Display { get; set; } = new();

    // ---------- 网络 ----------
    public NetworkConfig Network { get; set; } = new();

    // ---------- 音频 ----------
    public AudioConfig Audio { get; set; } = new();

    // ---------- 共享文件夹 ----------
    public SharedFolderConfig SharedFolder { get; set; } = new();

    // ---------- 高级 ----------
    /// <summary>附加到 QEMU 命令行末尾的自定义参数。</summary>
    public string ExtraArguments { get; set; } = string.Empty;

    /// <summary>关闭主窗口时是否一并关闭正在运行的虚拟机。</summary>
    public bool StopOnExit { get; set; } = true;

    // ---------- 运行时（不参与持久化）----------
    [JsonIgnore] public VmState State { get; set; } = VmState.Stopped;

    [JsonIgnore] public int? ProcessId { get; set; }

    [JsonIgnore] public int QmpPort { get; set; }

    [JsonIgnore] public string ConfigPath => Path.Combine(Directory, "machine.json");

    [JsonIgnore] public string SnapshotDirectory => Path.Combine(Directory, "snapshots");

    [JsonIgnore] public string LogPath => Path.Combine(Directory, "logs", "qemu.log");

    /// <summary>主磁盘（列表中的第一块）。</summary>
    [JsonIgnore]
    public VmDisk? PrimaryDisk => Disks.Count > 0 ? Disks[0] : null;

    public VirtualMachine Clone()
    {
        var json = JsonHelper.Serialize(this);
        return JsonHelper.Deserialize<VirtualMachine>(json)!;
    }
}

/// <summary>CPU / 内存 / 固件等系统级设置。</summary>
public sealed class SystemConfig
{
    /// <summary>虚拟 CPU 核心总数。</summary>
    public int CpuCores { get; set; } = 2;

    /// <summary>每核线程数（超线程）。</summary>
    public int ThreadsPerCore { get; set; } = 1;

    /// <summary>内存大小（MB）。</summary>
    public int MemoryMb { get; set; } = 4096;

    public FirmwareType Firmware { get; set; } = FirmwareType.Uefi;

    public AccelMode Accel { get; set; } = AccelMode.Auto;

    /// <summary>CPU 型号字符串，传给 -cpu。</summary>
    public string CpuModel { get; set; } = "max";

    /// <summary>启动设备顺序。</summary>
    public List<BootDevice> BootOrder { get; set; } = new() { BootDevice.CdRom, BootDevice.HardDisk };

    /// <summary>开机时显示 QEMU 引导菜单。</summary>
    public bool ShowBootMenu { get; set; }

    /// <summary>启用 RTC 使用本地时间（Windows 客户机需要）。</summary>
    public bool RtcLocalTime { get; set; } = true;

    /// <summary>启用 Hyper-V 增强（对 Windows 客户机有明显性能提升）。</summary>
    public bool HyperVEnlightenments { get; set; } = true;

    /// <summary>指针设备。</summary>
    public PointerDevice Pointer { get; set; } = PointerDevice.UsbTablet;
}

/// <summary>显示相关设置。</summary>
public sealed class DisplayConfig
{
    public VideoModel Video { get; set; } = VideoModel.Std;

    public DisplayBackend Backend { get; set; } = DisplayBackend.Sdl;

    /// <summary>显存大小（MB），仅对 std/qxl 有效。</summary>
    public int VideoMemoryMb { get; set; } = 64;

    /// <summary>启动时全屏。</summary>
    public bool FullScreen { get; set; }

    /// <summary>VNC 监听端口偏移（Backend = Vnc 时生效），实际端口为 5900 + 该值。</summary>
    public int VncDisplayNumber { get; set; } = 1;
}

/// <summary>网络设置。</summary>
public sealed class NetworkConfig
{
    public NetworkMode Mode { get; set; } = NetworkMode.Nat;

    public NicModel Model { get; set; } = NicModel.E1000;

    /// <summary>自定义 MAC 地址，为空则由 QEMU 自动生成。</summary>
    public string? MacAddress { get; set; }

    /// <summary>桥接模式使用的 TAP 网卡名称。</summary>
    public string? TapInterfaceName { get; set; }

    /// <summary>NAT 端口转发规则。</summary>
    public List<PortForward> PortForwards { get; set; } = new();
}

/// <summary>NAT 端口转发规则。</summary>
public sealed class PortForward
{
    public string Name { get; set; } = "规则";
    public string Protocol { get; set; } = "tcp"; // tcp / udp
    public int HostPort { get; set; } = 2222;
    public int GuestPort { get; set; } = 22;
}

/// <summary>音频设置。</summary>
public sealed class AudioConfig
{
    public AudioBackend Backend { get; set; } = AudioBackend.DirectSound;
    public SoundCard Card { get; set; } = SoundCard.IntelHda;
}

/// <summary>
/// 共享文件夹。Windows 主机上通过 QEMU 的 VVFAT 驱动实现，
/// 客户机中会看到一块额外的可读写磁盘。
/// </summary>
public sealed class SharedFolderConfig
{
    public bool Enabled { get; set; }

    /// <summary>主机侧目录。</summary>
    public string HostPath { get; set; } = string.Empty;

    /// <summary>是否允许客户机写入。</summary>
    public bool Writable { get; set; } = true;
}

/// <summary>共享的 JSON 序列化帮助器。</summary>
public static class JsonHelper
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, Options);
}
