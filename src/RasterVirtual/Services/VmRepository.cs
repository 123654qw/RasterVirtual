using System.IO;
using RasterVirtual.Models;

namespace RasterVirtual.Services;

/// <summary>
/// 虚拟机配置仓库。每台虚拟机对应一个独立目录，
/// 目录内包含 machine.json、虚拟硬盘、UEFI 变量与日志。
/// </summary>
public sealed class VmRepository
{
    private readonly AppSettings _settings;

    public VmRepository(AppSettings settings) => _settings = settings;

    public string Root => _settings.MachinesRoot;

    public void EnsureRoot() => Directory.CreateDirectory(Root);

    /// <summary>扫描根目录，载入全部虚拟机定义。</summary>
    public List<VirtualMachine> LoadAll()
    {
        var list = new List<VirtualMachine>();

        try
        {
            EnsureRoot();
            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var configPath = Path.Combine(dir, "machine.json");
                if (!File.Exists(configPath)) continue;

                try
                {
                    var json = File.ReadAllText(configPath);
                    var vm = JsonHelper.Deserialize<VirtualMachine>(json);
                    if (vm is null) continue;

                    // 目录可能被整体移动过，以实际路径为准
                    vm.Directory = dir;
                    vm.State = VmState.Stopped;
                    list.Add(vm);
                }
                catch
                {
                    // 单台虚拟机配置损坏不影响其它虚拟机加载
                }
            }
        }
        catch
        {
            // 根目录不可访问时返回空列表
        }

        return list.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(VirtualMachine vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Directory))
            vm.Directory = Path.Combine(Root, SanitizeName(vm.Name));

        Directory.CreateDirectory(vm.Directory);
        File.WriteAllText(vm.ConfigPath, JsonHelper.Serialize(vm));
    }

    /// <summary>为新虚拟机分配一个不冲突的目录。</summary>
    public string AllocateDirectory(string vmName)
    {
        EnsureRoot();
        var baseName = SanitizeName(vmName);
        var candidate = Path.Combine(Root, baseName);
        var counter = 2;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(Root, $"{baseName} ({counter})");
            counter++;
        }

        return candidate;
    }

    /// <summary>删除虚拟机。deleteFiles 为 true 时连同磁盘一起移入回收站。</summary>
    public (bool ok, string message) Delete(VirtualMachine vm, bool deleteFiles)
    {
        try
        {
            if (!deleteFiles)
            {
                if (File.Exists(vm.ConfigPath)) File.Delete(vm.ConfigPath);
                return (true, "已从列表中移除，磁盘文件保留在原位置。");
            }

            if (Directory.Exists(vm.Directory))
            {
                // 走系统回收站，避免误删无法挽回
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    vm.Directory,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            return (true, "虚拟机及其全部文件已移入回收站。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>把名称转换为安全的目录名。</summary>
    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "虚拟机";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "虚拟机";
        if (cleaned.Length > 80) cleaned = cleaned[..80];

        return cleaned.TrimEnd('.', ' ');
    }
}
