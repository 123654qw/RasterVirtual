using System.IO;
using System.Text.Json.Serialization;

namespace RasterVirtual.Models;

/// <summary>一块虚拟硬盘。</summary>
public sealed class VmDisk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>磁盘文件路径。相对路径时以虚拟机目录为基准。</summary>
    public string Path { get; set; } = string.Empty;

    public DiskFormat Format { get; set; } = DiskFormat.Qcow2;

    public DiskBus Bus { get; set; } = DiskBus.Sata;

    /// <summary>标称容量（GB），仅作显示用途，真实容量以磁盘文件为准。</summary>
    public int CapacityGb { get; set; } = 64;

    /// <summary>只读挂载。</summary>
    public bool ReadOnly { get; set; }

    /// <summary>启用 SSD 模拟（向客户机上报为固态硬盘）。</summary>
    public bool Ssd { get; set; } = true;

    /// <summary>缓存模式：writeback / writethrough / none / unsafe。</summary>
    public string CacheMode { get; set; } = "writeback";

    /// <summary>丢弃未使用块（TRIM），可让 qcow2 自动瘦身。</summary>
    public bool Discard { get; set; } = true;

    [JsonIgnore]
    public string FileName => string.IsNullOrEmpty(Path) ? string.Empty : System.IO.Path.GetFileName(Path);

    /// <summary>解析为绝对路径。</summary>
    public string ResolvePath(string vmDirectory)
    {
        if (string.IsNullOrWhiteSpace(Path)) return string.Empty;
        return System.IO.Path.IsPathRooted(Path)
            ? Path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(vmDirectory, Path));
    }

    /// <summary>获取磁盘文件在硬盘上的实际占用（字节）；文件不存在返回 0。</summary>
    public long GetActualSizeBytes(string vmDirectory)
    {
        var full = ResolvePath(vmDirectory);
        if (string.IsNullOrEmpty(full)) return 0;
        var fi = new FileInfo(full);
        return fi.Exists ? fi.Length : 0;
    }

    public string FormatToQemuString() => Format switch
    {
        DiskFormat.Qcow2 => "qcow2",
        DiskFormat.Raw => "raw",
        DiskFormat.Vmdk => "vmdk",
        DiskFormat.Vhdx => "vhdx",
        DiskFormat.Vdi => "vdi",
        _ => "qcow2"
    };

    public static string ExtensionFor(DiskFormat format) => format switch
    {
        DiskFormat.Qcow2 => ".qcow2",
        DiskFormat.Raw => ".img",
        DiskFormat.Vmdk => ".vmdk",
        DiskFormat.Vhdx => ".vhdx",
        DiskFormat.Vdi => ".vdi",
        _ => ".qcow2"
    };
}

/// <summary>虚拟机快照记录。</summary>
public sealed class VmSnapshot
{
    /// <summary>QEMU 内部快照标签。</summary>
    public string Tag { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>快照占用的空间描述，例如 "1.2 GiB"。</summary>
    public string SizeText { get; set; } = "—";

    /// <summary>快照建立时虚拟机是否处于运行态（含内存状态）。</summary>
    public bool IncludesMemory { get; set; }
}
