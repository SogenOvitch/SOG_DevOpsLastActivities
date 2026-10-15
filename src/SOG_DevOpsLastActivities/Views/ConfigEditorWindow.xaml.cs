using System.Windows;
using SOG_DevOpsLastActivities.ViewModels;

namespace SOG_DevOpsLastActivities.Views;

public partial class ConfigEditorWindow : Window
{
    public ConfigEditorWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigEditorViewModel { CanSave: true }) DialogResult = true;
    }
}
