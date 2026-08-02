using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using RasterVirtual.Models;

namespace RasterVirtual.Services;

/// <summary>虚拟机会话，封装一次「从开机到关机」的完整生命周期。</summary>
public sealed class VmSession : IDisposable
{
    private readonly QemuLocator _locator;
    private readonly QemuArgsBuilder _argsBuilder;

    private Process? _process;
    private QmpClient? _qmp;
    private StreamWriter? _logWriter;
    private CancellationTokenSource? _monitorCts;
    private DateTime _startedAt;

    public VirtualMachine Machine { get; }

    public VmState State { get; private set; } = VmState.Stopped;

    public int QmpPort { get; private set; }

    public event EventHandler<VmState>? StateChanged;
    public event EventHandler<string>? LogAppended;

    public VmSession(VirtualMachine machine, QemuLocator locator, QemuArgsBuilder argsBuilder)
    {
        Machine = machine;
        _locator = locator;
        _argsBuilder = argsBuilder;
    }

    // ---------------------------------------------------------------

    public async Task<bool> StartAsync(bool accelAvailable)
    {
        if (State is VmState.Running or VmState.Starting or VmState.Paused)
        {
            Log("虚拟机已在运行中。");
            return false;
        }

        if (!_locator.IsAvailable)
        {
            Log("错误：未找到 QEMU 运行时，无法启动虚拟机。");
            SetState(VmState.Faulted);
            return false;
        }

        SetState(VmState.Starting);

        try
        {
            Directory.CreateDirectory(Machine.Directory);
            Directory.CreateDirectory(Path.Combine(Machine.Directory, "logs"));

            QmpPort = FindFreePort();
            Machine.QmpPort = QmpPort;

            var build = _argsBuilder.Build(Machine, accelAvailable, QmpPort);

            foreach (var w in build.Warnings)
                Log("提示：" + w);

            var exe = _locator.SystemBinaryPath!;
            Log("启动命令：" + build.ToDisplayString(exe));

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = _locator.QemuDirectory!
            };

            foreach (var a in build.Arguments)
                psi.ArgumentList.Add(a);

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log("[qemu] " + e.Data); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log("[qemu] " + e.Data); };
            _process.Exited += OnProcessExited;

            OpenLogFile();

