using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

/// <summary>One due in a list (Home, Dues). <see cref="Pay"/> is null for paid rows.</summary>
public sealed record DueRow(string Title, string Subtitle, string Amount, string Status, bool IsLast, string PayText, ICommand? Pay, bool Upcoming = false)
{
    public bool CanPay => Pay is not null;
    public bool IsPaid => Pay is null;
}

/// <summary>One transaction in a history list. <see cref="Delete"/> removes it after a confirm.</summary>
public sealed record TxRow(string Title, string Subtitle, string Amount, Color AmountColor, ICommand? Delete, ICommand? Edit = null)
{
    /// <summary>False for a shown-only row (Liabilities → Last month, ADR 0037): no 🗑, no edit.</summary>
    public bool CanDelete => Delete is not null;

    /// <summary>The same row, shown only: it can't be deleted or edited from here.</summary>
    public TxRow ReadOnly() => this with { Delete = null, Edit = null };
}

/// <summary>A labelled value with a 0–1 bar (report categories, trend). <see cref="Tap"/> opens a drill-down.</summary>
public sealed record BarRow(string Label, string Amount, double Progress, Color BarColor, ICommand? Tap = null)
{
    public bool HasTap => Tap is not null;
}

/// <summary>A selectable chip (transaction type, account type).</summary>
public sealed partial class ChipOption(string key, string label, Action<ChipOption> onSelect)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => onSelect(this);
}

/// <summary>Turns stored records into display text in the current language.</summary>
public static class Display
{
    public static Color Positive => Color.FromArgb("#1E6B4A");
    public static Color Negative => Color.FromArgb("#16201A");
    public static Color Warn => Color.FromArgb("#B4441A");
    public static Color Muted => Color.FromArgb("#5B6660");

    public static string DueTitle(Due due, FinanceSnapshot s) => due.SourceType switch
    {
        DueSource.Loan when s.Loans.FirstOrDefault(l => l.Id == due.SourceId) is { } loan
            => $"{loan.Lender} ({Fmt.Number(due.Sequence)}/{Fmt.Number(loan.InstallmentCount)})",
        DueSource.Card when s.Cards.FirstOrDefault(c => c.Id == due.SourceId) is { } card
            => Loc.F("Due_CardBill", card.Name, Fmt.ShortMonth(CardCycleMonth(due))),
        DueSource.Bill when s.Bills.FirstOrDefault(b => b.Id == due.SourceId) is { } bill
            => Loc.IsBangla && !string.IsNullOrEmpty(bill.NameBn) ? bill.NameBn : bill.Name,
        DueSource.Personal when s.Debts.FirstOrDefault(d => d.Id == due.SourceId) is { } debt
            => Loc.F("Due_Borrowed", debt.PersonName),
        _ => due.Title
    };

    /// <summary>Card dues are keyed by cycle start "yyyy-MM-dd"; the bill is for that cycle's month.</summary>
    private static string CardCycleMonth(Due due) =>
        due.PeriodKey.Length >= 7 ? due.PeriodKey[..7] : due.DueMonth;

    public static bool IsLastInstallment(Due due, FinanceSnapshot s) =>
        due.SourceType == DueSource.Loan
        && s.Loans.FirstOrDefault(l => l.Id == due.SourceId) is { } loan
        && due.Sequence == loan.InstallmentCount && loan.InstallmentCount > 1;

    public static string Status(DueStatus status) => Loc.T($"Status_{status}");

    public static DueRow ToRow(Due due, FinanceSnapshot s, bool canPay = true)
    {
        var subtitle = due.DueDate is { } d ? Loc.F("DueOn", Fmt.DayMonth(d)) : Loc.T("Dues_NoDate");
        if (due.Status == DueStatus.Partial) subtitle += " · " + Loc.T("Remaining") + " " + Fmt.Money(due.Remaining);

        ICommand? pay = due.Status == DueStatus.Paid || !canPay
            ? null
            : new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?dueId={due.Id}"));

        var upcoming = !canPay && due.Status != DueStatus.Paid;
        return new DueRow(
            DueTitle(due, s),
            subtitle,
            Fmt.Money(due.Status == DueStatus.Paid ? due.Amount : due.Remaining),
            upcoming ? Loc.T("Status_Upcoming") : Status(due.Status),
            IsLastInstallment(due, s),
            Loc.T(due.SourceType == DueSource.Personal ? "Repay" : "Pay"),
            pay,
            upcoming);
    }

