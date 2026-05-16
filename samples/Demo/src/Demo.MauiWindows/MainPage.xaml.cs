using Demo.Shared;

namespace Demo.MauiWindows;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Message.Text = DemoCatalog.Current.DisplayName;
    }
}
