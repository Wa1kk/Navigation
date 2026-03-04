using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using IndoorNav.Services;

namespace IndoorNav;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
          LaunchMode = LaunchMode.SingleTop,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                                 ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                                 ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// Deep-link intent filter — handles  indoornav://node/{nodeId}  URIs.
[IntentFilter(
    new[] { Intent.ActionView },
    Categories    = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme    = "indoornav",
    DataHost      = "node",
    AutoVerify    = false)]
public class MainActivity : MauiAppCompatActivity
{
    // Called when the app is already running and a new Intent arrives
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    // Called when the app is cold-started from a deep link
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleIntent(Intent);
    }

    private static void HandleIntent(Intent? intent)
    {
        if (intent?.Data?.Scheme?.Equals("indoornav", StringComparison.OrdinalIgnoreCase) != true)
            return;

        // Path is  /node/{guid}  → first segment after "node" is the node ID
        var nodeId = intent.Data?.PathSegments?.LastOrDefault();
        if (!string.IsNullOrWhiteSpace(nodeId))
            DeepLinkService.RequestNode(nodeId);
    }
}
