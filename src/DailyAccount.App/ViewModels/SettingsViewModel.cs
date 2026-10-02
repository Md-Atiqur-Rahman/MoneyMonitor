using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;

namespace DailyAccount.App.ViewModels;

public sealed partial class SettingsViewModel(AppSettings settings, BackupService backup) : ViewModelBase
{
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
