using System.IO;
using Microsoft.Win32;

namespace RasterVirtual.Services;

/// <summary>
/// 负责定位 QEMU 运行时。查找顺序：
/// 1. 用户在设置中手动指定的目录
/// 2. 程序目录下的 runtime\qemu（随软件内置分发）
/// 3. 程序目录下的 qemu
/// 4. 系统 PATH
/// 5. 常见安装路径与注册表记录
/// </summary>
public sealed class QemuLocator
{
    public const string SystemBinary = "qemu-system-x86_64.exe";
    public const string ImageBinary = "qemu-img.exe";

    private string? _cachedDirectory;

    /// <summary>已解析到的 QEMU 目录，未解析时为 null。</summary>
    public string? QemuDirectory => _cachedDirectory;

    public string? SystemBinaryPath =>
        _cachedDirectory is null ? null : Path.Combine(_cachedDirectory, SystemBinary);

    public string? ImageBinaryPath =>
        _cachedDirectory is null ? null : Path.Combine(_cachedDirectory, ImageBinary);

    public bool IsAvailable => _cachedDirectory is not null && File.Exists(SystemBinaryPath!);

    /// <summary>执行探测。返回是否找到可用的 QEMU。</summary>
    public bool Locate(string? userOverride = null)
    {
        foreach (var candidate in EnumerateCandidates(userOverride))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var probe = Path.Combine(candidate, SystemBinary);
                if (File.Exists(probe))
                {
                    _cachedDirectory = Path.GetFullPath(candidate);
                    return true;
                }
            }
            catch
            {
                // 无效路径，跳过
            }
        }

        _cachedDirectory = null;
        return false;
    }

    private static IEnumerable<string?> EnumerateCandidates(string? userOverride)
    {
        yield return userOverride;

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "runtime", "qemu");
        yield return Path.Combine(baseDir, "qemu");
        yield return baseDir;

        // 开发期：从 bin\Debug\net9.0-windows 回溯到仓库根的 runtime\qemu
        var probe = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && probe is not null; i++)
        {
            yield return Path.Combine(probe.FullName, "runtime", "qemu");
            probe = probe.Parent;
        }

        // PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var p in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return p.Trim().Trim('"');
        }

        // 常见安装位置
        yield return @"C:\Program Files\qemu";
        yield return @"C:\Program Files (x86)\qemu";
        yield return @"C:\qemu";
        yield return @"C:\msys64\mingw64\bin";

        // 注册表（QEMU 官方安装包会写入 Uninstall 项）
        foreach (var fromReg in ReadRegistryPaths())
            yield return fromReg;
    }

    private static IEnumerable<string> ReadRegistryPaths()
    {
        var results = new List<string>();
        string[] roots =
        {
            @"SOFTWARE\QEMU",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QEMU",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\QEMU"
        };

        foreach (var root in roots)
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(root);
                    if (key is null) continue;
                    var value = key.GetValue("InstallLocation") as string
                                ?? key.GetValue("Install_Dir") as string
                                ?? key.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        results.Add(value.Trim('"'));
                }
                catch
                {
                    // 注册表不可读时忽略
                }
            }
        }

        return results;
    }

    /// <summary>读取 QEMU 版本号，失败返回 null。</summary>
    public async Task<string?> GetVersionAsync()
    {
        if (!IsAvailable) return null;
        try
        {
            var output = await ProcessRunner.RunAndCaptureAsync(SystemBinaryPath!, "--version", TimeSpan.FromSeconds(10));
            var firstLine = output.StandardOutput.Split('\n').FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>查找 UEFI 固件文件（edk2 OVMF）。</summary>
    public string? FindUefiFirmware()
    {
        if (_cachedDirectory is null) return null;

        string[] names =
        {
            "edk2-x86_64-code.fd",
            "OVMF_CODE.fd",
            "OVMF.fd",
            "bios-256k.bin" // 兜底：不存在 UEFI 时由调用方降级为 BIOS
        };

        string[] dirs =
        {
            _cachedDirectory,
            Path.Combine(_cachedDirectory, "share"),
            Path.Combine(_cachedDirectory, "share", "qemu"),
            Path.Combine(_cachedDirectory, "firmware")
        };

        foreach (var name in names.Take(3))
        {
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>查找 UEFI 变量存储模板（NVRAM）。</summary>
    public string? FindUefiVarsTemplate()
    {
        if (_cachedDirectory is null) return null;

        string[] names = { "edk2-i386-vars.fd", "OVMF_VARS.fd" };
        string[] dirs =
        {
            _cachedDirectory,
            Path.Combine(_cachedDirectory, "share"),
            Path.Combine(_cachedDirectory, "share", "qemu"),
            Path.Combine(_cachedDirectory, "firmware")
        };

        foreach (var name in names)
        {
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
