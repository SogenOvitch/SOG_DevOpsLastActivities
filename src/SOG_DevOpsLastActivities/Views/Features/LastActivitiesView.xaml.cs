using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SOG_DevOpsLastActivities.ViewModels.Features;

namespace SOG_DevOpsLastActivities.Views.Features;

public partial class LastActivitiesView : UserControl
{
    public LastActivitiesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the row's work item. Only a double-click on a row counts: one on a header (sorting) or
    /// a scroll bar would otherwise open whatever row happens to be selected.
    /// </summary>
    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((DataGrid)sender, (DependencyObject)e.OriginalSource) is not DataGridRow row) return;
        if (DataContext is LastActivitiesViewModel { OpenWorkItemCommand: var open } && open.CanExecute(row.Item)) open.Execute(row.Item);
    }
}
