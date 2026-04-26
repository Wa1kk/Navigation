using IndoorNav.ViewModels;

namespace IndoorNav.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        Services.EdgeColorService.SetEdgeColor(this, "#FFFFFF");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Services.EdgeColorService.SetEdgeColor(this, "#FFFFFF");
    }

    private void OnUsernameCompleted(object? sender, EventArgs e)
    {
        PasswordEntry.Focus();
    }

    private void OnPasswordCompleted(object? sender, EventArgs e)
    {
        if (BindingContext is not LoginViewModel vm)
            return;

        vm.Username = UsernameEntry.Text ?? string.Empty;
        vm.Password = PasswordEntry.Text ?? string.Empty;

        if (vm.LoginCommand.CanExecute(null))
            vm.LoginCommand.Execute(null);
    }
}
