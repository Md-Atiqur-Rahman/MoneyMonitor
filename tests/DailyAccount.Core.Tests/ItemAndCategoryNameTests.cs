using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;
using SQLite;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0027: qty × rate on item lines, unique category names, default category order.</summary>
public sealed class ItemAndCategoryNameTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-items-{Guid.NewGuid():N}.db3");
    private FinanceDatabase _db = null!;
    private FinanceService _svc = null!;

    public Task InitializeAsync()
    {
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        File.Delete(_path);
    }

    [Fact]
    public void Price_is_qty_times_rate()
    {
        Assert.Equal(Tk(450), ItemMath.Price(5, "kg", Tk(90)));
        Assert.Equal(Tk(195), ItemMath.Price(1.5m, "kg", Tk(130)));
        Assert.Equal(Tk(200), ItemMath.Price(250, "gm", Tk(800)));   // grams at a per-kg rate
        Assert.Equal(Tk(60), ItemMath.Price(500, "ml", Tk(120)));    // ml at a per-litre rate
        Assert.Equal(Tk(36), ItemMath.Price(12, "pcs", Tk(3)));
        Assert.Equal(Tk(33.33m), ItemMath.Price(1, "kg", Tk(33.33m)));
        Assert.Equal("kg", ItemMath.RateUnit("gm"));
        Assert.Equal("ltr", ItemMath.RateUnit("ml"));
        Assert.Equal("pcs", ItemMath.RateUnit("pcs"));
    }

    [Fact]
    public void Rate_is_found_back_from_price()
    {
        Assert.Equal(Tk(90), ItemMath.Rate(5, "kg", Tk(450)));
        Assert.Equal(Tk(800), ItemMath.Rate(250, "gm", Tk(200)));
        Assert.Null(ItemMath.Rate(0, "kg", Tk(450)));
    }

    [Fact]
    public void Quantity_is_written_and_read_back()
    {
        Assert.Equal("5 kg", ItemMath.Format(5, "kg"));
        Assert.Equal("1.5 kg", ItemMath.Format(1.5m, "kg"));
        Assert.Equal("3", ItemMath.Format(3, null));
        Assert.Null(ItemMath.Format(null, "kg"));
        Assert.Null(ItemMath.Format(0, "kg"));

        Assert.Equal((5m, "kg"), ItemMath.Parse("5 kg"));
        Assert.Equal((2m, "kg"), ItemMath.Parse("2kg"));        // older free text
        Assert.Equal((500m, "gm"), ItemMath.Parse("500 g"));
        Assert.Equal((3m, null), ItemMath.Parse("3"));
        Assert.Equal((null, null), ItemMath.Parse("half"));
        Assert.Equal((null, null), ItemMath.Parse("2 bags"));
        Assert.Equal((null, null), ItemMath.Parse(null));
    }

    [Fact]
    public async Task Category_names_are_unique_across_levels_and_languages()
    {
        var s = await _svc.LoadAsync();
        var bajar = s.Categories.Single(c => c.Name == "Bajar");
        var transport = s.Categories.Single(c => c.Name == "Transport");

        async Task<string> Refused(string name, CategoryKind kind, int? parent) =>
            (await Assert.ThrowsAsync<FinanceException>(() => _svc.AddCategoryAsync(name, kind, parent))).Key;

        Assert.Equal("Err_CategoryExists", await Refused("fish", CategoryKind.Expense, bajar.Id));      // same parent
        Assert.Equal("Err_CategoryExists", await Refused("Fish", CategoryKind.Expense, transport.Id));  // another parent
        Assert.Equal("Err_CategoryExists", await Refused("Fish", CategoryKind.Expense, null));         // top level
        Assert.Equal("Err_CategoryExists", await Refused(" bajar ", CategoryKind.Expense, null));
        Assert.Equal("Err_CategoryExists", await Refused("মাছ", CategoryKind.Expense, null));            // Bangla name

        var uber = await _svc.AddCategoryAsync("Uber", CategoryKind.Expense, transport.Id);
        Assert.Equal(transport.Id, uber.ParentId);
        Assert.Equal("Err_CategoryExists", await Refused("UBER", CategoryKind.Expense, transport.Id));
        Assert.Equal(uber.Id, (await _svc.LoadAsync()).FindCategory("uber", CategoryKind.Expense)!.Id);

        // Income and expense names are separate lists.
        await _svc.AddCategoryAsync("Others", CategoryKind.Income, null);

        // Renaming can't make a duplicate either, but keeping the own name (other case) is fine.
        Assert.Equal("Err_CategoryExists",
            (await Assert.ThrowsAsync<FinanceException>(() => _svc.RenameCategoryAsync(uber.Id, "Meat"))).Key);
        await _svc.RenameCategoryAsync(uber.Id, "uber");
    }

    [Fact]
    public async Task Default_expense_categories_are_in_the_users_order()
    {
        var s = await _svc.LoadAsync();
        Assert.Equal(
            ["Bajar", "Education", "Family", "Baby Care", "Medicine", "Fruits", "Gas", "Wifi", "Mobile", "Rent", "DPS", "Electric", "Transport", "Others"],
            s.TopCategories(CategoryKind.Expense).Select(c => c.Name));
    }

    [Fact]
    public async Task Existing_database_gets_baby_care_and_the_new_order_once()
    {
        // An older database: categories in the old order, no Baby Care, one own category.
        var conn = new SQLiteConnection(_path);
        conn.CreateTable<Category>();
        var order = 0;
        foreach (var name in new[] { "Bajar", "Fruits", "Transport", "Medicine", "Rent", "Gas", "Electric", "Wifi", "Mobile", "DPS", "Restaurant", "Others" })
            conn.Insert(new Category { Name = name, Kind = CategoryKind.Expense, SortOrder = order++ });
        conn.Insert(new Category { Name = "Salary", Kind = CategoryKind.Income });
        conn.Close();

        var s = await _svc.LoadAsync();
        // Education and Family were deleted by the user: not brought back. Baby Care is new: added.
        Assert.Equal(
            ["Bajar", "Baby Care", "Medicine", "Fruits", "Gas", "Wifi", "Mobile", "Rent", "DPS", "Electric", "Transport", "Others", "Restaurant"],
            s.TopCategories(CategoryKind.Expense).Select(c => c.Name));

        // Runs once: a later deletion of Baby Care stays deleted.
        await _svc.DeleteCategoryAsync(s.Categories.Single(c => c.Name == "Baby Care").Id);
        await _db.CloseAsync();
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        Assert.DoesNotContain((await _svc.LoadAsync()).Categories, c => c.Name == "Baby Care");
    }
}
