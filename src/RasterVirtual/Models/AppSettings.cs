using System.IO;

namespace RasterVirtual.Models;

/// <summary>应用级全局设置，保存在 %AppData%\RasterVirtual\settings.json。</summary>
public sealed class AppSettings
{
    /// <summary>虚拟机默认存放根目录。</summary>
    public string MachinesRoot { get; set; } = DefaultMachinesRoot;

    /// <summary>手动指定的 QEMU 目录（含 qemu-system-x86_64.exe）。为空则自动探测。</summary>
    public string? QemuDirectoryOverride { get; set; }

    /// <summary>上次打开 ISO 的目录，用于文件对话框记忆。</summary>
    public string? LastIsoDirectory { get; set; }

    /// <summary>主窗口尺寸记忆。</summary>
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    /// <summary>关闭主窗口时自动停止所有运行中的虚拟机。</summary>
    public bool StopAllOnExit { get; set; } = true;

    /// <summary>在日志面板中显示完整的 QEMU 命令行。</summary>
    public bool ShowFullCommandLine { get; set; } = true;

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RasterVirtual");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "settings.json");

    public static string DefaultMachinesRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RasterVirtual VMs");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonHelper.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.MachinesRoot))
                        loaded.MachinesRoot = DefaultMachinesRoot;
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回落到默认值，不阻断启动
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigPath, JsonHelper.Serialize(this));
        }
        catch
        {
            // 写入失败不影响运行
        }
    }
}
