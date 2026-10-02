using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

public partial class DuesPage : AppPage
{
    public DuesPage(DuesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        // After each load, scroll to the row asked for from Budget → Due (ADR 0024 note).
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DuesViewModel.Model) && viewModel.FocusRow is { } row)
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => ScrollTo(row));
        };
    }

    private async void ScrollTo(BudgetDueRow row)
    {
        var view = NotPaidList.Children.OfType<VisualElement>().FirstOrDefault(v => ReferenceEquals(v.BindingContext, row));
        if (view is not null) await Scroller.ScrollToAsync(view, ScrollToPosition.Center, true);
    }
}
