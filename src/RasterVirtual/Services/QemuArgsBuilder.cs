using System.IO;
using System.Text;
using RasterVirtual.Models;

namespace RasterVirtual.Services;

/// <summary>
/// 把 <see cref="VirtualMachine"/> 翻译成 QEMU 命令行参数。
/// 这是整个软件的核心：虚拟机能不能正确开机，全看这里生成的参数。
/// </summary>
public sealed class QemuArgsBuilder
{
    private readonly QemuLocator _locator;

    public QemuArgsBuilder(QemuLocator locator) => _locator = locator;

    /// <summary>构建结果。</summary>
    public sealed record BuildResult(List<string> Arguments, List<string> Warnings)
    {
        /// <summary>用于日志展示的完整命令行。</summary>
        public string ToDisplayString(string exePath)
        {
            var sb = new StringBuilder();
            sb.Append(Quote(exePath));
            foreach (var a in Arguments)
            {
                sb.Append(' ');
                sb.Append(Quote(a));
            }
            return sb.ToString();
        }

        private static string Quote(string s) =>
            s.Contains(' ') || s.Contains('"') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
    }

    public BuildResult Build(VirtualMachine vm, bool accelAvailable, int qmpPort)
    {
        var args = new List<string>();
        var warnings = new List<string>();
        var hw = vm.Hardware;

        // ---------------- 基本身份 ----------------
        args.Add("-name");
        args.Add($"{vm.Name},process=RasterVM");

        // ---------------- 机型 ----------------
        var machineType = ResolveMachineType(vm.OsFamily);
        var accelString = ResolveAccel(hw.Accel, accelAvailable, warnings);

        var machineOpts = new List<string> { machineType, $"accel={accelString}" };
        if (accelString == "whpx")
        {
            // WHPX 目前不支持内核态中断控制器，必须关闭，否则客户机会卡在引导阶段
            machineOpts.Add("kernel-irqchip=off");
        }
        args.Add("-machine");
        args.Add(string.Join(",", machineOpts));

        // ---------------- CPU ----------------
        var cpuModel = string.IsNullOrWhiteSpace(hw.CpuModel) ? "max" : hw.CpuModel.Trim();
        var cpuOpts = new List<string> { cpuModel };

        if (hw.HyperVEnlightenments && IsWindowsGuest(vm.OsFamily) && accelString == "whpx")
        {
            // 这些标志能显著改善 Windows 客户机的调度与计时器表现
            cpuOpts.Add("hv-relaxed");
            cpuOpts.Add("hv-vapic");
            cpuOpts.Add("hv-time");
            cpuOpts.Add("hv-spinlocks=0x1fff");
        }

        args.Add("-cpu");
        args.Add(string.Join(",", cpuOpts));

        // ---------------- SMP ----------------
        var cores = Math.Clamp(hw.CpuCores, 1, 64);
        var threads = Math.Clamp(hw.ThreadsPerCore, 1, 4);
        args.Add("-smp");
        args.Add($"{cores * threads},cores={cores},threads={threads},sockets=1");

        // ---------------- 内存 ----------------
        var memory = Math.Clamp(hw.MemoryMb, 128, 262144);
        args.Add("-m");
        args.Add($"{memory}M");

        // ---------------- 固件 ----------------
        if (hw.Firmware == FirmwareType.Uefi)
        {
            var code = _locator.FindUefiFirmware();
            if (code is null)
            {
                warnings.Add("未找到 UEFI 固件文件（edk2-x86_64-code.fd），已自动降级为传统 BIOS 引导。");
            }
            else
            {
                args.Add("-drive");
                args.Add($"if=pflash,format=raw,unit=0,readonly=on,file={NormalizePath(code)}");

                var vars = EnsureUefiVars(vm, warnings);
                if (vars is not null)
                {
                    args.Add("-drive");
                    args.Add($"if=pflash,format=raw,unit=1,file={NormalizePath(vars)}");
                }
            }
        }

        // ---------------- RTC ----------------
        args.Add("-rtc");
        args.Add(hw.RtcLocalTime ? "base=localtime,clock=host" : "base=utc,clock=host");

        // ---------------- 硬盘 ----------------
        var driveIndex = 0;
        var nvmeCounter = 0;
        foreach (var disk in vm.Disks)
        {
            var full = disk.ResolvePath(vm.Directory);
            if (string.IsNullOrEmpty(full))
                continue;

            if (!File.Exists(full))
            {
                warnings.Add($"磁盘文件不存在，已跳过：{full}");
                continue;
            }

            var opts = new List<string>
            {
                $"file={NormalizePath(full)}",
                $"format={disk.FormatToQemuString()}",
                $"cache={disk.CacheMode}"
            };

            if (disk.Discard) opts.Add("discard=unmap");
            if (disk.ReadOnly) opts.Add("readonly=on");

            switch (disk.Bus)
            {
                case DiskBus.VirtioBlk:
                    opts.Add("if=virtio");
                    opts.Add($"index={driveIndex}");
                    args.Add("-drive");
                    args.Add(string.Join(",", opts));
                    break;

                case DiskBus.Nvme:
                    var nvmeId = $"nvme{nvmeCounter++}";
                    opts.Add("if=none");
                    opts.Add($"id={nvmeId}");
                    args.Add("-drive");
                    args.Add(string.Join(",", opts));
                    args.Add("-device");
                    args.Add($"nvme,drive={nvmeId},serial=RV{nvmeId.ToUpperInvariant()}");
                    break;

                case DiskBus.Sata:
                case DiskBus.Ide:
                default:
                    opts.Add("if=ide");
                    opts.Add("media=disk");
                    opts.Add($"index={driveIndex}");
                    args.Add("-drive");
                    args.Add(string.Join(",", opts));
                    break;
            }

            driveIndex++;
        }

        // ---------------- 光驱 ----------------
        var cdIndex = Math.Max(driveIndex, 2);
        if (!string.IsNullOrWhiteSpace(vm.IsoPath))
        {
            if (File.Exists(vm.IsoPath))
            {
                args.Add("-drive");
                args.Add($"file={NormalizePath(vm.IsoPath)},media=cdrom,if=ide,index={cdIndex},readonly=on");
                cdIndex++;
            }
            else
            {
                warnings.Add($"ISO 镜像不存在，已跳过挂载：{vm.IsoPath}");
            }
        }

        if (!string.IsNullOrWhiteSpace(vm.SecondaryIsoPath))
        {
            if (File.Exists(vm.SecondaryIsoPath))
            {
                args.Add("-drive");
                args.Add($"file={NormalizePath(vm.SecondaryIsoPath)},media=cdrom,if=ide,index={cdIndex},readonly=on");
            }
            else
            {
                warnings.Add($"第二光驱镜像不存在，已跳过：{vm.SecondaryIsoPath}");
            }
        }

        // ---------------- 引导顺序 ----------------
        var bootLetters = new StringBuilder();
        foreach (var dev in hw.BootOrder)
        {
            var c = dev switch
            {
                BootDevice.CdRom => 'd',
                BootDevice.HardDisk => 'c',
                BootDevice.Network => 'n',
                BootDevice.Floppy => 'a',
                _ => '\0'
            };
            if (c != '\0' && !bootLetters.ToString().Contains(c))
                bootLetters.Append(c);
        }
        if (bootLetters.Length == 0) bootLetters.Append("cd");

        args.Add("-boot");
        args.Add($"order={bootLetters},menu={(hw.ShowBootMenu ? "on" : "off")}");

        // ---------------- 显示 ----------------
        BuildDisplay(vm, args, warnings);

        // ---------------- USB 与指针 ----------------
        args.Add("-device");
        args.Add("qemu-xhci,id=xhci");

        switch (hw.Pointer)
        {
            case PointerDevice.UsbTablet:
                args.Add("-device");
                args.Add("usb-tablet,bus=xhci.0");
                break;
            case PointerDevice.UsbMouse:
                args.Add("-device");
                args.Add("usb-mouse,bus=xhci.0");
                break;
            case PointerDevice.Ps2:
                // PS/2 由机型内建，无需额外设备
                break;
        }

        args.Add("-device");
        args.Add("usb-kbd,bus=xhci.0");

        // ---------------- 网络 ----------------
        BuildNetwork(vm, args, warnings);

        // ---------------- 音频 ----------------
        BuildAudio(vm, args);

        // ---------------- 共享文件夹 ----------------
        BuildSharedFolder(vm, args, warnings);

        // ---------------- QMP 控制通道 ----------------
        args.Add("-qmp");
        args.Add($"tcp:127.0.0.1:{qmpPort},server=on,wait=off");

        // ---------------- 其它 ----------------
        args.Add("-monitor");
        args.Add("none");

        // 自定义参数
        if (!string.IsNullOrWhiteSpace(vm.ExtraArguments))
        {
            foreach (var token in TokenizeArguments(vm.ExtraArguments))
                args.Add(token);
        }

        return new BuildResult(args, warnings);
    }

