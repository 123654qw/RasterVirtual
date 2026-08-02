using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RasterVirtual;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show(
            $"发生了一个未处理的错误：\n\n{e.Exception.Message}\n\n详细信息已写入日志文件。",
            "Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Models.AppSettings.ConfigDirectory;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "crash.log");
            File.AppendAllText(file,
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 写日志失败时静默处理
        }
    }
}
