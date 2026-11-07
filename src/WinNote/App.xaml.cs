using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace WinNote;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "WinNote.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            MessageBox.Show(
                "备忘录已经在运行中，请从任务栏切换到已打开的窗口。",
                "备忘录",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                try
                {
                    MessageBox.Show(ex.ToString(), "备忘录", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // ignored
                }
            }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch
        {
            // ignored
        }

        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "备忘录", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