    // ------------------------------------------------------------------

    private void BuildDisplay(VirtualMachine vm, List<string> args, List<string> warnings)
    {
        var d = vm.Display;

        switch (d.Video)
        {
            case VideoModel.VirtioGpu:
                args.Add("-device");
                args.Add("virtio-vga");
                break;
            case VideoModel.Qxl:
                args.Add("-vga");
                args.Add("qxl");
                break;
            case VideoModel.Vmware:
                args.Add("-vga");
                args.Add("vmware");
                break;
            case VideoModel.Cirrus:
                args.Add("-vga");
                args.Add("cirrus");
                break;
            case VideoModel.Std:
            default:
                args.Add("-vga");
                args.Add("std");
                var vram = Math.Clamp(d.VideoMemoryMb, 16, 256);
                args.Add("-global");
                args.Add($"VGA.vgamem_mb={vram}");
                break;
        }

        switch (d.Backend)
        {
            case DisplayBackend.Gtk:
                args.Add("-display");
                args.Add($"gtk,zoom-to-fit=on{(d.FullScreen ? ",full-screen=on" : "")}");
                break;

            case DisplayBackend.Vnc:
                args.Add("-display");
                args.Add("none");
                args.Add("-vnc");
                args.Add($"127.0.0.1:{d.VncDisplayNumber}");
                warnings.Add($"已启用无头模式，请使用 VNC 客户端连接 127.0.0.1:{5900 + d.VncDisplayNumber}");
                break;

            case DisplayBackend.None:
                args.Add("-display");
                args.Add("none");
                break;

            case DisplayBackend.Sdl:
            default:
                args.Add("-display");
                args.Add("sdl");
                if (d.FullScreen) args.Add("-full-screen");
                break;
        }
    }

