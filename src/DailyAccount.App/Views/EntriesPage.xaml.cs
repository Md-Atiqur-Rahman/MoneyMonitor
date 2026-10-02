using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class EntriesPage : AppPage
{
    public EntriesPage(EntriesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
