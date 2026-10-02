using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.App.ViewModels;

namespace DailyAccount.App.Views;

/// <summary>Base for every screen: reloads its view model each time the page appears.</summary>
public class AppPage : ContentPage
{
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is not ViewModelBase vm) return;
        try
        {
            await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            await Ui.Alert(Loc.F("Err_Unexpected", ex.Message));
        }
    }
}