            if (!_process.Start())
            {
                Log("错误：QEMU 进程启动失败。");
                SetState(VmState.Faulted);
                return false;
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _startedAt = DateTime.Now;
            Machine.ProcessId = _process.Id;
            Machine.LastStartedAt = _startedAt;

            Log($"QEMU 进程已启动，PID = {_process.Id}，QMP 端口 = {QmpPort}");

            // 连接控制通道
            _qmp = new QmpClient(QmpPort);
            var connected = await _qmp.ConnectAsync(TimeSpan.FromSeconds(15));

            if (_process.HasExited)
            {
                Log($"QEMU 在启动阶段退出，退出码 {_process.ExitCode}。请检查上方日志中的错误信息。");
                SetState(VmState.Faulted);
                return false;
            }

            if (connected)
            {
                Log("控制通道已建立。");
            }
            else
            {
                Log("警告：控制通道连接超时，暂停/快照等功能将不可用，但虚拟机本身正常运行。");
            }

            SetState(VmState.Running);
            StartStateMonitor();
            return true;
        }
        catch (Exception ex)
        {
            Log("启动异常：" + ex.Message);
            SetState(VmState.Faulted);
            return false;
        }
    }

    public async Task PauseAsync()
    {
        if (_qmp is null || !_qmp.IsConnected || State != VmState.Running) return;
        try
        {
            await _qmp.PauseAsync();
            SetState(VmState.Paused);
            Log("虚拟机已暂停。");
        }
        catch (Exception ex)
        {
            Log("暂停失败：" + ex.Message);
        }
    }

    public async Task ResumeAsync()
    {
        if (_qmp is null || !_qmp.IsConnected || State != VmState.Paused) return;
        try
        {
            await _qmp.ResumeAsync();
            SetState(VmState.Running);
            Log("虚拟机已恢复运行。");
        }
        catch (Exception ex)
        {
            Log("恢复失败：" + ex.Message);
        }
    }

    /// <summary>发送 ACPI 关机信号，让客户机自己正常关机。</summary>
    public async Task ShutdownAsync()
    {
        if (State is VmState.Stopped or VmState.Stopping) return;

        SetState(VmState.Stopping);
        Log("已发送关机信号，等待客户机响应……");

        try
        {
            if (_qmp is not null && _qmp.IsConnected)
            {
                if (State == VmState.Paused) await _qmp.ResumeAsync();
                await _qmp.PowerButtonAsync();
            }
            else
            {
                Log("控制通道不可用，改为直接终止进程。");
                await PowerOffAsync();
                return;
            }

            // 给客户机 60 秒完成关机
            var waited = 0;
            while (waited < 60_000 && _process is { HasExited: false })
            {
                await Task.Delay(500);
                waited += 500;
            }

            if (_process is { HasExited: false })
            {
                Log("客户机在 60 秒内未完成关机，将强制断电。");
                await PowerOffAsync();
            }
        }
        catch (Exception ex)
        {
            Log("关机过程出错：" + ex.Message);
            await PowerOffAsync();
        }
    }

    /// <summary>强制断电，等同于拔掉电源线。</summary>
    public async Task PowerOffAsync()
    {
        SetState(VmState.Stopping);
        try
        {
            if (_qmp is not null && _qmp.IsConnected)
            {
                try { await _qmp.QuitAsync(); } catch { /* 进程可能已退出 */ }
                await Task.Delay(500);
            }

            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                Log("虚拟机已强制断电。");
            }
        }
        catch (Exception ex)
        {
            Log("强制断电失败：" + ex.Message);
        }
        finally
        {
            SetState(VmState.Stopped);
        }
    }

    public async Task ResetAsync()
    {
        if (_qmp is null || !_qmp.IsConnected) return;
        try
        {
            await _qmp.ResetAsync();
            Log("已发送重启信号。");
        }
        catch (Exception ex)
        {
            Log("重启失败：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------
    // 快照

    public async Task<(bool ok, string message)> SaveSnapshotAsync(string tag)
    {
        if (_qmp is null || !_qmp.IsConnected)
            return (false, "控制通道不可用，无法在运行状态下创建快照。");

        try
        {
            var output = await _qmp.SaveSnapshotAsync(tag);
            if (!string.IsNullOrWhiteSpace(output) &&
                output.Contains("Error", StringComparison.OrdinalIgnoreCase))
            {
                Log("创建快照失败：" + output.Trim());
                return (false, output.Trim());
            }

            Log($"快照「{tag}」已创建。");
            return (true, "快照创建成功。");
        }
        catch (Exception ex)
        {
            Log("创建快照异常：" + ex.Message);
            return (false, ex.Message);
        }
    }

    public async Task<(bool ok, string message)> RestoreSnapshotAsync(string tag)
    {
        if (_qmp is null || !_qmp.IsConnected)
            return (false, "控制通道不可用。");

        try
        {
            var output = await _qmp.LoadSnapshotAsync(tag);
            if (!string.IsNullOrWhiteSpace(output) &&
                output.Contains("Error", StringComparison.OrdinalIgnoreCase))
                return (false, output.Trim());

            Log($"已恢复到快照「{tag}」。");
            return (true, "恢复成功。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool ok, string message)> DeleteSnapshotAsync(string tag)
    {
        if (_qmp is null || !_qmp.IsConnected)
            return (false, "控制通道不可用。");

        try
        {
            var output = await _qmp.DeleteSnapshotAsync(tag);
            if (!string.IsNullOrWhiteSpace(output) &&
                output.Contains("Error", StringComparison.OrdinalIgnoreCase))
                return (false, output.Trim());

            Log($"已删除快照「{tag}」。");
            return (true, "快照已删除。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string?> CaptureScreenshotAsync(string outputPath)
    {
        if (_qmp is null || !_qmp.IsConnected) return null;
        try
        {
            await _qmp.ScreenshotAsync(outputPath);
            Log("已保存屏幕截图：" + outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            Log("截图失败：" + ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------

    private void StartStateMonitor()
    {
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, CancellationToken.None);
                if (token.IsCancellationRequested) break;

                if (_process is null || _process.HasExited) break;
                if (_qmp is null || !_qmp.IsConnected) continue;

                var status = await _qmp.QueryStatusAsync(token);
                if (status is null) continue;

                var mapped = status switch
                {
                    "running" => VmState.Running,
                    "paused" => VmState.Paused,
                    "suspended" => VmState.Paused,
                    "shutdown" => VmState.Stopping,
                    _ => State
                };

                if (mapped != State && State is not (VmState.Stopping or VmState.Stopped))
                    SetState(mapped);
            }
        }, token);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _monitorCts?.Cancel();

        var elapsed = (DateTime.Now - _startedAt).TotalSeconds;
        if (elapsed > 0 && elapsed < TimeSpan.FromDays(30).TotalSeconds)
            Machine.TotalRuntimeSeconds += (long)elapsed;

        var code = 0;
        try { code = _process?.ExitCode ?? 0; } catch { }

        Log(code == 0
            ? "虚拟机已关闭。"
            : $"虚拟机进程退出，退出码 {code}。");

        Machine.ProcessId = null;
        SetState(code == 0 ? VmState.Stopped : VmState.Stopped);
        CloseLogFile();
    }

    private void SetState(VmState state)
    {
        if (State == state) return;
        State = state;
        Machine.State = state;
        StateChanged?.Invoke(this, state);
    }

    private void OpenLogFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(Machine.LogPath)!;
            Directory.CreateDirectory(dir);
            _logWriter = new StreamWriter(Machine.LogPath, append: false) { AutoFlush = true };
            _logWriter.WriteLine($"=== Raster Virtual 会话日志 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch
        {
            _logWriter = null;
        }
    }

    private void CloseLogFile()
    {
        try { _logWriter?.Dispose(); } catch { }
        _logWriter = null;
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        try { _logWriter?.WriteLine(line); } catch { }
        LogAppended?.Invoke(this, line);
    }

    /// <summary>向系统申请一个空闲的本地端口用于 QMP。</summary>
    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _qmp?.Dispose();
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        CloseLogFile();
    }
}
