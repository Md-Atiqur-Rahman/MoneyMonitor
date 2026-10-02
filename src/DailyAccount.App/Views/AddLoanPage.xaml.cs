using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class AddLoanPage : AppPage
{
    public AddLoanPage(AddLoanViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
