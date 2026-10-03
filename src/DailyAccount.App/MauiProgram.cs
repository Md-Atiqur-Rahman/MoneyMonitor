using CommunityToolkit.Mvvm.ComponentModel;
using DailyAccount.App.Services;
using DailyAccount.App.ViewModels;
using DailyAccount.App.Views;
using DailyAccount.Core.Data;
using Microsoft.Extensions.Logging;

namespace DailyAccount.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Data (Core) and app services: one instance for the app's lifetime.
        builder.Services.AddSingleton(_ => new FinanceDatabase(Path.Combine(FileSystem.AppDataDirectory, "dailyaccount.db3")));
        builder.Services.AddSingleton<FinanceService>();
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<MonthState>();
        builder.Services.AddSingleton<BackupService>();

        // Screens: a fresh page + view model each time one is opened.
        AddScreen<DashboardPage, DashboardViewModel>(builder.Services);
        AddScreen<BudgetPage, BudgetViewModel>(builder.Services);
        AddScreen<DuesPage, DuesViewModel>(builder.Services);
        AddScreen<AccountsPage, AccountsViewModel>(builder.Services);
        AddScreen<LiabilitiesPage, LiabilitiesViewModel>(builder.Services);
        AddScreen<ReportsPage, ReportsViewModel>(builder.Services);
        AddScreen<AddTransactionPage, AddTransactionViewModel>(builder.Services);
        AddScreen<PayPage, PayViewModel>(builder.Services);
        AddScreen<AddAccountPage, AddAccountViewModel>(builder.Services);
        AddScreen<AccountDetailPage, AccountDetailViewModel>(builder.Services);
        AddScreen<AddLoanPage, AddLoanViewModel>(builder.Services);
        AddScreen<AddCardPage, AddCardViewModel>(builder.Services);
        AddScreen<CategoriesPage, CategoriesViewModel>(builder.Services);
        AddScreen<CategoryReportPage, CategoryReportViewModel>(builder.Services);
        AddScreen<EntriesPage, EntriesViewModel>(builder.Services);
        AddScreen<FlowPage, FlowViewModel>(builder.Services);
        AddScreen<SettingsPage, SettingsViewModel>(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    private static void AddScreen<TPage, TViewModel>(IServiceCollection services)
        where TPage : Page
        where TViewModel : ObservableObject
    {
        services.AddTransient<TPage>();
        services.AddTransient<TViewModel>();
    }
}
