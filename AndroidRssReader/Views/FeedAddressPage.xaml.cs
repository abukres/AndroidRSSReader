namespace AndroidRssReader.Views;

/// <summary>
/// Asks for a feed address on a page of the app itself. The platform prompt draws its field inside
/// an alert dialog; this page uses a plain <see cref="Entry"/>, so the address is visibly editable
/// and can be pasted into from the clipboard.
/// </summary>
public partial class FeedAddressPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _address = new();
    private bool _closed;

    private FeedAddressPage(string title, string message, string accept, string placeholder, string? initialValue)
    {
        InitializeComponent();

        Title = title;
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        AcceptButton.Text = accept;
        ValueEntry.Placeholder = placeholder;
        ValueEntry.Keyboard = Keyboard.Url;
        ValueEntry.Text = initialValue;
        ValueEntry.TextChanged += OnTextChanged;
        ValueEntry.Loaded += OnValueEntryLoaded;
        AcceptButton.IsEnabled = !string.IsNullOrWhiteSpace(initialValue);
    }

    /// <summary>
    /// Shows the page on top of the current one and returns the address it was closed with, or
    /// <c>null</c> when the user cancelled.
    /// </summary>
    public static async Task<string?> ShowAsync(
        string title,
        string message,
        string accept,
        string placeholder,
        string? initialValue = null)
    {
        var page = new FeedAddressPage(title, message, accept, placeholder, initialValue);

        await Shell.Current.Navigation.PushModalAsync(page);

        return await page._address.Task;
    }

    /// <summary>
    /// The field is not attached to the platform window while the page appears, so the caret is
    /// placed at the end of the current address once it is, and typing goes straight into it.
    /// </summary>
    private void OnValueEntryLoaded(object? sender, EventArgs e)
    {
        ValueEntry.CursorPosition = ValueEntry.Text?.Length ?? 0;
        ValueEntry.Focus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // The page is gone without a button being used, i.e. the back gesture dismissed it.
        if (!_closed)
            _address.TrySetResult(null);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) =>
        AcceptButton.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);

    private async void OnPasteClicked(object? sender, EventArgs e)
    {
        var clipboard = (await Clipboard.Default.GetTextAsync())?.Trim();
        if (string.IsNullOrEmpty(clipboard))
            return;

        // The address may be half typed, so the clipboard is inserted at the caret.
        var current = ValueEntry.Text ?? string.Empty;
        var caret = Math.Clamp(ValueEntry.CursorPosition, 0, current.Length);

        ValueEntry.Text = current[..caret] + clipboard + current[caret..];
        ValueEntry.CursorPosition = caret + clipboard.Length;
        ValueEntry.Focus();
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    private async void OnAcceptClicked(object? sender, EventArgs e) => await CloseAsync(ValueEntry.Text);

    private async Task CloseAsync(string? address)
    {
        _closed = true;

        await Navigation.PopModalAsync();

        _address.TrySetResult(address);
    }
}
