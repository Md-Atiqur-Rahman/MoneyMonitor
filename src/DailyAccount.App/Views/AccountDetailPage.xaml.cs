using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class AccountDetailPage : AppPage
{
    public AccountDetailPage(AccountDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
