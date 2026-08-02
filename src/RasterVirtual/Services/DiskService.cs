using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using RasterVirtual.Models;

namespace RasterVirtual.Services;

/// <summary>封装 qemu-img，负责虚拟磁盘的创建、检查、扩容与快照查询。</summary>
public sealed partial class DiskService
{
    private readonly QemuLocator _locator;

    public DiskService(QemuLocator locator) => _locator = locator;

    private string RequireImgBinary()
    {
        var path = _locator.ImageBinaryPath;
        if (path is null || !File.Exists(path))
            throw new InvalidOperationException("未找到 qemu-img.exe，无法进行磁盘操作。");
        return path;
    }

    /// <summary>创建一块新的虚拟硬盘。</summary>
    public async Task<(bool ok, string message)> CreateDiskAsync(
        string filePath, DiskFormat format, int sizeGb, bool preallocate = false)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(filePath))
                return (false, "目标文件已存在：" + filePath);

            var fmt = format switch
            {
                DiskFormat.Raw => "raw",
                DiskFormat.Vmdk => "vmdk",
                DiskFormat.Vhdx => "vhdx",
                DiskFormat.Vdi => "vdi",
                _ => "qcow2"
            };

            var args = $"create -f {fmt}";

            if (format == DiskFormat.Qcow2)
            {
                // 关闭延迟刷新可以提升数据安全性；预分配可换取更好的写入性能
                args += preallocate ? " -o preallocation=metadata" : " -o preallocation=off";
            }

            args += $" \"{filePath}\" {sizeGb}G";

            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), args, TimeSpan.FromMinutes(10));

            if (!result.Success)
                return (false, result.Combined.Trim());

            return (true, $"已创建 {sizeGb} GB 虚拟硬盘。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>查询磁盘信息。</summary>
    public async Task<DiskInfo?> GetInfoAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), $"info --output=json \"{filePath}\"", TimeSpan.FromSeconds(30));

            if (!result.Success) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
            var root = doc.RootElement;

            long virtualSize = root.TryGetProperty("virtual-size", out var vs) ? vs.GetInt64() : 0;
            long actualSize = root.TryGetProperty("actual-size", out var acs) ? acs.GetInt64() : 0;
            var fmt = root.TryGetProperty("format", out var f) ? f.GetString() ?? "unknown" : "unknown";

            return new DiskInfo(filePath, fmt, virtualSize, actualSize);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>扩容磁盘（只能变大）。</summary>
    public async Task<(bool ok, string message)> ResizeAsync(string filePath, int newSizeGb)
    {
        try
        {
            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), $"resize \"{filePath}\" {newSizeGb}G", TimeSpan.FromMinutes(5));

            return result.Success
                ? (true, $"磁盘已扩容至 {newSizeGb} GB。请在客户机内扩展分区后才能真正生效。")
                : (false, result.Combined.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>转换磁盘格式。</summary>
    public async Task<(bool ok, string message)> ConvertAsync(
        string sourcePath, string targetPath, DiskFormat targetFormat)
    {
        try
        {
            var fmt = targetFormat switch
            {
                DiskFormat.Raw => "raw",
                DiskFormat.Vmdk => "vmdk",
                DiskFormat.Vhdx => "vhdx",
                DiskFormat.Vdi => "vdi",
                _ => "qcow2"
            };

            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(),
                $"convert -p -O {fmt} \"{sourcePath}\" \"{targetPath}\"",
                TimeSpan.FromHours(2));

            return result.Success
                ? (true, "格式转换完成。")
                : (false, result.Combined.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>在关机状态下读取磁盘内的快照列表。</summary>
    public async Task<List<VmSnapshot>> ListSnapshotsAsync(string filePath)
    {
        var list = new List<VmSnapshot>();
        try
        {
            if (!File.Exists(filePath)) return list;

            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), $"snapshot -l \"{filePath}\"", TimeSpan.FromSeconds(60));

            if (!result.Success) return list;

            foreach (var raw in result.StandardOutput.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("ID", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.TrimStart().StartsWith("Snapshot list", StringComparison.OrdinalIgnoreCase)) continue;

                var m = SnapshotLineRegex().Match(line);
                if (!m.Success) continue;

                var tag = m.Groups["tag"].Value.Trim();
                var size = m.Groups["size"].Value.Trim();
                var date = m.Groups["date"].Value.Trim();

                DateTime.TryParse(date, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var created);

                list.Add(new VmSnapshot
                {
                    Tag = tag,
                    DisplayName = tag,
                    SizeText = size,
                    CreatedAt = created == default ? DateTime.Now : created,
                    IncludesMemory = true
                });
            }
        }
        catch
        {
            // 读取失败时返回空列表
        }

        return list;
    }

    /// <summary>在关机状态下创建快照（仅磁盘状态，不含内存）。</summary>
    public async Task<(bool ok, string message)> CreateSnapshotOfflineAsync(string filePath, string tag)
    {
        try
        {
            if (!File.Exists(filePath))
                return (false, "磁盘文件不存在：" + filePath);

            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), $"snapshot -c \"{tag}\" \"{filePath}\"", TimeSpan.FromMinutes(10));

            return result.Success
                ? (true, "快照已创建（仅磁盘状态）。")
                : (false, result.Combined.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>在关机状态下删除快照。</summary>
    public async Task<(bool ok, string message)> DeleteSnapshotOfflineAsync(string filePath, string tag)
    {
        try
        {
            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), $"snapshot -d \"{tag}\" \"{filePath}\"", TimeSpan.FromMinutes(5));

            return result.Success ? (true, "快照已删除。") : (false, result.Combined.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>在关机状态下恢复快照。</summary>
    public async Task<(bool ok, string message)> RestoreSnapshotOfflineAsync(string filePath, string tag)
    {
        try
        {
            var result = await ProcessRunner.RunAndCaptureAsync(
                RequireImgBinary(), $"snapshot -a \"{tag}\" \"{filePath}\"", TimeSpan.FromMinutes(10));

            return result.Success ? (true, "已恢复到该快照。") : (false, result.Combined.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // qemu-img snapshot -l 的输出形如：
    // 1         snap-1                  1.2 GiB 2026-08-02 12:00:00   00:01:23.456
    [GeneratedRegex(@"^\s*(?<id>\S+)\s+(?<tag>\S+)\s+(?<size>[\d.]+\s*\w+)\s+(?<date>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})")]
    private static partial Regex SnapshotLineRegex();
}

public sealed record DiskInfo(string Path, string Format, long VirtualSizeBytes, long ActualSizeBytes)
{
    public string VirtualSizeText => FormatBytes(VirtualSizeBytes);
    public string ActualSizeText => FormatBytes(ActualSizeBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double len = bytes;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }
}
