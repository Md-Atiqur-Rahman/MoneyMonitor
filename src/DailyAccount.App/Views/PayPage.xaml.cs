using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class PayPage : AppPage
{
    public PayPage(PayViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
