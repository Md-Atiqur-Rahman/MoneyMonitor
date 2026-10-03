using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0035: the monthly salary is added automatically and can be changed from a month on.</summary>
public sealed class SalaryTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-salary-{Guid.NewGuid():N}.db3");
    private FinanceDatabase _db = null!;
    private FinanceService _svc = null!;
    private Account _bank = null!;

    public async Task InitializeAsync()
    {
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        _bank = new Account { Name = "Bank", Type = AccountType.Bank };
        await _svc.AddAccountAsync(_bank);
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        File.Delete(_path);
    }

    private static List<Transaction> Salaries(FinanceSnapshot s) =>
        s.Transactions.Where(t => t.Type == TransactionType.Income).OrderBy(t => t.Date).ToList();

    [Fact]
    public async Task Salary_is_added_once_a_month_from_its_pay_day()
    {
        await _svc.SetSalaryAsync("2026-08", Tk(80_000), _bank.Id, 1);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 3));
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 3)); // idempotent

        var s = await _svc.LoadAsync();
        var salaries = Salaries(s);
        Assert.Equal([new DateTime(2026, 8, 1), new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)], salaries.Select(t => t.Date));
        Assert.All(salaries, t => Assert.Equal(Tk(80_000), t.Amount));
        Assert.All(salaries, t => Assert.Equal("Salary", s.Categories.Single(c => c.Id == t.CategoryId).Name));
        Assert.Equal(Tk(240_000), s.Balance(_bank));

        // Pay day on the 25th: October's isn't there on the 3rd yet, but the plan still expects it.
        await _svc.SetSalaryAsync("2026-11", Tk(80_000), _bank.Id, 25);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 11, 3));
        s = await _svc.LoadAsync();
        Assert.Equal(3, Salaries(s).Count);
        Assert.Equal(Tk(80_000), s.Plan("2026-11", 0).Income);
        Assert.True(s.Plan("2026-11", 0).IncomeIsExpected);
    }

    [Fact]
    public async Task An_increment_applies_from_its_month_and_updates_entries_already_added()
    {
        await _svc.SetSalaryAsync("2026-08", Tk(80_000), _bank.Id, 1);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 3));

        await _svc.SetSalaryAsync("2026-10", Tk(90_000), _bank.Id, 1);
        var s = await _svc.LoadAsync();
        Assert.Equal([Tk(80_000), Tk(80_000), Tk(90_000)], Salaries(s).Select(t => t.Amount));
        Assert.Equal(Tk(80_000), s.SalaryIn("2026-09"));
        Assert.Equal(Tk(90_000), s.SalaryIn("2027-01"));

        // Next month's forecast expects the new salary.
        var (next, _, _) = s.NextMonthPlan(new DateTime(2026, 10, 3), 0);
        Assert.Equal(Tk(90_000), next.Income);

        // Stop from November: no salary any more, nothing expected.
        await _svc.SetSalaryAsync("2026-11", 0, _bank.Id, 1);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 12, 5));
        s = await _svc.LoadAsync();
        Assert.Equal(3, Salaries(s).Count);
        Assert.Equal(0, s.SalaryIn("2026-12"));
    }

    [Fact]
    public async Task A_deleted_or_typed_salary_is_not_added_again()
    {
        var s = await _svc.LoadAsync();
        var salary = s.Categories.Single(c => c.Name == "Salary").Id;
        // September's salary was typed by hand.
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Income, AccountId = _bank.Id, CategoryId = salary, Amount = Tk(80_000), Date = new DateTime(2026, 9, 2) });
        await _svc.SetSalaryAsync("2026-09", Tk(80_000), _bank.Id, 1);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 3));
        s = await _svc.LoadAsync();
        Assert.Equal(2, Salaries(s).Count);   // typed September + automatic October

        // October's automatic salary deleted (not paid): stays deleted.
        await _svc.DeleteTransactionAsync(Salaries(s).Last().Id);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 20));
        Assert.Single(Salaries(await _svc.LoadAsync()));

        // A changed amount doesn't touch the typed one.
        await _svc.SetSalaryAsync("2026-09", Tk(85_000), _bank.Id, 1);
        Assert.Equal(Tk(80_000), Salaries(await _svc.LoadAsync()).Single().Amount);
    }

    [Fact]
    public async Task Backup_keeps_the_salary()
    {
        await _svc.SetSalaryAsync("2026-09", Tk(80_000), _bank.Id, 1);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 3));
        var backup = BackupData.FromJson((await _svc.ExportAsync(DateTime.Now)).ToJson());
        Assert.Single(backup.SalaryRates);

        await _svc.ImportAsync(backup);
        await _svc.GenerateSalaryAsync(new DateTime(2026, 10, 3)); // no duplicates after a restore
        var s = await _svc.LoadAsync();
        Assert.Equal(2, Salaries(s).Count);
        Assert.Equal(Tk(80_000), s.SalaryIn("2026-10"));
    }
}
