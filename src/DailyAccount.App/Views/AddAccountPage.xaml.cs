using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class AddAccountPage : AppPage
{
    public AddAccountPage(AddAccountViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
