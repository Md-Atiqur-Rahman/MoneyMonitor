using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class CategoryReportPage : AppPage
{
    public CategoryReportPage(CategoryReportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
