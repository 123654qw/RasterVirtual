using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RasterVirtual.Services;

/// <summary>
/// QEMU Machine Protocol 客户端。
/// QEMU 以 <c>-qmp tcp:127.0.0.1:PORT,server=on,wait=off</c> 暴露一个 JSON 行协议端口，
/// 通过它可以实现暂停、恢复、软关机、快照、截图等运行时控制。
/// </summary>
public sealed class QmpClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsConnected => _tcp?.Connected == true;

    public QmpClient(int port, string host = "127.0.0.1")
    {
        _port = port;
        _host = host;
    }

    /// <summary>连接并完成能力协商。QEMU 启动需要时间，因此内置重试。</summary>
    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var tcp = new TcpClient();
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(2));
                    await tcp.ConnectAsync(_host, _port, connectCts.Token);
                }

                _tcp = tcp;
                _stream = tcp.GetStream();
                _reader = new StreamReader(_stream, new UTF8Encoding(false));
                _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true };

                // 读取 greeting
                var greeting = await ReadLineWithTimeoutAsync(TimeSpan.FromSeconds(5), ct);
                if (greeting is null) { Cleanup(); continue; }

                // 能力协商
                await _writer.WriteLineAsync("{\"execute\":\"qmp_capabilities\"}");
                var ack = await ReadUntilResponseAsync(TimeSpan.FromSeconds(5), ct);
                if (ack is null) { Cleanup(); continue; }

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                Cleanup();
                await Task.Delay(300, CancellationToken.None);
            }
        }

        return false;
    }

    /// <summary>执行一条 QMP 命令。</summary>
    public async Task<JsonNode?> ExecuteAsync(string command, object? arguments = null, CancellationToken ct = default)
    {
        if (_writer is null || _reader is null || !IsConnected)
            throw new InvalidOperationException("QMP 未连接。");

        await _gate.WaitAsync(ct);
        try
        {
            var payload = new Dictionary<string, object> { ["execute"] = command };
            if (arguments is not null) payload["arguments"] = arguments;

            var json = JsonSerializer.Serialize(payload);
            await _writer.WriteLineAsync(json);

            return await ReadUntilResponseAsync(TimeSpan.FromSeconds(30), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 通过 QMP 代理执行 human monitor 命令。
    /// savevm / loadvm / delvm 这类快照操作目前只有 HMP 提供。
    /// </summary>
    public async Task<string> HumanMonitorAsync(string commandLine, CancellationToken ct = default)
    {
        var result = await ExecuteAsync("human-monitor-command",
            new { command_line = commandLine }, ct);
        return result?["return"]?.GetValue<string>() ?? string.Empty;
    }

    // ---------- 常用操作 ----------

    public Task PauseAsync(CancellationToken ct = default) => ExecuteAsync("stop", null, ct);

    public Task ResumeAsync(CancellationToken ct = default) => ExecuteAsync("cont", null, ct);

    /// <summary>发送 ACPI 关机信号，等价于按下电源键。</summary>
    public Task PowerButtonAsync(CancellationToken ct = default) => ExecuteAsync("system_powerdown", null, ct);

    /// <summary>立即终止虚拟机，相当于拔电源。</summary>
    public Task QuitAsync(CancellationToken ct = default) => ExecuteAsync("quit", null, ct);

    public Task ResetAsync(CancellationToken ct = default) => ExecuteAsync("system_reset", null, ct);

    /// <summary>保存快照（含内存状态，虚拟机需处于运行或暂停态）。</summary>
    public Task<string> SaveSnapshotAsync(string tag, CancellationToken ct = default) =>
        HumanMonitorAsync($"savevm {tag}", ct);

    public Task<string> LoadSnapshotAsync(string tag, CancellationToken ct = default) =>
        HumanMonitorAsync($"loadvm {tag}", ct);

    public Task<string> DeleteSnapshotAsync(string tag, CancellationToken ct = default) =>
        HumanMonitorAsync($"delvm {tag}", ct);

    public Task<string> ListSnapshotsAsync(CancellationToken ct = default) =>
        HumanMonitorAsync("info snapshots", ct);

    /// <summary>把当前画面保存为 PPM 文件。</summary>
    public Task ScreenshotAsync(string filePath, CancellationToken ct = default) =>
        ExecuteAsync("screendump", new { filename = filePath.Replace('\\', '/') }, ct);

    /// <summary>更换光驱中的介质。</summary>
    public Task<string> ChangeCdRomAsync(string deviceId, string isoPath, CancellationToken ct = default) =>
        HumanMonitorAsync($"change {deviceId} \"{isoPath.Replace('\\', '/')}\"", ct);

    /// <summary>查询虚拟机运行状态，例如 running / paused。</summary>
    public async Task<string?> QueryStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ExecuteAsync("query-status", null, ct);
            return result?["return"]?["status"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    // ---------- 内部 ----------

    /// <summary>读取直到拿到一条包含 return 或 error 的应答（跳过异步事件）。</summary>
    private async Task<JsonNode?> ReadUntilResponseAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var line = await ReadLineWithTimeoutAsync(deadline - DateTime.UtcNow, ct);
            if (line is null) return null;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch { continue; }

            if (node is null) continue;

            // event 是 QEMU 主动推送的异步消息，不是命令应答
            if (node["event"] is not null) continue;

            if (node["error"] is not null)
            {
                var desc = node["error"]?["desc"]?.GetValue<string>() ?? "未知 QMP 错误";
                throw new QmpException(desc);
            }

            return node;
        }

        return null;
    }

    private async Task<string?> ReadLineWithTimeoutAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (_reader is null) return null;
        if (timeout <= TimeSpan.Zero) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await _reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Cleanup()
    {
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _reader = null;
        _writer = null;
        _stream = null;
        _tcp = null;
    }

    public void Dispose()
    {
        Cleanup();
        _gate.Dispose();
    }
}

public sealed class QmpException : Exception
{
    public QmpException(string message) : base(message) { }
}
