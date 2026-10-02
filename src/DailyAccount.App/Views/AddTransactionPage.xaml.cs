using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class AddTransactionPage : AppPage
{
    public AddTransactionPage(AddTransactionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
