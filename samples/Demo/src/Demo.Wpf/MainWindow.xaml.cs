using Demo.Shared;

namespace Demo.Wpf;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Message.Text = DemoCatalog.Current.DisplayName;
    }
}
