using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class SettingsPage : AppPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
