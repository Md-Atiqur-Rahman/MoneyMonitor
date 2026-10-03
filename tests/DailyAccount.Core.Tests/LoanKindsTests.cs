using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0034: card loans with and without installments, money received, moving, converting to EMI.</summary>
public sealed class LoanKindsTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-loans-{Guid.NewGuid():N}.db3");
    private FinanceDatabase _db = null!;
    private FinanceService _svc = null!;
    private Account _bank = null!;
    private CreditCard _card = null!;

    public async Task InitializeAsync()
    {
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        _bank = new Account { Name = "Bank", Type = AccountType.Bank };
        await _svc.AddAccountAsync(_bank);
        _card = new CreditCard { Name = "Card", CreditLimit = Tk(320_000), StatementDay = 1, DueDay = 15, PayFromAccountId = _bank.Id };
        await _svc.AddCardAsync(_card, new DateTime(2026, 10, 1));
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        File.Delete(_path);
    }

    private List<Due> Dues(FinanceSnapshot s, Loan loan) =>
        s.Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loan.Id).OrderBy(d => d.Sequence).ToList();

    [Fact]
    public async Task An_emi_loan_brings_its_money_in_and_starts_later()
    {
        var loan = await _svc.AddLoanAsync(new Loan
        {
            Lender = "Card loan", TotalPayable = Tk(60_000), InstallmentCount = 6, StartMonth = "2026-11", DueDay = 15, CardId = _card.Id
        }, _bank.Id, new DateTime(2026, 10, 5));
        var s = await _svc.LoadAsync();

        Assert.Equal(Tk(60_000), s.Balance(_bank));
        var plan = s.Plan("2026-10", 0);
        Assert.Equal(Tk(60_000), plan.Borrowed);                     // counts in October's income
        var line = Assert.Single(s.CashFlow("2026-10").In);
        Assert.Equal(loan.Id, line.LoanId);

        Assert.Null(s.LoanIn(loan, "2026-10").Installment);          // pending: nothing due in October
        Assert.Equal(Tk(10_000), s.LoanIn(loan, "2026-11").Installment!.Amount);
        Assert.Equal(Tk(10_000), s.CardNextMonth(_card, "2026-10", new DateTime(2026, 10, 5)).Emi);

        // Deleting the loan takes its money out again.
        await _svc.DeleteLoanAsync(loan.Id);
        Assert.Equal(0, (await _svc.LoadAsync()).Balance(_bank));
    }

    [Fact]
    public async Task A_loan_without_installments_is_one_amount_on_that_months_card_bill_and_can_move()
    {
        var loan = await _svc.AddLoanAsync(new Loan
        {
            Lender = "Cash loan", TotalPayable = Tk(30_000), NoInstallments = true, InstallmentCount = 6, StartMonth = "2026-11", DueDay = 15, CardId = _card.Id
        }, _bank.Id, new DateTime(2026, 10, 5));
        var s = await _svc.LoadAsync();
        var due = Assert.Single(Dues(s, loan));
        Assert.Equal(Tk(30_000), due.Amount);
        Assert.Equal("2026-11", due.DueMonth);
        Assert.Equal(1, s.Loans.Single().InstallmentCount);
        Assert.Equal(Tk(30_000), s.CardBillDues(_card.Id, "2026-11").Sum(d => d.Remaining)); // on November's card bill
        Assert.Empty(s.CardBillDues(_card.Id, "2026-10"));

        await _svc.MoveLoanAsync(loan.Id, "2027-01");
        s = await _svc.LoadAsync();
        Assert.Equal("2027-01", Dues(s, loan).Single().DueMonth);
        Assert.Equal(new DateTime(2027, 1, 15), Dues(s, loan).Single().DueDate);
        Assert.Empty(s.CardBillDues(_card.Id, "2026-12"));
    }

    [Fact]
    public async Task Converting_to_emi_splits_the_new_total_from_the_chosen_month()
    {
        var loan = await _svc.AddLoanAsync(new Loan
        {
            Lender = "Cash loan", TotalPayable = Tk(30_000), NoInstallments = true, StartMonth = "2026-11", DueDay = 15, CardId = _card.Id
        });
        await _svc.ConvertLoanToEmiAsync(loan.Id, Tk(31_500), 3, "2026-12");
        var s = await _svc.LoadAsync();
        var dues = Dues(s, loan);
        Assert.Equal([1, 2, 3], dues.Select(d => d.Sequence));
        Assert.Equal(["2026-12", "2027-01", "2027-02"], dues.Select(d => d.DueMonth));
        Assert.Equal(Tk(31_500), dues.Sum(d => d.Amount));
        var converted = s.Loans.Single();
        Assert.False(converted.NoInstallments);
        Assert.Equal(3, converted.InstallmentCount);
        Assert.Empty(s.CardBillDues(_card.Id, "2026-11"));       // nothing left in the old month

        // The effective month can still be changed.
        await _svc.MoveLoanAsync(loan.Id, "2027-02");
        Assert.Equal(["2027-02", "2027-03", "2027-04"], Dues(await _svc.LoadAsync(), loan).Select(d => d.DueMonth));

        // Only a loan without installments converts.
        Assert.Equal("Err_AlreadyEmi", (await Assert.ThrowsAsync<FinanceException>(
            () => _svc.ConvertLoanToEmiAsync(loan.Id, Tk(1), 1, "2027-02"))).Key);
    }

    [Fact]
    public async Task A_partly_paid_loan_keeps_what_was_paid_when_converted()
    {
        var loan = await _svc.AddLoanAsync(new Loan
        {
            Lender = "Cash loan", TotalPayable = Tk(30_000), NoInstallments = true, StartMonth = "2026-10", DueDay = 15
        });
        var s = await _svc.LoadAsync();
        await _svc.PayDueAsync(Dues(s, loan).Single().Id, Tk(10_000), _bank.Id, new DateTime(2026, 10, 15));

        await _svc.ConvertLoanToEmiAsync(loan.Id, Tk(20_000), 2, "2026-11");
        s = await _svc.LoadAsync();
        var dues = Dues(s, loan);
        Assert.Equal(3, dues.Count);
        Assert.Equal(DueStatus.Paid, dues[0].Status);
        Assert.Equal(Tk(10_000), dues[0].Amount);
        Assert.Equal(Tk(30_000), s.Loans.Single().TotalPayable);
        Assert.Equal(1, s.LoanIn(loan, "2026-11").PaidCount);
        Assert.Equal(2, s.LoanIn(loan, "2026-11").Installment!.Sequence);
    }
}
