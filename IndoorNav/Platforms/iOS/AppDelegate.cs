using Foundation;
using IndoorNav.Services;
using UIKit;

namespace IndoorNav;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Called when the app is opened via a custom URI scheme (indoornav://node/{id})
    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        if (url.Scheme?.Equals("indoornav", StringComparison.OrdinalIgnoreCase) == true &&
            url.Host?.Equals("node", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Path is  /node/{guid} or just /{guid} depending on iOS version
            var nodeId = (url.Path ?? "").TrimStart('/');
            if (!string.IsNullOrWhiteSpace(nodeId))
                DeepLinkService.RequestNode(nodeId);
        }
        return true;
    }
}
