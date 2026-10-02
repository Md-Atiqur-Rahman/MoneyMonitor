using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class DashboardPage : AppPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
