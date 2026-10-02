using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class DuesPage : AppPage
{
    public DuesPage(DuesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
