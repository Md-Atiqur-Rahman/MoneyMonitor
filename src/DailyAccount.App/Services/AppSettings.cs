using DailyAccount.App.Localization;

namespace DailyAccount.App.Services;

/// <summary>Small user preferences stored with MAUI Preferences (not in the SQLite database).</summary>
public sealed class AppSettings
{
    private readonly IPreferences _prefs = Preferences.Default;

    public string Language
    {
        get => _prefs.Get("language", Loc.English);
        set => _prefs.Set("language", value);
    }

    /// <summary>In poisha. 0 = not set.</summary>
    public long ExpectedIncome
    {
        get => _prefs.Get("expected_income", 0L);
        set => _prefs.Set("expected_income", value);
    }

    /// <summary>"yyyy-MM": the first month the user enters data for (ADR 0025). null = not set (current month).</summary>
    public string? StartMonth
    {
        get => _prefs.Get<string?>("start_month", null);
        set => _prefs.Set("start_month", value);
    }

    public DateTime? LastBackup
    {
        get => _prefs.Get("last_backup", 0L) is var ticks and > 0 ? new DateTime(ticks) : null;
        set => _prefs.Set("last_backup", value?.Ticks ?? 0L);
    }
}
