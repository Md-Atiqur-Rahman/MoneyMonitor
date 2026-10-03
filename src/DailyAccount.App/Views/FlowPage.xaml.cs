using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class FlowPage : AppPage
{
    public FlowPage(FlowViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
