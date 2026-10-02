using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class LiabilitiesPage : AppPage
{
    public LiabilitiesPage(LiabilitiesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
