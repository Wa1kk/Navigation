using IndoorNav.Pages;

namespace IndoorNav;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("admin", typeof(AdminPage));
    }
}
