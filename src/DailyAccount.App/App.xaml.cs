using DailyAccount.App.Localization;
using DailyAccount.App.Services;

namespace DailyAccount.App;

public partial class App : Application
{
    public App(AppSettings settings)
    {
        InitializeComponent();
        // Light theme only for now; the palette is designed for it.
        UserAppTheme = AppTheme.Light;
        Loc.SetLanguage(settings.Language);
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());

    /// <summary>Rebuilds every screen so all text picks up the new language.</summary>
    public static void ReloadShell()
    {
        if (Current?.Windows.FirstOrDefault() is { } window)
            window.Page = new AppShell();
    }
}
