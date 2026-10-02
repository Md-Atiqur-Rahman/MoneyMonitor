using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class AccountsPage : AppPage
{
    public AccountsPage(AccountsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
