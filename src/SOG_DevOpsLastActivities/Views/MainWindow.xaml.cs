using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SOG_DevOpsLastActivities.ViewModels;

namespace SOG_DevOpsLastActivities.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Only a double-click on a configuration opens it, not one on the empty space or the scroll bar.</summary>
    private void OnConfigDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is null) return;
        if (DataContext is MainViewModel { OpenSessionCommand: var open } && open.CanExecute(null)) open.Execute(null);
    }
}
