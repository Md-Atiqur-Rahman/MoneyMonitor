using DailyAccount.App.Localization;
using DailyAccount.Core.Data;

namespace DailyAccount.App.Services;

/// <summary>
/// Export: writes a JSON file and opens Android's share sheet (Google Drive, Gmail, WhatsApp…).
/// Restore: lets the user pick a file and replaces all data. See docs/adr/0007-json-backup-file.md.
/// </summary>
public sealed class BackupService(FinanceService finance, AppSettings settings)
{
    public async Task ExportAsync()
    {
        var now = DateTime.Now;
        var json = (await finance.ExportAsync(now)).ToJson();
        var file = Path.Combine(FileSystem.CacheDirectory, $"DailyAccount-backup-{now:yyyyMMdd-HHmm}.json");
        await File.WriteAllTextAsync(file, json);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = Loc.T("ExportBackup"),
            File = new ShareFile(file, "application/json")
        });
        settings.LastBackup = now;
    }

    /// <summary>Returns false when the user cancelled.</summary>
    public async Task<bool> RestoreAsync()
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("RestoreBackup") });
        if (picked is null) return false;

        string json;
        await using (var stream = await picked.OpenReadAsync())
        using (var reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync();

        var data = BackupData.FromJson(json);
        if (!await Ui.Confirm(Loc.F("Restore_Confirm", Fmt.Date(data.CreatedAt))))
            return false;

        await finance.ImportAsync(data);
        return true;
    }
}
