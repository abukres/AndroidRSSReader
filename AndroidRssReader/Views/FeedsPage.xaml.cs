using AndroidRssReader.Models;
using AndroidRssReader.Services;
using AndroidRssReader.ViewModels;

namespace AndroidRssReader.Views;

public partial class FeedsPage : ContentPage
{
    private readonly FeedsViewModel _viewModel;

    public FeedsPage()
    {
        InitializeComponent();

        _viewModel = new FeedsViewModel(AppServices.Feeds, AppServices.Opml, AppServices.OpmlExport, AppServices.Settings);
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // The cached rows render straight away; the subscriptions are then downloaded once per
        // visit without an indicator.
        await _viewModel.LoadAsync();
        await _viewModel.RefreshOnEnterAsync();
    }

    private async void OnFeedTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Feed feed)
            return;

        AppServices.Settings.SelectFeed(feed.Url, feed.Title);
        await Shell.Current.GoToAsync("..");
    }

    private async void OnAllFeedsTapped(object? sender, TappedEventArgs e)
    {
        AppServices.Settings.SelectFeed(null, null);
        await Shell.Current.GoToAsync("..");
    }

    private async void OnReadUnreadClicked(object? sender, EventArgs e)
    {
        _viewModel.ShowUnreadArticles();
        await Shell.Current.GoToAsync("..");
    }

    private async void OnFeedMoreClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Feed feed })
            return;

        var choice = await DisplayActionSheetAsync(
            feed.Title,
            "Cancel",
            null,
            "Mark as read",
            "Copy feed address",
            "Edit feed address",
            "Remove feed");

        switch (choice)
        {
            case "Mark as read":
                await _viewModel.MarkFeedAsReadAsync(feed);
                break;
            case "Copy feed address":
                await _viewModel.CopyFeedUrlAsync(feed);
                break;
            case "Edit feed address":
                var address = await FeedAddressPage.ShowAsync(
                    "Edit feed address",
                    $"Address of '{feed.Title}'",
                    "Save",
                    "https://example.com/feed.xml",
                    feed.Url);

                if (address is null)
                    break;

                await SaveFeedAddressAsync(feed, address);

                break;
            case "Remove feed":
                var confirmed = await DisplayAlertAsync(
                    "Remove feed",
                    $"Stop following '{feed.Title}'?",
                    "Remove",
                    "Cancel");

                if (confirmed)
                    await _viewModel.RemoveFeedAsync(feed);

                break;
        }
    }

    /// <summary>
    /// Saves the address of a subscription. When the address is already used by another
    /// subscription, the edit is offered as a merge of the two rows instead of being rejected: the
    /// same feed ends up in the list twice, e.g. once with http and once with https, after an OPML
    /// import, and correcting one of them towards the other is exactly what the user is doing.
    /// </summary>
    private async Task SaveFeedAddressAsync(Feed feed, string address)
    {
        var updated = await _viewModel.EditFeedUrlAsync(feed, address);

        if (updated is null && _viewModel.DuplicateTitle is { } duplicate)
        {
            var merge = await DisplayAlertAsync(
                "Feed already subscribed",
                $"'{duplicate}' already uses that address. Merge the two subscriptions into one?",
                "Merge",
                "Cancel");

            if (!merge)
                return;

            updated = await _viewModel.EditFeedUrlAsync(feed, address, mergeDuplicate: true);
        }

        if (updated is null && _viewModel.IsStatusError)
            await DisplayAlertAsync("Could not save the feed address", _viewModel.StatusText, "OK");
    }

    private async void OnAddFeedClicked(object? sender, EventArgs e)
    {
        var address = await FeedAddressPage.ShowAsync(
            "Add feed",
            "Feed address",
            "Add",
            "https://example.com/feed.xml");

        if (address is null)
            return;

        if (await _viewModel.AddFeedAsync(address) is null && _viewModel.IsStatusError)
            await DisplayAlertAsync("Could not add the feed", _viewModel.StatusText, "OK");
    }

    private async void OnImportOpmlClicked(object? sender, EventArgs e)
    {
        await _viewModel.ImportOpmlAsync();
    }

    private async void OnExportOpmlClicked(object? sender, EventArgs e)
    {
        await _viewModel.ExportOpmlAsync();
    }
}