    /// <summary>
    /// Rows for a set of dues, with everything billed on one card (statement + EMIs) merged into a single
    /// "card payment" row that opens the card-bill payment (ADR 0012). Other dues stay one row each.
    /// <paramref name="canPay"/> = false for dues that aren't payable yet (next month): no Pay button.
    /// </summary>
    public static List<DueRow> DueRows(IEnumerable<Due> dues, FinanceSnapshot s, bool canPay = true)
    {
        var loanCard = s.Loans.ToDictionary(l => l.Id, l => l.CardId);
        int? CardOf(Due d) => d.SourceType switch
        {
            DueSource.Card => d.SourceId,
            DueSource.Loan => loanCard.GetValueOrDefault(d.SourceId),
            _ => null
        };

        var list = dues.ToList();
        var rows = new List<(DateTime Sort, DueRow Row)>();

        foreach (var group in list.Where(d => CardOf(d) is not null).GroupBy(d => CardOf(d)!.Value))
        {
            var card = s.Cards.FirstOrDefault(c => c.Id == group.Key);
            if (card is null) continue;
            var allPaid = group.All(d => d.Status == DueStatus.Paid);
            long Part(IEnumerable<Due> ds) => ds.Sum(d => allPaid ? d.Amount : d.Remaining);
            var emi = Part(group.Where(d => d.SourceType == DueSource.Loan));
            var statement = Part(group.Where(d => d.SourceType == DueSource.Card));
            var first = group.Min(d => d.DueDate) ?? DateTime.MaxValue;
            var month = group.Max(d => d.DueMonth)!;

            rows.Add((first, new DueRow(
                Loc.F("Due_CardPayment", card.Name),
                Loc.F("Due_CardBreakdown", Fmt.Money(emi), Fmt.Money(statement)) +
                    (first != DateTime.MaxValue ? " · " + Loc.F("DueOn", Fmt.DayMonth(first)) : ""),
                Fmt.Money(emi + statement),
                !canPay && !allPaid ? Loc.T("Status_Upcoming")
                    : Status(allPaid ? DueStatus.Paid : group.Any(d => d.PaidAmount > 0) ? DueStatus.Partial : DueStatus.Pending),
                false,
                Loc.T("Pay"),
                allPaid || !canPay ? null : new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?cardId={card.Id}&month={month}")),
                !canPay && !allPaid)));
        }

        rows.AddRange(list.Where(d => CardOf(d) is null).Select(d => (d.DueDate ?? DateTime.MaxValue, ToRow(d, s, canPay))));
        return rows.OrderBy(r => r.Sort).Select(r => r.Row).ToList();
    }

    public static string CategoryName(int? categoryId, FinanceSnapshot s) =>
        s.Categories.FirstOrDefault(c => c.Id == categoryId) is { } c
            ? (Loc.IsBangla && !string.IsNullOrEmpty(c.NameBn) ? c.NameBn : c.Name)
            : Loc.T("Uncategorized");

    /// <summary>Why a new name was refused (ADR 0027): "\"Fish\" already exists under Bajar." / "... as a category."</summary>
    public static string CategoryExists(Category existing, FinanceSnapshot s) => existing.ParentId is { } parent
        ? Loc.F("Category_ExistsUnder", CategoryName(existing.Id, s), CategoryName(parent, s))
        : Loc.F("Category_ExistsTop", CategoryName(existing.Id, s));

    /// <summary>
    /// Months borrowed money can be paid back in (ADR 0030): "Not decided yet", then the month it was
    /// borrowed and the 24 after it. Value = month key, or null for none.
    /// </summary>
    public static Dictionary<string, string?> PayMonthOptions(DateTime borrowed)
    {
        var first = Core.MonthKey.Of(borrowed);
        var options = new Dictionary<string, string?> { [Loc.T("Debt_NoPayMonth")] = null };
        for (var i = 0; i <= 24; i++)
        {
            var m = Core.MonthKey.Add(first, i);
            options[Fmt.Month(m)] = m;
        }
        return options;
    }

