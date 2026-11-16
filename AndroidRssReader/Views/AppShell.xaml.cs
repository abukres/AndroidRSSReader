namespace AndroidRssReader.Views;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(FeedsPage), typeof(FeedsPage));
    }
}
