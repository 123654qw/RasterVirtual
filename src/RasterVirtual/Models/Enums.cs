namespace RasterVirtual.Models;

/// <summary>客户机操作系统族，用于推荐默认硬件配置。</summary>
public enum GuestOsFamily
{
    WindowsModern,   // Windows 10 / 11
    WindowsLegacy,   // Windows 7 / XP
    Linux,
    BsdOrOther,
    Dos
}

/// <summary>固件类型。</summary>
public enum FirmwareType
{
    Bios,
    Uefi
}

/// <summary>虚拟机运行状态。</summary>
public enum VmState
{
    Stopped,
    Starting,
    Running,
    Paused,
    Stopping,
    Faulted
}

/// <summary>硬件加速模式。</summary>
public enum AccelMode
{
    Auto,   // 优先 WHPX，失败回落 TCG
    Whpx,   // 强制 Windows Hypervisor Platform
    Tcg     // 纯软件翻译，兼容性最好但慢
}

/// <summary>磁盘控制器总线。</summary>
public enum DiskBus
{
    VirtioBlk,
    Sata,
    Ide,
    Nvme
}

/// <summary>虚拟磁盘格式。</summary>
public enum DiskFormat
{
    Qcow2,
    Raw,
    Vmdk,
    Vhdx,
    Vdi
}

/// <summary>网络连接方式。</summary>
public enum NetworkMode
{
    None,        // 无网卡
    Nat,         // 用户模式网络（SLIRP），开箱即用
    Bridged,     // 桥接到物理网卡，需要 TAP 驱动
    HostOnly     // 仅主机（受限 NAT，无外网）
}

/// <summary>虚拟网卡型号。</summary>
public enum NicModel
{
    VirtioNet,
    E1000,
    E1000e,
    Rtl8139,
    Vmxnet3
}

/// <summary>显示适配器型号。</summary>
public enum VideoModel
{
    Std,
    Qxl,
    VirtioGpu,
    Vmware,
    Cirrus
}

/// <summary>显示后端。</summary>
public enum DisplayBackend
{
    Sdl,        // QEMU 自带窗口（默认）
    Gtk,        // 带菜单栏的窗口
    Vnc,        // 无头，通过 VNC 连接
    None        // 完全无显示
}

/// <summary>指针设备类型。</summary>
public enum PointerDevice
{
    UsbTablet,  // 绝对坐标，鼠标不需要抓取
    UsbMouse,   // 相对坐标
    Ps2         // 传统 PS/2
}

/// <summary>音频后端。</summary>
public enum AudioBackend
{
    None,
    DirectSound,
    Sdl
}

/// <summary>声卡型号。</summary>
public enum SoundCard
{
    IntelHda,
    Ac97,
    SoundBlaster16
}

/// <summary>引导设备优先级。</summary>
public enum BootDevice
{
    HardDisk,
    CdRom,
    Network,
    Floppy
}