    private static void BuildNetwork(VirtualMachine vm, List<string> args, List<string> warnings)
    {
        var n = vm.Network;

        if (n.Mode == NetworkMode.None)
        {
            args.Add("-nic");
            args.Add("none");
            return;
        }

        var netdevOpts = new List<string>();
        switch (n.Mode)
        {
            case NetworkMode.Bridged:
                if (string.IsNullOrWhiteSpace(n.TapInterfaceName))
                {
                    warnings.Add("桥接模式未指定 TAP 网卡名称，已自动回落为 NAT 模式。");
                    netdevOpts.Add("user");
                    netdevOpts.Add("id=net0");
                }
                else
                {
                    netdevOpts.Add("tap");
                    netdevOpts.Add("id=net0");
                    netdevOpts.Add($"ifname={n.TapInterfaceName}");
                    netdevOpts.Add("script=no");
                    netdevOpts.Add("downscript=no");
                }
                break;

            case NetworkMode.HostOnly:
                netdevOpts.Add("user");
                netdevOpts.Add("id=net0");
                netdevOpts.Add("restrict=on");
                break;

            case NetworkMode.Nat:
            default:
                netdevOpts.Add("user");
                netdevOpts.Add("id=net0");
                break;
        }

        // 端口转发（仅用户模式网络支持）
        if (n.Mode is NetworkMode.Nat or NetworkMode.HostOnly)
        {
            foreach (var pf in n.PortForwards)
            {
                if (pf.HostPort is <= 0 or > 65535 || pf.GuestPort is <= 0 or > 65535) continue;
                var proto = pf.Protocol?.ToLowerInvariant() == "udp" ? "udp" : "tcp";
                netdevOpts.Add($"hostfwd={proto}::{pf.HostPort}-:{pf.GuestPort}");
            }
        }

        args.Add("-netdev");
        args.Add(string.Join(",", netdevOpts));

        var model = n.Model switch
        {
            NicModel.VirtioNet => "virtio-net-pci",
            NicModel.E1000e => "e1000e",
            NicModel.Rtl8139 => "rtl8139",
            NicModel.Vmxnet3 => "vmxnet3",
            _ => "e1000"
        };

        var deviceOpts = new List<string> { model, "netdev=net0" };
        if (!string.IsNullOrWhiteSpace(n.MacAddress) && IsValidMac(n.MacAddress))
            deviceOpts.Add($"mac={n.MacAddress}");

        args.Add("-device");
        args.Add(string.Join(",", deviceOpts));
    }

