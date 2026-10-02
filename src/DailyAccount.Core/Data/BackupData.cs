using System.Text.Json;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Data;

/// <summary>The whole database as one JSON document. See docs/adr/0007-json-backup-file.md.</summary>
public sealed class BackupData
{
    /// <summary>1 = first release. 2 = budgets, sub-categories, items, card EMIs (older files still load).</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public DateTime CreatedAt { get; set; }
    public List<Account> Accounts { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Transaction> Transactions { get; set; } = [];
    public List<Loan> Loans { get; set; } = [];
    public List<CreditCard> Cards { get; set; } = [];
    public List<RecurringBill> Bills { get; set; } = [];
    public List<PersonalDebt> Debts { get; set; } = [];
    public List<Due> Dues { get; set; } = [];
    public List<BudgetItem> Budget { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static BackupData FromJson(string json)
    {
        BackupData? data;
        try
        {
            data = JsonSerializer.Deserialize<BackupData>(json, Options);
        }
        catch (JsonException)
        {
            throw new FinanceException("Err_BackupInvalid");
        }

        if (data is null) throw new FinanceException("Err_BackupInvalid");
        if (data.Version > CurrentVersion) throw new FinanceException("Err_BackupNewer");
        return data;
    }
}
