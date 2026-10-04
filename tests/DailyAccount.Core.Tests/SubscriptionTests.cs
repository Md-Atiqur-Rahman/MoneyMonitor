using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0038: a card purchase that repeats every month by itself.</summary>
public sealed class SubscriptionTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-subs-{Guid.NewGuid():N}.db3");
    private FinanceDatabase _db = null!;
    private FinanceService _svc = null!;
    private CreditCard _card = null!;
    private int _others;

    public async Task InitializeAsync()
    {
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        var bank = new Account { Name = "Bank", Type = AccountType.Bank };
        await _svc.AddAccountAsync(bank);
        _card = new CreditCard { Name = "Card", CreditLimit = Tk(100_000), StatementDay = 1, DueDay = 15, PayFromAccountId = bank.Id };
        await _svc.AddCardAsync(_card, new DateTime(2026, 9, 1));
        _others = (await _svc.LoadAsync()).Categories.Single(c => c.Name == "Others").Id;
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        File.Delete(_path);
    }

    private static List<Transaction> Purchases(FinanceSnapshot s) =>
        s.Transactions.Where(t => t.Type == TransactionType.CardPurchase).OrderBy(t => t.Date).ToList();

    private CardSubscription Ai(string from = "2026-09") => new()
    {
        Name = "AI Subscription", Amount = Tk(3_000), CardId = _card.Id, CategoryId = _others, Day = 20, FromMonth = from
    };

    [Fact]
    public async Task It_is_added_every_month_on_its_day_and_lands_on_the_card_bill()
    {
        await _svc.AddSubscriptionAsync(Ai(), new DateTime(2026, 10, 3));
        var s = await _svc.LoadAsync();
        var purchases = Purchases(s);
        Assert.Equal([new DateTime(2026, 9, 20)], purchases.Select(t => t.Date)); // October's day hasn't come yet
        Assert.Equal(Tk(3_000), s.CardBillDues(_card.Id, "2026-10").Sum(d => d.Remaining)); // on October's bill

        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 25));
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 25)); // once
        purchases = Purchases(await _svc.LoadAsync());
        Assert.Equal([new DateTime(2026, 9, 20), new DateTime(2026, 10, 20)], purchases.Select(t => t.Date));
        Assert.All(purchases, t => Assert.Equal("AI Subscription", t.ItemName));
        Assert.All(purchases, t => Assert.Equal(_others, t.CategoryId));
    }

    [Fact]
    public async Task A_purchase_typed_by_hand_in_its_first_month_is_not_doubled()
    {
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = _card.Id, ItemName = "AI subscription", Amount = Tk(2_900), Date = new DateTime(2026, 9, 20) });
        await _svc.AddSubscriptionAsync(Ai(), new DateTime(2026, 10, 25));
        var purchases = Purchases(await _svc.LoadAsync());
        Assert.Equal([Tk(2_900), Tk(3_000)], purchases.Select(t => t.Amount)); // typed September + automatic October
    }

    [Fact]
    public async Task New_amount_applies_to_later_months_and_stop_keeps_what_was_added()
    {
        var sub = await _svc.AddSubscriptionAsync(Ai(), new DateTime(2026, 9, 25));
        await _svc.SetSubscriptionAmountAsync(sub.Id, Tk(3_500));
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 25));
        Assert.Equal([Tk(3_000), Tk(3_500)], Purchases(await _svc.LoadAsync()).Select(t => t.Amount));

        await _svc.StopSubscriptionAsync(sub.Id, "2026-11");
        await _svc.GenerateDuesAsync(new DateTime(2026, 12, 25));
        Assert.Equal(2, Purchases(await _svc.LoadAsync()).Count); // none for November / December, nothing removed

        // A deleted automatic purchase isn't added back.
        var s = await _svc.LoadAsync();
        await _svc.DeleteTransactionAsync(Purchases(s).Last().Id);
        await _svc.GenerateDuesAsync(new DateTime(2026, 12, 26));
        Assert.Single(Purchases(await _svc.LoadAsync()));
    }

    [Fact]
    public async Task Backup_keeps_subscriptions_without_doubling()
    {
        await _svc.AddSubscriptionAsync(Ai(), new DateTime(2026, 10, 25));
        var backup = BackupData.FromJson((await _svc.ExportAsync(DateTime.Now)).ToJson());
        Assert.Single(backup.Subscriptions);
        await _svc.ImportAsync(backup);
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 25));
        Assert.Equal(2, Purchases(await _svc.LoadAsync()).Count);
    }
}
