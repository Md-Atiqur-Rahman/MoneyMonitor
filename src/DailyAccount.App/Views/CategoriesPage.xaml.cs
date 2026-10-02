using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class CategoriesPage : AppPage
{
    public CategoriesPage(CategoriesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
