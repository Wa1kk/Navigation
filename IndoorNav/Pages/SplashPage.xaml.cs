namespace IndoorNav.Pages;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();

        // Show loading indicator after 2 seconds
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), () =>
        {
            LoadingIndicator.IsRunning = true;
        });
    }
}
