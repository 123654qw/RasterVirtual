using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RasterVirtual.Models;

namespace RasterVirtual.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool b || !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool b || !b;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>把内存 MB 数值转成便于阅读的文本。</summary>
public sealed class MemoryTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int mb)
            return mb >= 1024 ? $"{mb / 1024.0:0.#} GB" : $"{mb} MB";
        if (value is double d)
        {
            var v = (int)d;
            return v >= 1024 ? $"{v / 1024.0:0.#} GB" : $"{v} MB";
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>枚举 -> 中文描述。</summary>
public sealed class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? string.Empty : EnumText.Describe(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>集中管理枚举的中文显示名。</summary>
public static class EnumText
{
    public static string Describe(object value) => value switch
    {
        GuestOsFamily.WindowsModern => "Windows 10 / 11",
        GuestOsFamily.WindowsLegacy => "Windows 7 / XP",
        GuestOsFamily.Linux => "Linux",
        GuestOsFamily.BsdOrOther => "BSD 或其它系统",
        GuestOsFamily.Dos => "DOS",

        FirmwareType.Bios => "传统 BIOS",
        FirmwareType.Uefi => "UEFI",

        AccelMode.Auto => "自动（优先硬件加速）",
        AccelMode.Whpx => "强制硬件加速 WHPX",
        AccelMode.Tcg => "纯软件模拟 TCG",

        DiskBus.VirtioBlk => "VirtIO（性能最佳，需驱动）",
        DiskBus.Sata => "SATA / AHCI（通用）",
        DiskBus.Ide => "IDE（兼容老系统）",
        DiskBus.Nvme => "NVMe（现代固态）",

        DiskFormat.Qcow2 => "qcow2（动态扩展，支持快照）",
        DiskFormat.Raw => "raw（原始镜像，最快）",
        DiskFormat.Vmdk => "vmdk（VMware 格式）",
        DiskFormat.Vhdx => "vhdx（Hyper-V 格式）",
        DiskFormat.Vdi => "vdi（VirtualBox 格式）",

        NetworkMode.None => "不连接",
        NetworkMode.Nat => "网络地址转换（NAT）",
        NetworkMode.Bridged => "桥接网卡",
        NetworkMode.HostOnly => "仅主机",

        NicModel.VirtioNet => "VirtIO（性能最佳，需驱动）",
        NicModel.E1000 => "Intel E1000（通用）",
        NicModel.E1000e => "Intel E1000e",
        NicModel.Rtl8139 => "Realtek RTL8139（老系统）",
        NicModel.Vmxnet3 => "VMXNET3",

        VideoModel.Std => "标准 VGA（通用）",
        VideoModel.Qxl => "QXL（支持动态分辨率）",
        VideoModel.VirtioGpu => "VirtIO GPU（Linux 推荐）",
        VideoModel.Vmware => "VMware SVGA",
        VideoModel.Cirrus => "Cirrus（极老系统）",

        DisplayBackend.Sdl => "独立窗口（SDL）",
        DisplayBackend.Gtk => "带菜单窗口（GTK）",
        DisplayBackend.Vnc => "无头 + VNC 远程",
        DisplayBackend.None => "不显示画面",

        PointerDevice.UsbTablet => "USB 触控板（鼠标无需抓取）",
        PointerDevice.UsbMouse => "USB 鼠标",
        PointerDevice.Ps2 => "PS/2 鼠标",

        AudioBackend.None => "禁用声卡",
        AudioBackend.DirectSound => "DirectSound",
        AudioBackend.Sdl => "SDL",

        SoundCard.IntelHda => "Intel HD Audio",
        SoundCard.Ac97 => "AC97",
        SoundCard.SoundBlaster16 => "Sound Blaster 16",

        BootDevice.HardDisk => "硬盘",
        BootDevice.CdRom => "光驱",
        BootDevice.Network => "网络",
        BootDevice.Floppy => "软驱",

        _ => value.ToString() ?? string.Empty
    };

    /// <summary>为下拉框生成「值 + 中文名」列表。</summary>
    public static List<EnumOption<T>> OptionsFor<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new EnumOption<T>(v, Describe(v))).ToList();
}

public sealed record EnumOption<T>(T Value, string Text) where T : struct, Enum
{
    public override string ToString() => Text;
}