    private static void BuildAudio(VirtualMachine vm, List<string> args)
    {
        var a = vm.Audio;
        if (a.Backend == AudioBackend.None) return;

        var backend = a.Backend == AudioBackend.Sdl ? "sdl" : "dsound";
        args.Add("-audiodev");
        args.Add($"{backend},id=snd0");

        switch (a.Card)
        {
            case SoundCard.Ac97:
                args.Add("-device");
                args.Add("AC97,audiodev=snd0");
                break;
            case SoundCard.SoundBlaster16:
                args.Add("-device");
                args.Add("sb16,audiodev=snd0");
                break;
            case SoundCard.IntelHda:
            default:
                args.Add("-device");
                args.Add("intel-hda");
                args.Add("-device");
                args.Add("hda-duplex,audiodev=snd0");
                break;
        }
    }

    private static void BuildSharedFolder(VirtualMachine vm, List<string> args, List<string> warnings)
    {
        var s = vm.SharedFolder;
        if (!s.Enabled) return;

        if (string.IsNullOrWhiteSpace(s.HostPath) || !Directory.Exists(s.HostPath))
        {
            warnings.Add($"共享文件夹路径无效，已跳过：{s.HostPath}");
            return;
        }

        // Windows 主机上通过 VVFAT 暴露为一块可移动磁盘
        var mode = s.Writable ? "rw" : "ro";
        var path = NormalizePath(s.HostPath).TrimEnd('/');

        args.Add("-drive");
        args.Add($"file=fat:{mode}:{path},format=raw,if=none,id=shared0");
        args.Add("-device");
        args.Add("usb-storage,drive=shared0,bus=xhci.0,removable=on");

        if (s.Writable)
            warnings.Add("共享文件夹为 VVFAT 实现：客户机内写入的文件需重启虚拟机后才会同步到主机，且不支持超过 4GB 的单个文件。");
    }

    // ------------------------------------------------------------------

    private static string ResolveMachineType(GuestOsFamily os) => os switch
    {
        GuestOsFamily.WindowsModern => "q35",
        GuestOsFamily.Linux => "q35",
        GuestOsFamily.BsdOrOther => "q35",
        GuestOsFamily.WindowsLegacy => "pc",
        GuestOsFamily.Dos => "pc",
        _ => "q35"
    };

    private static string ResolveAccel(AccelMode mode, bool accelAvailable, List<string> warnings)
    {
        switch (mode)
        {
            case AccelMode.Tcg:
                return "tcg";

            case AccelMode.Whpx:
                if (!accelAvailable)
                    warnings.Add("已强制使用 WHPX，但系统未检测到 Windows 虚拟机监控程序平台，启动可能失败。");
                return "whpx";

            case AccelMode.Auto:
            default:
                if (accelAvailable) return "whpx";
                warnings.Add("未检测到硬件加速（WHPX），本次将使用纯软件模拟（TCG），运行速度会明显下降。");
                return "tcg";
        }
    }

    private static bool IsWindowsGuest(GuestOsFamily os) =>
        os is GuestOsFamily.WindowsModern or GuestOsFamily.WindowsLegacy;

    /// <summary>
    /// UEFI 需要一份可写的变量存储。首次启动时从模板复制一份到虚拟机目录。
    /// </summary>
    private string? EnsureUefiVars(VirtualMachine vm, List<string> warnings)
    {
        try
        {
            var target = Path.Combine(vm.Directory, "efi_vars.fd");
            if (File.Exists(target)) return target;

            var template = _locator.FindUefiVarsTemplate();
            if (template is null)
            {
                warnings.Add("未找到 UEFI 变量模板（edk2-i386-vars.fd），客户机的启动项设置将无法保存。");
                return null;
            }

            Directory.CreateDirectory(vm.Directory);
            File.Copy(template, target);
            return target;
        }
        catch (Exception ex)
        {
            warnings.Add($"创建 UEFI 变量存储失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// QEMU 在 Windows 上同时接受正斜杠与反斜杠，但正斜杠可以避免转义歧义。
    /// 另外 QEMU 的 opts 解析中逗号需要写成两个。
    /// </summary>
    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Replace(",", ",,");

    private static bool IsValidMac(string mac)
    {
        var parts = mac.Split(':', '-');
        return parts.Length == 6 && parts.All(p => p.Length == 2 &&
            p.All(c => Uri.IsHexDigit(c)));
    }

    /// <summary>把用户输入的自定义参数拆分为 token，支持双引号包裹。</summary>
    public static IEnumerable<string> TokenizeArguments(string input)
    {
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }
}
