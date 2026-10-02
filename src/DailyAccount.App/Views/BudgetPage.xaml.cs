using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class BudgetPage : AppPage
{
    public BudgetPage(BudgetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
