using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace RasterVirtual.Services;

/// <summary>
/// 在线更新服务：比较本地/远程版本、下载客户端压缩包、解压并自更新（退出后覆盖 + 重启）。
/// </summary>
public sealed class UpdateService
{
    public const string VersionUrl =
        "https://lix-uix.bj.bcebos.com/Raster%20Virtual/Version.txt";

    public const string ClientZipUrl =
        "https://lix-uix.bj.bcebos.com/Raster%20Virtual/RasterVirtual-Client.zip";

    /// <summary>本地版本标识（exe 同目录下的 Version.txt，Trim 后）。</summary>
    public static string LocalVersion
    {
        get
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Version.txt");
                if (File.Exists(path)) return File.ReadAllText(path).Trim();
            }
            catch
            {
                // 读不到就当作无版本
            }
            return string.Empty;
        }
    }

    /// <summary>从服务器拉取最新版本标识（Trim 后）。</summary>
    public static async Task<string> FetchRemoteVersionAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var text = await http.GetStringAsync(VersionUrl, ct);
        return text.Trim();
    }

    /// <summary>是否有可用更新（本地与远程哈希不一致即视为有更新）。</summary>
    public static async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var remote = await FetchRemoteVersionAsync(ct);
        var local = LocalVersion;
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(local))
            return false;
        return !string.Equals(local, remote, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>下载客户端 zip 到临时目录，并通过 progress 汇报 0~100 的进度。</summary>
    public static async Task<string> DownloadClientZipAsync(IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "RasterVirtualUpdate");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "RasterVirtual-Client.zip");

        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var resp = await http.GetAsync(ClientZipUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(zipPath);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report(read * 100.0 / total);
        }
        progress?.Report(100);
        return zipPath;
    }

    /// <summary>把客户端 zip 解压到临时目录（覆盖既有内容）。返回解压目录。</summary>
    public static string ExtractAndStage(string zipPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), "RasterVirtualUpdate", "extracted");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);
        return extractDir;
    }

    /// <summary>
    /// 生成自更新批处理并启动它，随后立即退出本进程。
    /// 批处理会等待本进程（按 PID）退出后，把解压目录整体覆盖到 exe 所在目录，并重启主程序。
    /// </summary>
    public static void LaunchUpdaterAndExit(string extractDir)
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd('\\');
        var pid = Environment.ProcessId;
        var updaterDir = Path.Combine(Path.GetTempPath(), "RasterVirtualUpdate");
        Directory.CreateDirectory(updaterDir);
        var bat = Path.Combine(updaterDir, "rv_updater.bat");

        // 用系统默认编码（中文 Windows 为 GBK），保证带中文的路径在批处理中不乱码
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul 2>&1");
        sb.AppendLine($"set PID={pid}");
        sb.AppendLine(":wait");
        sb.AppendLine("tasklist /FI \"PID eq %PID%\" | find \"%PID%\" >nul");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        sb.AppendLine($"xcopy \"{extractDir}\\*\" \"{baseDir}\" /E /Y /Q /I");
        sb.AppendLine($"start \"\" \"{baseDir}\\RasterVirtual.exe\"");
        sb.AppendLine("del \"%~f0\"");
        File.WriteAllText(bat, sb.ToString(), Encoding.Default);

        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        Process.Start(psi);

        // 退出当前进程，把文件锁交给批处理去覆盖
        Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
        Environment.Exit(0);
    }
}
