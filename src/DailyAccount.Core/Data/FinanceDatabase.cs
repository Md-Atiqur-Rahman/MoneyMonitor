using DailyAccount.Core.Models;
using SQLite;

namespace DailyAccount.Core.Data;

/// <summary>Owns the SQLite connection and schema. One instance per app (singleton).</summary>
public sealed class FinanceDatabase
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public FinanceDatabase(string path)
    {
        Path = path;
        Connection = new SQLiteAsyncConnection(path,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public string Path { get; }
    public SQLiteAsyncConnection Connection { get; }

    /// <summary>Creates/migrates tables (sqlite-net adds new columns automatically) and seeds categories.</summary>
    public async Task InitAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            await Connection.CreateTableAsync<Account>();
            await Connection.CreateTableAsync<Category>();
            await Connection.CreateTableAsync<Transaction>();
            await Connection.CreateTableAsync<Loan>();
            await Connection.CreateTableAsync<CreditCard>();
            await Connection.CreateTableAsync<RecurringBill>();
            await Connection.CreateTableAsync<PersonalDebt>();
            await Connection.CreateTableAsync<Due>();
            await Connection.CreateTableAsync<BudgetItem>();
            await Connection.CreateTableAsync<AppMeta>();

            if (await Connection.Table<Category>().CountAsync() == 0)
                await Connection.RunInTransactionAsync(SeedCategories);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task CloseAsync() => Connection.CloseAsync();

    /// <summary>Default categories, modelled on the user's monthly sheet (ADR 0011, 0013).</summary>
    public static readonly (string Name, string Bn, CategoryKind Kind, (string Name, string Bn)[] Children)[] Defaults =
    [
        ("Salary", "বেতন", CategoryKind.Income, []),
        ("Bonus", "বোনাস", CategoryKind.Income, []),
        ("Other income", "অন্যান্য আয়", CategoryKind.Income, []),
        ("Bajar", "বাজার", CategoryKind.Expense,
        [
            ("Grocery", "মুদি"), ("Meat", "মাংস"), ("Fish", "মাছ"), ("Vegetables", "সবজি"),
            ("Tiffin", "নাস্তা"), ("Cosmetics", "প্রসাধনী")
        ]),
        ("Fruits", "ফল", CategoryKind.Expense, []),
        ("Transport", "যাতায়াত", CategoryKind.Expense, []),
        ("Medicine", "ওষুধ", CategoryKind.Expense, []),
        ("Rent", "বাসা ভাড়া", CategoryKind.Expense, []),
        ("Gas", "গ্যাস", CategoryKind.Expense, []),
        ("Electric", "বিদ্যুৎ", CategoryKind.Expense, []),
        ("Wifi", "ওয়াইফাই", CategoryKind.Expense, []),
        ("Mobile", "মোবাইল", CategoryKind.Expense, []),
        // DPS is back as a default because it is in the default budget (ADR 0020); Certificate stays removed.
        ("DPS", "ডিপিএস", CategoryKind.Expense, []),
        ("Education", "শিক্ষা", CategoryKind.Expense, []),
        ("Family", "পরিবার", CategoryKind.Expense, []),
        ("Others", "অন্যান্য", CategoryKind.Expense, []),
    ];

    /// <summary>
    /// The fixed monthly budget every user starts with, in taka (ADR 0020). Added once, without any
    /// input, as "Monthly" lines, so it shows in every month; each amount can be changed later.
    /// </summary>
    public static readonly (string Category, string Bn, decimal Estimate)[] DefaultBudget =
    [
        ("Bajar", "বাজার", 18_000),
        ("Education", "শিক্ষা", 1_500),
        ("Family", "পরিবার", 3_000),
        ("Medicine", "ওষুধ", 500),
        ("Fruits", "ফল", 1_500),
        ("Gas", "গ্যাস", 1_700),
        ("Wifi", "ওয়াইফাই", 600),
        ("Mobile", "মোবাইল", 300),
        ("Rent", "বাসা ভাড়া", 11_350),
        ("DPS", "ডিপিএস", 20_000),
        ("Electric", "বিদ্যুৎ", 2_049),
        ("Transport", "যাতায়াত", 2_000),
        ("Others", "অন্যান্য", 1_000),
    ];

    private static void SeedCategories(SQLiteConnection c)
    {
        var order = 0;
        foreach (var (name, bn, kind, children) in Defaults)
        {
            var parent = new Category { Name = name, NameBn = bn, Kind = kind, SortOrder = order++ };
            c.Insert(parent);
            var childOrder = 0;
            foreach (var (childName, childBn) in children)
                c.Insert(new Category { Name = childName, NameBn = childBn, Kind = kind, ParentId = parent.Id, SortOrder = childOrder++ });
        }
    }
}
