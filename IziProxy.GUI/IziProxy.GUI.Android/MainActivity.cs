using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace IziProxy.GUI.Android;

[Activity(
    Label = "IziProxy.GUI.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
