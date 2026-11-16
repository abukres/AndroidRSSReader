using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace AndroidRssReader;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window is not { } window)
            return;

        // The reader is always opened by the user directly, so bring the display up when the device
        // is still asleep; otherwise the launch looks like a blank screen until the power button is
        // pressed. Keep it on as well, so the page never blanks while the user is reading.
        window.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.TurnScreenOn);

        if (OperatingSystem.IsAndroidVersionAtLeast(27))
        {
            SetTurnScreenOn(true);
            SetShowWhenLocked(true);
        }
    }
}
