using System.Windows;
using System.Windows.Threading;
using SOG_DevOpsLastActivities.Services;
using SOG_DevOpsLastActivities.ViewModels;
using SOG_DevOpsLastActivities.Views;

namespace SOG_DevOpsLastActivities;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var viewModel = new MainViewModel(new ConfigStore());
        var window = new MainWindow { DataContext = viewModel };

        // The view model decides when a configuration needs editing; the window that does it is
        // a view, so it is handed in rather than created by the view model.
        viewModel.ShowConfigEditor = editor =>
            new ConfigEditorWindow { Owner = window, DataContext = editor }.ShowDialog() == true;

        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Expected failures (refused PAT, network, unknown project) are reported where they happen.
    /// Anything that reaches here is a bug; showing it beats the process vanishing.
    /// </summary>
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
