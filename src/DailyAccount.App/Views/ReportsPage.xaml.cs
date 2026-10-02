using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class ReportsPage : AppPage
{
    public ReportsPage(ReportsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
