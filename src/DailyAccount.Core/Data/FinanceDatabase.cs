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
            await Connection.CreateTableAsync<SalaryRate>();
            await Connection.CreateTableAsync<CardSubscription>();
            await Connection.CreateTableAsync<Person>();

            if (await Connection.Table<Category>().CountAsync() == 0)
                await Connection.RunInTransactionAsync(SeedCategories);
            if (await Connection.FindAsync<AppMeta>(CategoryOrderKey) is null)
                await Connection.RunInTransactionAsync(UpdateDefaultCategories);
            await Connection.RunInTransactionAsync(LinkPeople);

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
        // Expense categories in the user's order (ADR 0027).
        ("Bajar", "বাজার", CategoryKind.Expense,
        [
            ("Grocery", "মুদি"), ("Meat", "মাংস"), ("Fish", "মাছ"), ("Vegetables", "সবজি"),
            ("Tiffin", "নাস্তা"), ("Cosmetics", "প্রসাধনী")
        ]),
        ("Education", "শিক্ষা", CategoryKind.Expense, []),
        ("Family", "পরিবার", CategoryKind.Expense, []),
        ("Baby Care", "শিশুর যত্ন", CategoryKind.Expense, []),
        ("Medicine", "ওষুধ", CategoryKind.Expense, []),
        ("Fruits", "ফল", CategoryKind.Expense, []),
        ("Gas", "গ্যাস", CategoryKind.Expense, []),
        ("Wifi", "ওয়াইফাই", CategoryKind.Expense, []),
        ("Mobile", "মোবাইল", CategoryKind.Expense, []),
        ("Rent", "বাসা ভাড়া", CategoryKind.Expense, []),
        // DPS is back as a default because it is in the default budget (ADR 0020); Certificate stays removed.
        ("DPS", "ডিপিএস", CategoryKind.Expense, []),
        ("Electric", "বিদ্যুৎ", CategoryKind.Expense, []),
        ("Transport", "যাতায়াত", CategoryKind.Expense, []),
        ("Others", "অন্যান্য", CategoryKind.Expense, []),
    ];

    private const string CategoryOrderKey = "category-order-v2";

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

    /// <summary>
    /// Once per database (ADR 0027): adds default categories that are missing (Baby Care) and puts the
    /// default expense categories in the user's order; the user's own categories follow them.
    /// A default the user deleted on purpose is not brought back, except Baby Care, which is new.
    /// </summary>
    private static void UpdateDefaultCategories(SQLiteConnection c)
    {
        var all = c.Table<Category>().ToList();
        var top = all.Where(x => x.ParentId is null && x.Kind == CategoryKind.Expense).ToList();
        var order = 0;
        foreach (var (name, bn, kind, _) in Defaults.Where(d => d.Kind == CategoryKind.Expense))
        {
            var existing = top.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                if (name != "Baby Care" || all.Any(x => x.Kind == kind && Category.SameName(x, name))) continue;
                c.Insert(new Category { Name = name, NameBn = bn, Kind = kind, SortOrder = order++ });
                continue;
            }
            existing.SortOrder = order++;
            c.Update(existing);
            top.Remove(existing);
        }
        foreach (var own in top.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
        {
            own.SortOrder = order++;
            c.Update(own);
        }
        c.InsertOrReplace(new AppMeta { Key = CategoryOrderKey, Value = "done" });
    }

    /// <summary>
    /// Every personal debt belongs to a <see cref="Person"/> (ADR 0042): a debt without one (older data, an older
    /// backup) gets the person with the same name — any letter case — or a new one. Safe to run every time.
    /// </summary>
    public static void LinkPeople(SQLiteConnection c)
    {
        var people = c.Table<Person>().ToList();
        foreach (var debt in c.Table<PersonalDebt>().ToList())
        {
            if (debt.PersonId is { } id && people.Any(p => p.Id == id)) continue;
            var name = debt.PersonName.Trim();
            var person = people.FirstOrDefault(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (person is null)
            {
                person = new Person { Name = name };
                c.Insert(person);
                people.Add(person);
            }
            debt.PersonId = person.Id;
            debt.PersonName = person.Name;
            c.Update(debt);
        }
    }

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
