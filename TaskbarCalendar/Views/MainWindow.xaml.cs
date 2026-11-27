using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using TaskbarCalendar.Services;

namespace TaskbarCalendar.Views;

/// <summary>
/// 管理窗口：只保留「关于」，事项与同步都搬到了轻量面板和时光序。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 初始化时回填勾选状态，避免触发 Checked/Unchecked 事件去改文件。
    /// </summary>
    private bool IsLoading;

    public MainWindow()
    {
        IsLoading = true;
        InitializeComponent();

        StartupCheck.IsChecked = StartupShortcut.IsEnabled;
        IsLoading = false;

        DataDirectoryText.Text = GetRelativeDataDirectory();
        DataDirectoryText.ToolTip = AppPaths.DataDirectory;

        string _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        AboutVersionText.Text = $"版本 v{_version}";
    }

    private void StartupCheck_Changed(object _sender, RoutedEventArgs _e)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            if (StartupCheck.IsChecked is true)
            {
                StartupShortcut.Enable();
                StartupHint.Text = "已在启动文件夹创建快捷方式：" + StartupShortcut.ShortcutPath;
            }
            else
            {
                StartupShortcut.Disable();
                StartupHint.Text = "已移除启动文件夹里的快捷方式";
            }
        }
        catch (Exception _ex)
        {
            AppLog.Write($"设置开机启动失败：{_ex.Message}");
            StartupHint.Text = $"设置失败：{_ex.Message}";

            // - 失败时把勾选状态改回真实状态
            IsLoading = true;
            StartupCheck.IsChecked = StartupShortcut.IsEnabled;
            IsLoading = false;
        }
    }

    private void Header_MouseLeftButtonDown(object _sender, MouseButtonEventArgs _e)
    {
        if (_e.LeftButton is MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OpenDataDirectory_Click(object _sender, RoutedEventArgs _e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            Process.Start("explorer.exe", AppPaths.DataDirectory);
        }
        catch (Exception _ex)
        {
            AppLog.Write($"打开数据目录失败：{_ex.Message}");
        }
    }

    private void Exit_Click(object _sender, RoutedEventArgs _e) => Application.Current.Shutdown();

    private void Close_Click(object _sender, RoutedEventArgs _e) => Close();

    private static string GetRelativeDataDirectory()
    {
        // - 便携模式下数据目录就在程序目录里，显示成相对路径；回退到用户目录时仍是绝对路径
        string _relative = Path.GetRelativePath(AppContext.BaseDirectory, AppPaths.DataDirectory);
        return _relative.StartsWith("..", StringComparison.Ordinal) ? AppPaths.DataDirectory : _relative;
    }
}
