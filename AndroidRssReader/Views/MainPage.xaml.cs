using AndroidRssReader.Models;
using AndroidRssReader.Services;
using AndroidRssReader.ViewModels;

namespace AndroidRssReader.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(AppServices.Feeds, AppServices.Settings);
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        var unreadFilterLabel = AppServices.Settings.UnreadOnly
            ? "Show read and unread"
            : "Show unread only";

        var choice = await DisplayActionSheetAsync("Reader", "Cancel", null, "Feeds & unread counts", unreadFilterLabel);

        switch (choice)
        {
            case "Feeds & unread counts":
                await Shell.Current.GoToAsync(nameof(FeedsPage));
                break;
            case "Show unread only":
            case "Show read and unread":
                await _viewModel.ToggleUnreadOnlyAsync();
                break;
        }
    }

    private async void OnImportOpmlClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await AppServices.Opml.PickAndImportAsync();
            if (result is null)
                return;

            if (result.ImportedCount == 0)
                await DisplayAlertAsync("Nothing new to import", result.Summary, "OK");

            await _viewModel.RefreshAsync();
        }
        catch (InvalidDataException exception)
        {
            await DisplayAlertAsync("Import failed", exception.Message, "OK");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Import failed", "The OPML file could not be imported: " + exception.Message, "OK");
        }
    }

    private async void OnRefreshLatestClicked(object? sender, EventArgs e)
    {
        // The bottom refresh button gets the latest articles and leaves the reader on the unread list.
        await _viewModel.RefreshAsync(showUnreadOnly: true);
    }

    private async void OnMarkAllReadClicked(object? sender, EventArgs e)
    {
        var scope = AppServices.Settings.HasSelectedFeed
            ? $"every article of '{AppServices.Settings.SelectedFeedTitle}'"
            : "every article";

        var confirmed = await DisplayAlertAsync(
            "Mark all as read",
            $"Mark {scope} as read?",
            "Mark as read",
            "Cancel");

        if (confirmed)
            await _viewModel.MarkAllAsReadAsync();
    }

    private async void OnArticleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Article article)
            return;

        if (sender is CollectionView collectionView)
            collectionView.SelectedItem = null;

        // Opening an article counts as reading it, which clears its unread marker.
        await _viewModel.MarkReadAsync(article);

        if (string.IsNullOrWhiteSpace(article.Link))
            return;

        await Launcher.Default.OpenAsync(article.Link);
    }
}
