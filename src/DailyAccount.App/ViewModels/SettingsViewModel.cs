using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;

namespace DailyAccount.App.ViewModels;

public sealed partial class SettingsViewModel(AppSettings settings, BackupService backup, DailyAccount.Core.Data.FinanceService finance) : ViewModelBase
{
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private string _startText = "";
    [ObservableProperty] private bool _isEnglish;
    [ObservableProperty] private bool _isBangla;
    [ObservableProperty] private string _incomeText = "";
    [ObservableProperty] private string _lastBackupText = "";
    [ObservableProperty] private bool _neverBackedUp;

    public override Task LoadAsync()
    {
        IsBangla = Loc.IsBangla;
        IsEnglish = !Loc.IsBangla;
        IncomeText = settings.ExpectedIncome > 0 ? Fmt.EditableAmount(settings.ExpectedIncome) : "";
        StartDate = settings.StartMonth is { } m ? DailyAccount.Core.MonthKey.FirstDay(m) : DateTime.Today;
        StartText = settings.StartMonth is { } sm ? Loc.F("Settings_StartSet", Fmt.Month(sm)) : "";
        RefreshBackup();
        return Task.CompletedTask;
    }

    private void RefreshBackup()
    {
        NeverBackedUp = settings.LastBackup is null;
        LastBackupText = settings.LastBackup is { } d ? Loc.F("LastBackup", Fmt.Date(d)) : Loc.T("NeverBackedUp");
    }

    [RelayCommand]
    private void SetLanguage(string language)
    {
        if (language == Loc.Language) return;
        settings.Language = language;
        Loc.SetLanguage(language);
        App.ReloadShell();
    }

    /// <summary>First month of data (ADR 0025): every month from it to now gets a budget.</summary>
    [RelayCommand]
    private async Task SaveStart()
    {
        var start = DailyAccount.Core.MonthKey.Of(StartDate);
        var current = DailyAccount.Core.MonthKey.Of(DateTime.Today);
        if (string.CompareOrdinal(start, current) > 0) start = current;
        settings.StartMonth = start;
        await Ui.Try(() => finance.FillBudgetMonthsAsync(start, current));
        StartText = Loc.F("Settings_StartSet", Fmt.Month(start));
        await Ui.Alert(Loc.T("Saved"));
    }

    [RelayCommand]
    private async Task SaveIncome()
    {
        var value = Fmt.ParseMoney(IncomeText);
        if (value is null or < 0)
        {
            await Ui.Alert(Loc.T("Err_Amount"));
            return;
        }
        settings.ExpectedIncome = value.Value;
        await Ui.Alert(Loc.T("Saved"));
    }

    [RelayCommand]
    private Task OpenCategories() => Ui.Go(AppShell.Categories);

    [RelayCommand]
    private async Task Export()
    {
        await Ui.Try(backup.ExportAsync);
        RefreshBackup();
    }

    [RelayCommand]
    private async Task Restore()
    {
        var restored = false;
        if (await Ui.Try(async () => restored = await backup.RestoreAsync()) && restored)
            await Ui.Alert(Loc.T("Restore_Done"));
    }
}
