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

    // ----- Monthly salary, added automatically (ADR 0035) -----
    [ObservableProperty] private string _salaryText = "";
    [ObservableProperty] private string _payDayText = "1";
    [ObservableProperty] private List<Option> _salaryAccounts = [];
    [ObservableProperty] private Option? _salaryAccount;
    [ObservableProperty] private List<Option> _salaryMonths = [];
    [ObservableProperty] private Option? _salaryFrom;
    [ObservableProperty] private string _salaryHistory = "";
    [ObservableProperty] private bool _hasSalary;
    private List<string> _salaryMonthKeys = [];
    [ObservableProperty] private string _lastBackupText = "";
    [ObservableProperty] private bool _neverBackedUp;

    public override async Task LoadAsync()
    {
        await LoadSalaryAsync();
        IsBangla = Loc.IsBangla;
        IsEnglish = !Loc.IsBangla;
        IncomeText = settings.ExpectedIncome > 0 ? Fmt.EditableAmount(settings.ExpectedIncome) : "";
        StartDate = settings.StartMonth is { } m ? DailyAccount.Core.MonthKey.FirstDay(m) : DateTime.Today;
        StartText = settings.StartMonth is { } sm ? Loc.F("Settings_StartSet", Fmt.Month(sm)) : "";
        RefreshBackup();
    }

    private async Task LoadSalaryAsync()
    {
        var s = await finance.LoadAsync();
        var current = DailyAccount.Core.MonthKey.Of(DateTime.Today);
        SalaryAccounts = Display.AccountOptions(s);
        var rate = s.SalaryRateIn(current) ?? s.SalaryRates?.OrderBy(r => r.FromMonth).FirstOrDefault();
        HasSalary = rate is { Amount: > 0 };
        SalaryText = rate is { Amount: > 0 } ? Fmt.EditableAmount(rate.Amount) : "";
        PayDayText = (rate?.Day ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        SalaryAccount = SalaryAccounts.FirstOrDefault(a => a.Id == rate?.AccountId) ?? SalaryAccounts.FirstOrDefault();

        // From which month: the start month (or a year back) up to a year ahead; default this month.
        var first = settings.StartMonth ?? DailyAccount.Core.MonthKey.Add(current, -12);
        _salaryMonthKeys = [];
        for (var m = first; string.CompareOrdinal(m, DailyAccount.Core.MonthKey.Add(current, 12)) <= 0; m = DailyAccount.Core.MonthKey.Add(m, 1))
            _salaryMonthKeys.Add(m);
        SalaryMonths = _salaryMonthKeys.Select((m, i) => new Option(i, Fmt.Month(m))).ToList();
        SalaryFrom = SalaryMonths.ElementAtOrDefault(Math.Max(0, _salaryMonthKeys.IndexOf(current)));

        SalaryHistory = string.Join("\n", (s.SalaryRates ?? []).OrderBy(r => r.FromMonth).Select(r => r.Amount > 0
            ? Loc.F("Salary_Rate", Fmt.Month(r.FromMonth), Fmt.Money(r.Amount), Fmt.Number(r.Day), Display.AccountName(r.AccountId, s))
            : Loc.F("Salary_Stopped", Fmt.Month(r.FromMonth))));
    }

    /// <summary>Saves the salary from the chosen month on: the first time, or an increment (ADR 0035).</summary>
    [RelayCommand]
    private async Task SaveSalary()
    {
        var amount = Fmt.ParseMoney(SalaryText);
        var day = Fmt.ParseInt(PayDayText);
        if (amount is not > 0) { await Ui.Alert(Loc.T("Err_Amount")); return; }
        if (day is not (>= 1 and <= 31)) { await Ui.Alert(Loc.T("Err_Day")); return; }
        if (SalaryAccount is null || SalaryFrom is null) return;
        var from = _salaryMonthKeys[SalaryFrom.Id];
        if (!await Ui.Try(async () =>
            {
                await finance.SetSalaryAsync(from, amount.Value, SalaryAccount.Id, day.Value);
                await finance.GenerateSalaryAsync(DateTime.Today);
            })) return;
        settings.ExpectedIncome = amount.Value; // the fallback for months without a salary follows too
        IncomeText = Fmt.EditableAmount(amount.Value);
        await LoadSalaryAsync();
        await Ui.Alert(Loc.F("Salary_Saved", Fmt.Money(amount.Value), Fmt.Month(from)));
    }

    /// <summary>No salary from the chosen month on (e.g. a job ended); earlier months keep theirs.</summary>
    [RelayCommand]
    private async Task StopSalary()
    {
        if (SalaryFrom is null || !await Ui.Confirm(Loc.F("Salary_ConfirmStop", SalaryFrom.Display))) return;
        var from = _salaryMonthKeys[SalaryFrom.Id];
        if (await Ui.Try(() => finance.SetSalaryAsync(from, 0, SalaryAccount?.Id ?? 0, 1))) await LoadSalaryAsync();
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
