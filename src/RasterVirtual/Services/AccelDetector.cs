using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RasterVirtual.Services;

/// <summary>硬件加速检测结果。</summary>
public sealed class AccelStatus
{
    /// <summary>CPU 是否支持虚拟化（VT-x / AMD-V）且已在 BIOS 中启用。</summary>
    public bool CpuVirtualizationEnabled { get; init; }

    /// <summary>Windows 虚拟机监控程序平台（WHPX）功能是否已安装。</summary>
    public bool WhpxAvailable { get; init; }

    /// <summary>系统是否运行在 Hyper-V 之上（会独占虚拟化能力）。</summary>
    public bool HyperVPresent { get; init; }

    public string Summary { get; init; } = string.Empty;

    /// <summary>可给用户看的修复建议，无需修复时为空。</summary>
    public string? Advice { get; init; }

    public bool CanAccelerate => WhpxAvailable && CpuVirtualizationEnabled;
}

/// <summary>
/// 检测 Windows 上的虚拟化加速能力。
/// QEMU 在 Windows 上依赖 WHPX（Windows Hypervisor Platform），
/// 该功能需要在「启用或关闭 Windows 功能」中勾选。
/// </summary>
public static class AccelDetector
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessorFeaturePresent(uint processorFeature);

    // PF_VIRT_FIRMWARE_ENABLED = 21：固件层面已启用虚拟化
    private const uint PF_VIRT_FIRMWARE_ENABLED = 21;

    // PF_HYPERVISOR_PRESENT = 22：当前系统运行于 hypervisor 之上
    private const uint PF_HYPERVISOR_PRESENT = 22;

    public static AccelStatus Detect()
    {
        bool virtFirmware;
        bool hyperVPresent;

        try
        {
            virtFirmware = IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED);
            hyperVPresent = IsProcessorFeaturePresent(PF_HYPERVISOR_PRESENT);
        }
        catch
        {
            virtFirmware = false;
            hyperVPresent = false;
        }

        var whpxDll = DetectWhpxLibrary();
        var whpxFeature = DetectWhpxFeatureFlag();
        var whpx = whpxDll && (whpxFeature ?? true);

        // 运行在 hypervisor 之上（例如已开启 Hyper-V / VBS / 内存完整性）时，
        // 固件标志可能读不到，但 WHPX 本身仍然可用
        var cpuOk = virtFirmware || hyperVPresent;

        string summary;
        string? advice = null;

        if (whpx && cpuOk)
        {
            summary = "硬件加速可用（WHPX）";
        }
        else if (!cpuOk)
        {
            summary = "CPU 虚拟化未启用";
            advice = "请重启进入 BIOS/UEFI，开启 Intel VT-x 或 AMD SVM，然后重新启动系统。";
        }
        else if (!whpxDll)
        {
            summary = "未安装 Windows 虚拟机监控程序平台";
            advice = "打开「控制面板 → 程序 → 启用或关闭 Windows 功能」，勾选「Windows 虚拟机监控程序平台」后重启电脑。";
        }
        else
        {
            summary = "Windows 虚拟机监控程序平台已禁用";
            advice = "该功能已安装但处于禁用状态，请在「启用或关闭 Windows 功能」中重新勾选并重启。";
        }

        return new AccelStatus
        {
            CpuVirtualizationEnabled = cpuOk,
            WhpxAvailable = whpx,
            HyperVPresent = hyperVPresent,
            Summary = summary,
            Advice = advice
        };
    }

    private static bool DetectWhpxLibrary()
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, "WinHvPlatform.dll"))
                   && File.Exists(Path.Combine(system32, "WinHvEmulation.dll"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 读取注册表中的 WHPX 组件启用状态。读不到时返回 null（表示未知，不作为否决依据）。
    /// </summary>
    private static bool? DetectWhpxFeatureFlag()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\vmcompute");
            if (key is null) return null;

            var start = key.GetValue("Start");
            if (start is int startValue)
            {
                // 4 = 已禁用
                return startValue != 4;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
