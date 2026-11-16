using AndroidRssReader.Services;
using AndroidRssReader.Views;

namespace AndroidRssReader;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // The reader opens on the unread articles; showing the articles that were already read is an
        // explicit choice made from the menu, so it does not survive a restart.
        AppServices.Settings.UnreadOnly = true;

        return new Window(new AppShell());
    }
}