using Avalonia.Controls;
using Demo.Shared;

namespace Demo.Avalonia;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.FindControl<TextBlock>("Message")!.Text = DemoCatalog.Current.DisplayName;
    }
}
