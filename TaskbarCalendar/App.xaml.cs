using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TaskbarCalendar.Data;
using TaskbarCalendar.Services;
using TaskbarCalendar.Views;
using Screen = System.Windows.Forms.Screen;

namespace TaskbarCalendar;

/// <summary>
/// 应用入口：单实例、按设置决定是否接管任务栏时钟点击，启动时打开设置窗口与右下角提示。
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\TaskbarCalendar.SingleInstance";

    private const string ActivateEventName = @"Local\TaskbarCalendar.Activate";

    private readonly SettingsStore SettingsStore = new();

    private Mutex? InstanceMutex;
    private bool OwnsMutex;
    private AppSettings Settings = new();
    private ClockBoundsMonitor ClockBoundsMonitor = null!;
    private ClockTakeoverService TakeoverService = null!;
    private FlyoutWindow? FlyoutWindow;
    private MainWindow? ManagementWindow;
    private SgxDatabase SgxDatabase = null!;
    private SgxSyncService? SgxSyncService;
    private bool IsPreviewMode;
    private EventWaitHandle? ActivateEvent;
    private volatile bool IsShuttingDown;

    private void App_Startup(object _sender, StartupEventArgs _e)
    {
        // - 旧版本数据在 %LOCALAPPDATA%，首次运行时搬到软件目录
        AppPaths.MigrateFromLegacyDirectory();

        string[] _args = Environment.GetCommandLineArgs();
        IsPreviewMode = _args.Any(_arg => _arg is "--ui-preview");

        // - 开机启动快捷方式带 --silent：不弹管理窗口，只在后台接管时钟
        bool _silent = _args.Any(_arg => _arg is StartupShortcut.SilentArgument);

        // - 界面预览模式：只弹面板看布局，不安装钩子、不接管时钟
        if (IsPreviewMode)
        {
            StartPreview();
            return;
        }

        InstanceMutex = new Mutex(true, MutexName, out bool _created_new);
        OwnsMutex = _created_new;
        if (_created_new is false)
        {
            // - 已有实例：通知它把设置窗口唤到前台，然后自己退出
            SignalExistingInstance();
            Shutdown();
            return;
        }

        StartActivateListener();

        DispatcherUnhandledException += App_DispatcherUnhandledException;

        Settings = SettingsStore.Load();

        // - 启动时建表，保证首次点击前缓存已就绪
        SgxDatabase = new SgxDatabase(AppPaths.DatabaseFile);
        SgxDatabase.Initialize();
        SgxSyncService = new SgxSyncService(Settings, SgxDatabase);
        SgxSyncService.StateChanged += SgxSyncService_StateChanged;

        ClockBoundsMonitor = new ClockBoundsMonitor();
        ClockBoundsMonitor.Start();

        TakeoverService = new ClockTakeoverService(ClockBoundsMonitor, Settings);
        TakeoverService.ClockClicked += TakeoverService_ClockClicked;
        TakeoverService.SetEnabled(true);

        AppLog.Write($"应用启动，接管=开启，方式={Settings.HitTestMode}");

        // - 启动即打开设置窗口；关掉后应用继续常驻，面板照常工作
        if (_silent is false)
        {
            ShowMainWindow();
        }

        SgxSyncService.Start();

        // ! 时钟定位依赖 UI Automation，首帧前先同步一次，避免第一次点击落空
        if (ClockBoundsMonitor.TryGet(out _) is false)
        {
            AppLog.Write("启动时未定位到任务栏时钟，等待周期刷新");
        }
    }

    protected override void OnExit(ExitEventArgs _e)
    {
        IsShuttingDown = true;
        ActivateEvent?.Set();
        ActivateEvent?.Dispose();

        ManagementWindow?.Close();
        SgxSyncService?.Dispose();
        TakeoverService?.Dispose();
        ClockBoundsMonitor?.Dispose();
        // ! 第二个实例没有拿到互斥量，释放会抛异常
        if (OwnsMutex)
        {
            InstanceMutex?.ReleaseMutex();
        }

        InstanceMutex?.Dispose();
        AppLog.Write("应用退出，系统时钟行为已恢复");

        base.OnExit(_e);
    }

    /// <summary>
    /// 监听"再次启动"的信号：唤起设置窗口。
    /// </summary>
    private void StartActivateListener()
    {
        ActivateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        Thread _listener = new(() =>
        {
            while (IsShuttingDown is false)
            {
                ActivateEvent.WaitOne();
                if (IsShuttingDown)
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(ShowMainWindow));
            }
        })
        {
            IsBackground = true,
            Name = "TaskbarCalendarActivateListener"
        };
        _listener.Start();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using EventWaitHandle _existing = EventWaitHandle.OpenExisting(ActivateEventName);
            _existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            MessageBox.Show("任务栏日历已经在运行。", "任务栏日历", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void StartPreview()
    {
        ClockBoundsMonitor = new ClockBoundsMonitor();
        ClockBoundsMonitor.Start();

        AppLog.Write("界面预览模式启动");

        ClockBounds _bounds;
        if (ClockBoundsMonitor.TryGet(out _bounds) is false)
        {
            System.Drawing.Rectangle _work = Screen.PrimaryScreen!.WorkingArea;
            _bounds = new ClockBounds(_work.Right - 1, _work.Bottom, _work.Right, _work.Bottom);
        }

        SgxDatabase = new SgxDatabase(AppPaths.DatabaseFile);
        SgxDatabase.Initialize();

        // - 预览模式也接上同步服务，但不启动定时器，只用于按需取重复实例
        SgxSyncService ??= new SgxSyncService(Settings, SgxDatabase);

        FlyoutWindow = new FlyoutWindow(SgxDatabase, SgxSyncService) { AutoCloseEnabled = false };
        FlyoutWindow.SettingsRequested += FlyoutWindow_SettingsRequested;
        FlyoutWindow.Closed += PreviewFlyout_Closed;
        FlyoutWindow.ShowNearClock(_bounds);
    }

    private void PreviewFlyout_Closed(object? _sender, EventArgs _args)
    {
        // - 预览模式没有常驻入口，面板关闭即退出
        Shutdown();
    }

    private void App_DispatcherUnhandledException(object _sender, DispatcherUnhandledExceptionEventArgs _e)
    {
        AppLog.Write($"未处理异常：{_e.Exception}");
        _e.Handled = true;
    }

    private static void SgxSyncService_StateChanged(object? _sender, string _message)
    {
        AppLog.Write($"时光序状态：{_message}");
    }

    private void TakeoverService_ClockClicked(object? _sender, EventArgs _args)
    {
        ToggleFlyoutAtClock();
    }

    private void TakeoverService_MouseDownAt(object? _sender, MouseDownEventArgs _args)
    {
        if (FlyoutWindow is null)
        {
            return;
        }

        if (FlyoutWindow.ContainsScreenPoint(_args.X, _args.Y))
        {
            return;
        }

        FlyoutWindow.CloseByOutsideClick();
    }

    private void FlyoutWindow_SettingsRequested(object? _sender, EventArgs _args)
    {
        ShowMainWindow();
    }

    /// <summary>
    /// 打开（或激活）管理窗口。关闭窗口不会退出程序。
    /// </summary>
    private void ShowMainWindow()
    {
        if (ManagementWindow is not null)
        {
            ManagementWindow.Activate();
            return;
        }

        ManagementWindow = new MainWindow();
        ManagementWindow.Closed += ManagementWindow_Closed;
        ManagementWindow.Show();
    }

    private void ManagementWindow_Closed(object? _sender, EventArgs _args)
    {
        ManagementWindow = null;
    }

    private void ToggleFlyoutAtClock()
    {
        if (FlyoutWindow is not null)
        {
            FlyoutWindow.Close();
            return;
        }

        if (ClockBoundsMonitor.TryGet(out ClockBounds _bounds) is false)
        {
            AppLog.Write("未定位到任务栏时钟，无法定位面板");
            return;
        }

        FlyoutWindow = new FlyoutWindow(SgxDatabase, SgxSyncService);
        FlyoutWindow.SettingsRequested += FlyoutWindow_SettingsRequested;
        FlyoutWindow.Closed += FlyoutWindow_Closed;

        // - 只在面板打开期间订阅全局点击，平时没有额外开销
        TakeoverService.MouseDownAt += TakeoverService_MouseDownAt;

        FlyoutWindow.ShowNearClock(_bounds);
    }

    private void FlyoutWindow_Closed(object? _sender, EventArgs _args)
    {
        TakeoverService?.MouseDownAt -= TakeoverService_MouseDownAt;
        FlyoutWindow = null;
    }
}
