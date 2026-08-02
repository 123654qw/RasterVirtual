using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RasterVirtual.Services;

namespace RasterVirtual.Views;

public partial class UpdateWindow : Window
{
    private readonly string _current;
    private readonly string _latest;
    private readonly Func<bool> _isBusy;

    public UpdateWindow(string current, string latest, Func<bool> isBusy)
    {
        InitializeComponent();
        _current = current;
        _latest = latest;
        _isBusy = isBusy;

        CurrentVersionRun.Text = string.IsNullOrEmpty(current) ? "(未知)" : current;
        LatestVersionRun.Text = string.IsNullOrEmpty(latest) ? "(未知)" : latest;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        // 有虚拟机运行时先阻止，避免覆盖 qemu 进程锁定的文件
        if (_isBusy())
        {
            MessageBox.Show("请先停止所有正在运行的虚拟机，再执行更新。",
                "Raster Virtual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;

        try
        {
            StatusText.Text = "正在下载更新包…";
            var zip = await UpdateService.DownloadClientZipAsync(
                new Progress<double>(p => ProgressBar.Value = p));

            StatusText.Text = "正在解压…";
            ProgressBar.Value = 100;
            var extractDir = UpdateService.ExtractAndStage(zip);

            StatusText.Text = "正在安装并重启…";
            // 该方法会启动自更新批处理并退出本进程，不会返回
            UpdateService.LaunchUpdaterAndExit(extractDir);
        }
        catch (Exception ex)
        {
            StatusText.Text = "更新失败：" + ex.Message;
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e) => Close();
}
