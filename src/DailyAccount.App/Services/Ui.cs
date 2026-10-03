using DailyAccount.App.Localization;
using DailyAccount.Core.Data;

namespace DailyAccount.App.Services;

/// <summary>Dialogs and navigation helpers shared by view models.</summary>
public static class Ui
{
    private static Page? CurrentPage => Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    public static Task Alert(string message) =>
        CurrentPage?.DisplayAlertAsync(Loc.T("AppName"), message, Loc.T("OK")) ?? Task.CompletedTask;

    public static Task<bool> Confirm(string message) =>
        CurrentPage?.DisplayAlertAsync(Loc.T("AppName"), message, Loc.T("Yes"), Loc.T("No")) ?? Task.FromResult(false);

    /// <summary>Asks for an amount. Returns null when cancelled or invalid.</summary>
    public static async Task<long?> PromptMoney(string title, long? current = null)
    {
        if (CurrentPage is not { } page) return null;
        var text = await page.DisplayPromptAsync(Loc.T("AppName"), title, Loc.T("Save"), Loc.T("Cancel"),
            "0", maxLength: 12, keyboard: Keyboard.Numeric,
            initialValue: current is { } c ? Fmt.EditableAmount(c) : "");
        if (text is null) return null;
        var value = Fmt.ParseMoney(text);
        if (value is null or < 0) await Alert(Loc.T("Err_Amount"));
        return value is >= 0 ? value : null;
    }

    /// <summary>Asks for a short text (a name). Returns null when cancelled or blank.</summary>
    public static async Task<string?> PromptText(string title, string? current = null)
    {
        if (CurrentPage is not { } page) return null;
        var text = await page.DisplayPromptAsync(Loc.T("AppName"), title, Loc.T("Save"), Loc.T("Cancel"),
            maxLength: 40, initialValue: current ?? "");
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Asks for a whole number (e.g. installments). Returns null when cancelled or invalid.</summary>
    public static async Task<int?> PromptInt(string title, int? current = null)
    {
        if (CurrentPage is not { } page) return null;
        var text = await page.DisplayPromptAsync(Loc.T("AppName"), title, Loc.T("Save"), Loc.T("Cancel"),
            "0", maxLength: 4, keyboard: Keyboard.Numeric, initialValue: current?.ToString() ?? "");
        return text is null ? null : Fmt.ParseInt(text);
    }

    /// <summary>Shows a list of choices. Returns the chosen text or null.</summary>
    public static async Task<string?> Choose(string title, params string[] options)
    {
        if (CurrentPage is not { } page) return null;
        var choice = await page.DisplayActionSheetAsync(title, Loc.T("Cancel"), null, options);
        return choice is null || choice == Loc.T("Cancel") ? null : choice;
    }

    public static Task Go(string route) => Shell.Current.GoToAsync(route);

    public static Task Back() => Shell.Current.GoToAsync("..");

    /// <summary>Runs an action; a broken rule shows its translated message. Returns true on success.</summary>
    public static async Task<bool> Try(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (FinanceException ex)
        {
            await Alert(Loc.T(ex.Key));
            return false;
        }
        catch (Exception ex)
        {
            await Alert(Loc.F("Err_Unexpected", ex.Message));
            return false;
        }
    }
}