    /// <summary>"October 2026" → "2026-10" for the month of <paramref name="from"/> and the 24 after it.</summary>
    public static Dictionary<string, string> MonthOptions(DateTime from) =>
        PayMonthOptions(from).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);

    /// <summary>"Paid back ৳3,000 of ৳5,000 · left ৳2,000", "Fully paid · last on 9 Oct" (ADR 0040).</summary>
    public static string DebtProgress(DebtLedger l) => l.IsFullyPaid
        ? Loc.T("Debt_FullyPaid") + (l.LastPayment is { } on ? " · " + Loc.F("Debt_LastOn", Fmt.DayMonth(on)) : "")
        : l.PaidBack == 0
            ? Loc.T(l.Debt.Direction == DebtDirection.Lent ? "Debt_NothingBackYet" : "Debt_NothingRepaidYet")
            : Loc.F(l.Debt.Direction == DebtDirection.Lent ? "Debt_BackOf" : "Debt_RepaidOf",
                Fmt.Money(l.PaidBack), Fmt.Money(l.Total), Fmt.Money(l.Left));

    public static string AccountName(int? accountId, FinanceSnapshot s) =>
        s.Accounts.FirstOrDefault(a => a.Id == accountId)?.Name ?? "";

    /// <summary>A transaction as seen from one account (or from a card when accountId is null).</summary>
    public static TxRow ToRow(Transaction t, FinanceSnapshot s, int? accountId, Func<Transaction, Task> delete)
    {
        string title = t.Type switch
        {
            TransactionType.Income or TransactionType.Expense or TransactionType.CardPurchase when t.ItemName is not null
                => t.Quantity is null ? t.ItemName : $"{t.ItemName} · {t.Quantity}",
            TransactionType.Income or TransactionType.Expense or TransactionType.CardPurchase when !string.IsNullOrWhiteSpace(t.Note)
                => t.Note!,
            TransactionType.Income or TransactionType.Expense or TransactionType.CardPurchase => CategoryName(t.CategoryId, s),
            TransactionType.Transfer => t.AccountId == accountId
                ? Loc.F("TransferTo", AccountName(t.ToAccountId, s))
                : Loc.F("TransferFrom", AccountName(t.AccountId, s)),
            TransactionType.DuePayment when s.Dues.FirstOrDefault(d => d.Id == t.DueId) is { } due => DueTitle(due, s),
            _ => t.Note ?? Loc.T($"Tx_{t.Type}")
        };

        var titledByText = t.ItemName is not null || (!string.IsNullOrWhiteSpace(t.Note)
            && t.Type is TransactionType.Income or TransactionType.Expense or TransactionType.CardPurchase);
        var subtitle = Fmt.Date(t.Date) + " · " + (titledByText ? CategoryName(t.CategoryId, s) : Loc.T($"Tx_{t.Type}"));
        if (!string.IsNullOrWhiteSpace(t.Note) && title != t.Note) subtitle += " · " + t.Note;

        // From an account: its balance change. In a general list: + money in, − money out, ↔ transfer.
        var effect = accountId is { } id ? BalanceService(id, t) : t.Type switch
        {
            TransactionType.Income or TransactionType.BorrowIn or TransactionType.LendReturn => t.Amount,
            TransactionType.Transfer => 0,
            _ => -t.Amount
        };
        var amount = effect == 0 && t.Type == TransactionType.Transfer
            ? "↔ " + Fmt.Money(t.Amount)
            : (effect > 0 ? "+" : effect < 0 ? "−" : "") + Fmt.Money(Math.Abs(effect));

        // Income, expenses, transfers and card purchases open the edit form when tapped (ADR 0026).
        ICommand? editCommand = t.Type is TransactionType.Income or TransactionType.Expense
                or TransactionType.Transfer or TransactionType.CardPurchase
            ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.AddTransaction}?id={t.Id}"))
            : null;

        return new TxRow(title, subtitle, amount, effect > 0 ? Positive : Negative,
            new AsyncRelayCommand(() => delete(t)), editCommand);
    }

    private static long BalanceService(int accountId, Transaction t) =>
        Core.Services.BalanceService.EffectOn(accountId, t);

    public static List<Option> AccountOptions(FinanceSnapshot s) =>
        s.Accounts.Where(a => a.IsActive)
            .Select(a => new Option(a.Id, $"{a.Name} · {Fmt.Money(s.Balance(a))}"))
            .ToList();

    public static string MonthKeyOf(DateTime d) => MonthKey.Of(d);
}
