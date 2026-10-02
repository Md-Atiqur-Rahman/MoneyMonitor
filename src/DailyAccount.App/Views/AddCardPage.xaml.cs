using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class AddCardPage : AppPage
{
    public AddCardPage(AddCardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
