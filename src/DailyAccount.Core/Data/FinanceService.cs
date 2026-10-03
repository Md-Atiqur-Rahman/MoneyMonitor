using DailyAccount.Core.Models;
using DailyAccount.Core.Services;
using SQLite;

namespace DailyAccount.Core.Data;

/// <summary>
/// Every read and write the app does. Multi-row writes run in one SQLite transaction so a crash
/// never leaves, say, a payment without its Due update. Rule violations throw <see cref="FinanceException"/>.
/// </summary>
public sealed class FinanceService(FinanceDatabase database)
{
    private SQLiteAsyncConnection Db => database.Connection;

    public async Task<FinanceSnapshot> LoadAsync()
    {
        await database.InitAsync();
        return new FinanceSnapshot(
            await Db.Table<Account>().ToListAsync(),
            await Db.Table<Category>().ToListAsync(),
            await Db.Table<Transaction>().ToListAsync(),
            await Db.Table<Loan>().ToListAsync(),
            await Db.Table<CreditCard>().ToListAsync(),
            await Db.Table<RecurringBill>().ToListAsync(),
            await Db.Table<PersonalDebt>().ToListAsync(),
            await Db.Table<Due>().ToListAsync(),
            await Db.Table<BudgetItem>().ToListAsync(),
            await Db.Table<SalaryRate>().ToListAsync());
    }

    /// <summary>
    /// Creates any missing card statements and recurring-bill dues (through next month).
    /// Safe to call on every app start: generation is idempotent.
    /// </summary>
    public async Task<int> GenerateDuesAsync(DateTime today)
    {
        var s = await LoadAsync();
        var newDues = new List<Due>();
        var updated = new List<Due>();
        var removed = new List<Due>();
        foreach (var card in s.Cards)
        {
            // Existing statements follow late-entered or deleted purchases (ADR 0021).
            var (u, r) = LiabilityEngine.ReconcileCardStatements(card, s.Transactions, s.Dues);
            updated.AddRange(u);
            removed.AddRange(r);
            newDues.AddRange(LiabilityEngine.BuildCardStatements(card, s.Transactions, s.Dues, today));
        }
        var throughMonth = MonthKey.Add(MonthKey.Of(today), 1);
        foreach (var bill in s.Bills)
            newDues.AddRange(LiabilityEngine.BuildBillDues(bill, s.Dues, throughMonth));

        if (newDues.Count + updated.Count + removed.Count > 0)
        {
            await Db.RunInTransactionAsync(c =>
            {
                foreach (var d in updated) c.Update(d);
                foreach (var d in removed) c.Delete(d);
                c.InsertAll(newDues);
            });
        }
        await GenerateSalaryAsync(today); // every page that refreshes dues also gets the month's salary
        return newDues.Count;
    }

    // ---------- Monthly salary (ADR 0035) ----------

    private static string SalaryKey(string month) => "salary:" + month;

    /// <summary>
    /// Adds the salary of every month from the first rate up to <paramref name="today"/> whose pay day has come,
    /// once per month. A month that already has Salary income (typed by hand) is left alone, and a month whose
    /// automatic salary was deleted is not added again. Returns how many were added.
    /// </summary>
    public async Task<int> GenerateSalaryAsync(DateTime today)
    {
        var s = await LoadAsync();
        if (s.SalaryRates is not { Count: > 0 } rates) return 0;
        var salary = s.Categories.FirstOrDefault(c => c.Kind == CategoryKind.Income && c.ParentId == null && c.Name == "Salary")
                     ?? s.Categories.FirstOrDefault(c => c.Kind == CategoryKind.Income && c.ParentId == null);
        var done = (await Db.Table<AppMeta>().ToListAsync()).Where(m => m.Key.StartsWith("salary:")).Select(m => m.Key).ToHashSet();

        var added = 0;
        var current = MonthKey.Of(today);
        for (var month = rates.Min(r => r.FromMonth)!; string.CompareOrdinal(month, current) <= 0; month = MonthKey.Add(month, 1))
        {
            if (done.Contains(SalaryKey(month))) continue;
            var rate = s.SalaryRateIn(month);
            if (rate is not { Amount: > 0 }) continue;
            var payDay = MonthKey.DayIn(month, rate.Day);
            if (payDay > today.Date) continue;

            var typed = s.Transactions.Any(t => t.Type == TransactionType.Income && t.CategoryId == salary?.Id && MonthKey.Contains(month, t.Date));
            var key = SalaryKey(month);
            await Db.RunInTransactionAsync(c =>
            {
                string value = "typed";
                if (!typed)
                {
                    var t = new Transaction
                    {
                        Type = TransactionType.Income, AccountId = rate.AccountId, CategoryId = salary?.Id,
                        Amount = rate.Amount, Date = payDay, Note = "Salary (automatic)"
                    };
                    c.Insert(t);
                    value = t.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    added++;
                }
                c.InsertOrReplace(new AppMeta { Key = key, Value = value });
            });
        }
        return added;
    }

    /// <summary>
    /// Sets the salary from <paramref name="fromMonth"/> on (ADR 0035): the first time, or an increment. Later
    /// rates are replaced by this one, and automatic salary entries already added from that month on take the
    /// new amount and account. <paramref name="amount"/> 0 stops the salary from that month.
    /// </summary>
    public async Task SetSalaryAsync(string fromMonth, long amount, int accountId, int day)
    {
        await database.InitAsync();
        if (amount < 0) throw new FinanceException("Err_Amount");
        if (day is < 1 or > 31) throw new FinanceException("Err_Day");
        if (amount > 0 && await Db.FindAsync<Account>(accountId) is null) throw new FinanceException("Err_Account");

        var rates = await Db.Table<SalaryRate>().ToListAsync();
        var meta = (await Db.Table<AppMeta>().ToListAsync())
            .Where(m => m.Key.StartsWith("salary:") && string.CompareOrdinal(m.Key[7..], fromMonth) >= 0)
            .ToList();
        await Db.RunInTransactionAsync(c =>
        {
            foreach (var r in rates.Where(r => string.CompareOrdinal(r.FromMonth, fromMonth) >= 0)) c.Delete(r);
            c.Insert(new SalaryRate { FromMonth = fromMonth, Amount = amount, AccountId = accountId, Day = day });

            foreach (var m in meta)
            {
                if (!int.TryParse(m.Value, out var id)) continue; // typed by hand: not ours to change
                var t = c.Find<Transaction>(id);
                if (t is null) continue;
                if (amount == 0)
                {
                    c.Delete(t);
                    c.Delete<AppMeta>(m.Key);
                    continue;
                }
                t.Amount = amount;
                t.AccountId = accountId;
                t.Date = MonthKey.DayIn(MonthKey.Of(t.Date), day);
                c.Update(t);
            }
        });
    }

    // ---------- Accounts ----------

    public async Task AddAccountAsync(Account account)
    {
        await database.InitAsync();
        if (string.IsNullOrWhiteSpace(account.Name)) throw new FinanceException("Err_Name");
        account.Name = account.Name.Trim();
        await Db.InsertAsync(account);
    }

    /// <summary>Rename or correct the opening balance of an account.</summary>
    public async Task UpdateAccountAsync(Account account)
    {
        await database.InitAsync();
        if (string.IsNullOrWhiteSpace(account.Name)) throw new FinanceException("Err_Name");
        account.Name = account.Name.Trim();
        if (await Db.UpdateAsync(account) == 0) throw new FinanceException("Err_NotFound");
    }

    // ---------- Day-to-day transactions ----------

    /// <summary>Income, Expense, Transfer or CardPurchase. Liability flows have their own methods.</summary>
    public async Task AddTransactionAsync(Transaction t)
    {
        await database.InitAsync();
        Validate(t);
        await Db.InsertAsync(t);
    }

    /// <summary>Several lines of one shopping trip (Rice 5kg 450, Eggs 30 380…) saved together (ADR 0013).</summary>
    public async Task AddTransactionsAsync(IReadOnlyCollection<Transaction> lines)
    {
        await database.InitAsync();
        if (lines.Count == 0) throw new FinanceException("Err_Amount");
        foreach (var t in lines) Validate(t);
        await Db.RunInTransactionAsync(c => c.InsertAll(lines));
    }

    /// <summary>
    /// Changes an existing income, expense, transfer or card purchase (ADR 0026). The kind can't change.
    /// Bill payments and borrow/lend records can't be edited (delete and enter again). For a card purchase
    /// on a statement, the statement follows the change, but never below what was already paid on it.
    /// </summary>
    public async Task UpdateTransactionAsync(Transaction updated)
    {
        var s = await LoadAsync();
        var existing = s.Transactions.FirstOrDefault(x => x.Id == updated.Id) ?? throw new FinanceException("Err_NotFound");
        // The kind may change between these four (e.g. an expense that was really a card purchase).
        static bool Editable(TransactionType t) => t is TransactionType.Income or TransactionType.Expense
            or TransactionType.Transfer or TransactionType.CardPurchase;
        if (!Editable(existing.Type) || !Editable(updated.Type))
            throw new FinanceException("Err_EditNotAllowed");
        Validate(updated);

        if (existing.Type == TransactionType.CardPurchase || updated.Type == TransactionType.CardPurchase)
        {
            // Check every statement the old or new version belongs to: the total after the change must
            // still cover what was paid on it.
            var after = s.Transactions.Where(t => t.Id != existing.Id).Append(updated).ToList();
            foreach (var card in s.Cards.Where(c => c.Id == existing.CardId || c.Id == updated.CardId))
            {
                foreach (var statement in s.Dues.Where(d => d.SourceType == DueSource.Card && d.SourceId == card.Id && d.PaidAmount > 0))
                {
                    var total = after.Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id
                            && LiabilityEngine.CycleStart(card, t.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) == statement.PeriodKey)
                        .Sum(t => t.Amount);
                    if (total < statement.PaidAmount) throw new FinanceException("Err_CardBilled");
                }
            }
        }

        await Db.UpdateAsync(updated);
        if (existing.Type == TransactionType.CardPurchase || updated.Type == TransactionType.CardPurchase)
            await GenerateDuesAsync(DateTime.Today); // statements follow their purchases (ADR 0021)
    }

    private static void Validate(Transaction t)
    {
        if (t.Amount <= 0) throw new FinanceException("Err_Amount");
        t.ItemName = string.IsNullOrWhiteSpace(t.ItemName) ? null : t.ItemName.Trim();
        t.Quantity = string.IsNullOrWhiteSpace(t.Quantity) ? null : t.Quantity.Trim();

        switch (t.Type)
        {
            case TransactionType.Income:
            case TransactionType.Expense:
                if (t.AccountId is null) throw new FinanceException("Err_Account");
                t.CardId = null;
                t.ToAccountId = null;
                break;
            case TransactionType.Transfer:
                if (t.AccountId is null || t.ToAccountId is null) throw new FinanceException("Err_Account");
                if (t.AccountId == t.ToAccountId) throw new FinanceException("Err_SameAccount");
                t.CardId = null;
                t.CategoryId = null;
                break;
            case TransactionType.CardPurchase:
                if (t.CardId is null) throw new FinanceException("Err_Card");
                t.AccountId = null;
                t.ToAccountId = null;
                break;
            default:
                throw new FinanceException("Err_Type");
        }
    }

    /// <summary>
    /// Deletes a transaction and undoes its effects. A due payment gives the amount back to the Due.
    /// A card purchase already on a statement reduces that statement, as long as enough of the statement
    /// is still unpaid; a statement that drops to 0 is removed (ADR 0021). Borrow/lend records can't be
    /// deleted here.
    /// </summary>
    public async Task DeleteTransactionAsync(int transactionId)
    {
        var s = await LoadAsync();
        var t = s.Transactions.FirstOrDefault(x => x.Id == transactionId) ?? throw new FinanceException("Err_NotFound");

        switch (t.Type)
        {
            case TransactionType.DuePayment:
                var due = s.Dues.FirstOrDefault(d => d.Id == t.DueId);
                await Db.RunInTransactionAsync(c =>
                {
                    if (due is not null)
                    {
                        due.PaidAmount = Math.Max(0, due.PaidAmount - t.Amount);
                        due.Status = due.PaidAmount == 0 ? DueStatus.Pending
                            : due.PaidAmount >= due.Amount ? DueStatus.Paid : DueStatus.Partial;
                        c.Update(due);
                    }
                    c.Delete(t);
                });
                return;

            case TransactionType.CardPurchase:
                var card = s.Cards.FirstOrDefault(c => c.Id == t.CardId);
                var key = card is null ? null
                    : LiabilityEngine.CycleStart(card, t.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var statement = s.Dues.FirstOrDefault(d => d.SourceType == DueSource.Card && d.SourceId == card?.Id && d.PeriodKey == key);
                if (statement is null) break; // not billed yet: just delete it

                // Billed: take it off the statement, but never below what was already paid on it.
                if (statement.Remaining < t.Amount) throw new FinanceException("Err_CardBilled");
                await Db.RunInTransactionAsync(c =>
                {
                    statement.Amount -= t.Amount;
                    if (statement.Amount == 0)
                        c.Delete(statement);
                    else
                    {
                        statement.Status = statement.PaidAmount == 0 ? DueStatus.Pending
                            : statement.PaidAmount >= statement.Amount ? DueStatus.Paid : DueStatus.Partial;
                        c.Update(statement);
                    }
                    c.Delete(t);
                });
                return;

            case TransactionType.BorrowIn:
            case TransactionType.LendOut:
                throw new FinanceException("Err_DeleteDebtTx");
        }

        await Db.DeleteAsync(t);
    }

    // ---------- Loans ----------

    /// <summary>
    /// Saves the loan and its full installment schedule in one go. A loan without installments is one amount
    /// due in its start month (ADR 0034). With <paramref name="receivedIn"/> the money came in now: it is added to
    /// that account on <paramref name="receivedOn"/> as borrowed money, so it counts in that month's income.
    /// </summary>
    public async Task<Loan> AddLoanAsync(Loan loan, int? receivedIn = null, DateTime? receivedOn = null)
    {
        await database.InitAsync();
        if (loan.NoInstallments)
        {
            loan.InstallmentCount = 1;
            loan.InstallmentsPaidBefore = 0;
        }
        if (string.IsNullOrWhiteSpace(loan.Lender)) throw new FinanceException("Err_Name");
        if (loan.TotalPayable <= 0) throw new FinanceException("Err_Amount");
        if (loan.InstallmentCount is < 1 or > 600) throw new FinanceException("Err_Installments");
        if (loan.InstallmentsPaidBefore < 0 || loan.InstallmentsPaidBefore >= loan.InstallmentCount)
            throw new FinanceException("Err_PaidBefore");
        if (loan.Principal <= 0) loan.Principal = loan.TotalPayable;
        loan.Lender = loan.Lender.Trim();

        await Db.RunInTransactionAsync(c =>
        {
            c.Insert(loan);
            c.InsertAll(LiabilityEngine.BuildLoanSchedule(loan));
            if (receivedIn is not null)
                c.Insert(new Transaction
                {
                    Type = TransactionType.BorrowIn, AccountId = receivedIn, LoanId = loan.Id, Amount = loan.Principal,
                    Date = receivedOn ?? DateTime.Today, Note = loan.Lender
                });
        });
        return loan;
    }

    /// <summary>
    /// Moves what is still unpaid of a loan to start in <paramref name="month"/> (ADR 0034): a loan without
    /// installments gets that pay month; an EMI's unpaid installments follow monthly from it.
    /// </summary>
    public async Task MoveLoanAsync(int loanId, string month)
    {
        var s = await LoadAsync();
        var loan = s.Loans.FirstOrDefault(l => l.Id == loanId) ?? throw new FinanceException("Err_NotFound");
        var open = s.Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loanId && d.PaidAmount == 0)
            .OrderBy(d => d.Sequence).ToList();
        if (open.Count == 0) throw new FinanceException("Err_LoanNothingOpen");

        for (var i = 0; i < open.Count; i++)
        {
            open[i].DueMonth = MonthKey.Add(month, i);
            open[i].DueDate = MonthKey.DayIn(open[i].DueMonth, loan.DueDay);
        }
        if (open[0].Sequence == 1) loan.StartMonth = month;
        await Db.RunInTransactionAsync(c =>
        {
            foreach (var d in open) c.Update(d);
            c.Update(loan);
        });
    }

    /// <summary>
    /// Turns a loan without installments into EMI (ADR 0034): <paramref name="total"/> (what is unpaid, or more
    /// with the bank's interest) in <paramref name="count"/> installments from <paramref name="month"/>.
    /// Whatever was already paid stays as paid.
    /// </summary>
    public async Task ConvertLoanToEmiAsync(int loanId, long total, int count, string month)
    {
        var s = await LoadAsync();
        var loan = s.Loans.FirstOrDefault(l => l.Id == loanId) ?? throw new FinanceException("Err_NotFound");
        if (!loan.NoInstallments) throw new FinanceException("Err_AlreadyEmi");
        if (total <= 0) throw new FinanceException("Err_Amount");
        if (count is < 1 or > 600) throw new FinanceException("Err_Installments");

        var dues = s.Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loanId).ToList();
        var paid = dues.Sum(d => d.PaidAmount);
        if (dues.All(d => d.Status == DueStatus.Paid)) throw new FinanceException("Err_LoanNothingOpen");

        loan.NoInstallments = false;
        loan.TotalPayable = paid + total;
        loan.InstallmentCount = count + (paid > 0 ? 1 : 0);
        loan.StartMonth = month;
        var installments = LiabilityEngine.BuildInstallments(loan, paid > 0 ? 2 : 1, month, total, count);

        await Db.RunInTransactionAsync(c =>
        {
            foreach (var d in dues)
            {
                if (d.PaidAmount == 0) { c.Delete(d); continue; }
                // A part already paid stays as installment 1, closed at what was paid.
                d.Amount = d.PaidAmount;
                d.Status = DueStatus.Paid;
                c.Update(d);
            }
            c.InsertAll(installments);
            c.Update(loan);
        });
    }

    /// <summary>Removes a loan only if no payment was made in the app (to fix a typo).</summary>
    public async Task DeleteLoanAsync(int loanId)
    {
        var s = await LoadAsync();
        var dues = s.Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loanId).ToList();
        var dueIds = dues.Select(d => d.Id).ToHashSet();
        if (s.Transactions.Any(t => t.DueId is { } id && dueIds.Contains(id))) throw new FinanceException("Err_LoanHasPayments");
        var received = s.Transactions.Where(t => t.Type == TransactionType.BorrowIn && t.LoanId == loanId).ToList();
        await Db.RunInTransactionAsync(c =>
        {
            foreach (var d in dues) c.Delete(d);
            foreach (var t in received) c.Delete(t); // the money it brought in goes with it (ADR 0034)
            c.Delete<Loan>(loanId);
        });
    }

    // ---------- Credit cards ----------

    public async Task AddCardAsync(CreditCard card, DateTime today)
    {
        await database.InitAsync();
        ValidateCard(card);
        await Db.InsertAsync(card);
        await GenerateDuesAsync(today);
    }

    /// <summary>Edit name, limit, days or paying account. Existing statements keep their dates.</summary>
    public async Task UpdateCardAsync(CreditCard card)
    {
        await database.InitAsync();
        ValidateCard(card);
        if (await Db.UpdateAsync(card) == 0) throw new FinanceException("Err_NotFound");
    }

    private static void ValidateCard(CreditCard card)
    {
        if (string.IsNullOrWhiteSpace(card.Name)) throw new FinanceException("Err_Name");
        if (card.StatementDay is < 1 or > 28 || card.DueDay is < 1 or > 31) throw new FinanceException("Err_Day");
        if (card.CreditLimit < 0) throw new FinanceException("Err_Amount");
        card.Name = card.Name.Trim();
    }

    /// <summary>
    /// Pays the card bill for <paramref name="month"/>: unpaid statements and card EMIs up to that month,
    /// oldest first. One DuePayment per Due, so deleting a payment still restores exactly that Due.
    /// </summary>
    public async Task PayCardBillAsync(int cardId, string month, long amount, int accountId, DateTime date)
    {
        var s = await LoadAsync();
        if (amount <= 0) throw new FinanceException("Err_Amount");
        var bill = s.CardBillDues(cardId, month);
        if (amount > bill.Sum(d => d.Remaining)) throw new FinanceException("Err_TooMuch");

        var payments = new List<Transaction>();
        var left = amount;
        foreach (var due in bill)
        {
            if (left == 0) break;
            var part = Math.Min(left, due.Remaining);
            payments.Add(LiabilityEngine.ApplyPayment(due, part, accountId, date));
            left -= part;
        }

        await Db.RunInTransactionAsync(c =>
        {
            foreach (var due in bill) c.Update(due);
            c.InsertAll(payments);
        });
    }

    // ---------- Categories ----------

    /// <summary>Adds a category (or a sub-category when <paramref name="parentId"/> is set). Names are unique per parent.</summary>
    public async Task<Category> AddCategoryAsync(string name, CategoryKind kind, int? parentId)
    {
        var s = await LoadAsync();
        if (string.IsNullOrWhiteSpace(name)) throw new FinanceException("Err_Name");
        name = name.Trim();

        if (parentId is { } pid)
        {
            var parent = s.Categories.FirstOrDefault(c => c.Id == pid) ?? throw new FinanceException("Err_NotFound");
            if (parent.ParentId is not null) throw new FinanceException("Err_SubOfSub");
            kind = parent.Kind;
        }
        // Unique per kind across all levels (ADR 0027): no "Fish" under both Bajar and Others.
        if (s.FindCategory(name, kind) is not null) throw new FinanceException("Err_CategoryExists");

        var category = new Category
        {
            Name = name, Kind = kind, ParentId = parentId,
            SortOrder = s.Categories.Where(c => c.ParentId == parentId).Select(c => c.SortOrder).DefaultIfEmpty(-1).Max() + 1
        };
        await Db.InsertAsync(category);
        return category;
    }

    public async Task RenameCategoryAsync(int categoryId, string name)
    {
        await database.InitAsync();
        if (string.IsNullOrWhiteSpace(name)) throw new FinanceException("Err_Name");
        var category = await Db.FindAsync<Category>(categoryId) ?? throw new FinanceException("Err_NotFound");
        var all = await Db.Table<Category>().ToListAsync();
        if (all.Any(c => c.Kind == category.Kind && c.Id != categoryId && Category.SameName(c, name)))
            throw new FinanceException("Err_CategoryExists");
        category.Name = name.Trim();
        category.NameBn = null; // the typed name is used in both languages from now on
        await Db.UpdateAsync(category);
    }

    /// <summary>
    /// Deletes a category (ADR 0016).
    /// Sub-category: its entries move to the parent, then it is removed.
    /// Main category: only when neither it nor its sub-categories have entries; its sub-categories and
    /// its budget lines in every month are removed with it. The last main category of a kind stays.
    /// </summary>
    public async Task DeleteCategoryAsync(int categoryId)
    {
        var s = await LoadAsync();
        var category = s.Categories.FirstOrDefault(c => c.Id == categoryId) ?? throw new FinanceException("Err_NotFound");

        if (category.ParentId is { } parentId)
        {
            var moved = s.Transactions.Where(t => t.CategoryId == categoryId).ToList();
            await Db.RunInTransactionAsync(c =>
            {
                foreach (var t in moved)
                {
                    t.CategoryId = parentId;
                    c.Update(t);
                }
                c.Delete(category);
            });
            return;
        }

        var children = s.Categories.Where(c => c.ParentId == categoryId).ToList();
        var ids = children.Select(c => c.Id).Append(categoryId).ToHashSet();
        if (s.Transactions.Any(t => t.CategoryId is { } id && ids.Contains(id))) throw new FinanceException("Err_CategoryInUse");
        if (s.TopCategories(category.Kind).Count() <= 1) throw new FinanceException("Err_LastCategory");

        var budgetLines = s.Budget.Where(b => b.CategoryId == categoryId).ToList();
        await Db.RunInTransactionAsync(c =>
        {
            foreach (var b in budgetLines) c.Delete(b);
            foreach (var child in children) c.Delete(child);
            c.Delete(category);
        });
    }

    // ---------- Budget (ADR 0011) ----------

    /// <summary>
    /// Makes sure <paramref name="month"/> has a budget: if it has none, copies the **repeating** lines
    /// (not "only this month") of the latest earlier month that has a budget (ADR 0011, 0019).
    /// Returns true when it copied something.
    /// </summary>
    public async Task<bool> EnsureBudgetAsync(string month)
    {
        await database.InitAsync();
        if (await Db.Table<BudgetItem>().Where(b => b.Month == month).CountAsync() > 0) return false;

        var source = (await Db.Table<BudgetItem>().ToListAsync())
            .Where(b => string.CompareOrdinal(b.Month, month) < 0)
            .GroupBy(b => b.Month)
            .OrderByDescending(g => g.Key)
            .FirstOrDefault();
        var copies = source?.Where(b => !b.OnlyThisMonth)
            .Select(b => new BudgetItem { Month = month, CategoryId = b.CategoryId, Estimate = b.Estimate })
            .ToList() ?? [];
        if (copies.Count == 0) return false;

        await Db.InsertAllAsync(copies);
        return true;
    }

    /// <summary>
    /// Makes sure every month from <paramref name="fromMonth"/> to <paramref name="toMonth"/> has a budget
    /// (ADR 0025), so data can start in an earlier month. A month without a budget gets the Monthly
    /// lines of the nearest earlier month that has one; months before the first budget get the Monthly
    /// lines of the first budget month (filled backwards). Months that already have a budget are kept.
    /// Returns how many months were filled.
    /// </summary>
    public async Task<int> FillBudgetMonthsAsync(string fromMonth, string toMonth)
    {
        await database.InitAsync();
        if (string.CompareOrdinal(fromMonth, toMonth) > 0) return 0;
        var budget = await Db.Table<BudgetItem>().ToListAsync();
        var filled = 0;
        var inserts = new List<BudgetItem>();

        for (var month = fromMonth; string.CompareOrdinal(month, toMonth) <= 0; month = MonthKey.Add(month, 1))
        {
            if (budget.Any(b => b.Month == month)) continue;
            var source = budget.Where(b => string.CompareOrdinal(b.Month, month) < 0).GroupBy(b => b.Month).OrderByDescending(g => g.Key).FirstOrDefault()
                         ?? budget.Where(b => string.CompareOrdinal(b.Month, month) > 0).GroupBy(b => b.Month).OrderBy(g => g.Key).FirstOrDefault();
            if (source is null) continue;

            var lines = source.Where(b => !b.OnlyThisMonth)
                .Select(b => new BudgetItem { Month = month, CategoryId = b.CategoryId, Estimate = b.Estimate })
                .ToList();
            if (lines.Count == 0) continue;
            inserts.AddRange(lines);
            budget.AddRange(lines); // later months in the loop copy from this one
            filled++;
        }

        if (inserts.Count > 0) await Db.InsertAllAsync(inserts);
        return filled;
    }

    /// <summary>
    /// Strikes a budget item through for one month — "won't pay this month" — or undoes it (ADR 0031).
    /// Only that month changes; the item repeats into later months as before.
    /// </summary>
    public async Task SetBudgetSkippedAsync(string month, int categoryId, bool skipped)
    {
        await database.InitAsync();
        var item = await Db.Table<BudgetItem>().FirstOrDefaultAsync(b => b.Month == month && b.CategoryId == categoryId)
                   ?? throw new FinanceException("Err_NotFound");
        item.Skipped = skipped;
        await Db.UpdateAsync(item);
    }

    private const string DefaultBudgetKey = "default-budget-v1";

    /// <summary>
    /// One time per database (ADR 0020): adds the fixed default budget (<see cref="FinanceDatabase.DefaultBudget"/>)
    /// to <paramref name="month"/> and to later months that already have a budget, as Monthly lines.
    /// Missing categories (e.g. DPS, Education) are created. Lines the user already has are left as they
    /// are. Returns true when it ran.
    /// </summary>
    public async Task<bool> ApplyDefaultBudgetAsync(string month)
    {
        await database.InitAsync();
        if (await Db.FindAsync<AppMeta>(DefaultBudgetKey) is not null) return false;
        await EnsureBudgetAsync(month);

        var categories = await Db.Table<Category>().ToListAsync();
        var budget = await Db.Table<BudgetItem>().ToListAsync();
        var months = budget.Select(b => b.Month).Where(m => string.CompareOrdinal(m, month) > 0)
            .Append(month).Distinct().ToList();
        var nextOrder = categories.Where(c => c.ParentId is null).Select(c => c.SortOrder).DefaultIfEmpty(0).Max() + 1;

        await Db.RunInTransactionAsync(c =>
        {
            foreach (var (name, bn, estimate) in FinanceDatabase.DefaultBudget)
            {
                var category = categories.FirstOrDefault(x => x.ParentId is null && x.Kind == CategoryKind.Expense
                                                              && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (category is null)
                {
                    category = new Category { Name = name, NameBn = bn, Kind = CategoryKind.Expense, SortOrder = nextOrder++ };
                    c.Insert(category);
                }

                foreach (var m in months)
                {
                    if (!budget.Any(b => b.Month == m && b.CategoryId == category.Id))
                        c.Insert(new BudgetItem { Month = m, CategoryId = category.Id, Estimate = Money.FromTaka(estimate) });
                }
            }
            c.Insert(new AppMeta { Key = DefaultBudgetKey, Value = month });
        });
        return true;
    }

    /// <summary>
    /// Sets (or adds) the estimate of one category for one month (ADR 0019).
    /// <paramref name="onlyThisMonth"/>: null keeps the line's current setting (new lines repeat).
    /// A repeating line is also written into every later month that already has a budget, so a change
    /// "from now on" doesn't have to be repeated month by month; a one-off line is removed from them.
    /// </summary>
    public async Task SetBudgetAsync(string month, int categoryId, long estimate, bool? onlyThisMonth = null)
    {
        await database.InitAsync();
        if (estimate < 0) throw new FinanceException("Err_Amount");
        var category = await Db.FindAsync<Category>(categoryId) ?? throw new FinanceException("Err_NotFound");
        if (category.ParentId is not null || category.Kind != CategoryKind.Expense) throw new FinanceException("Err_BudgetCategory");

        var all = await Db.Table<BudgetItem>().ToListAsync();
        var existing = all.FirstOrDefault(b => b.Month == month && b.CategoryId == categoryId);
        var oneOff = onlyThisMonth ?? existing?.OnlyThisMonth ?? false;
        var laterMonths = all.Where(b => string.CompareOrdinal(b.Month, month) > 0).Select(b => b.Month).Distinct().ToList();

        await Db.RunInTransactionAsync(c =>
        {
            if (existing is null)
                c.Insert(new BudgetItem { Month = month, CategoryId = categoryId, Estimate = estimate, OnlyThisMonth = oneOff });
            else
            {
                existing.Estimate = estimate;
                existing.OnlyThisMonth = oneOff;
                c.Update(existing);
            }

            foreach (var later in laterMonths)
            {
                var line = all.FirstOrDefault(b => b.Month == later && b.CategoryId == categoryId);
                if (oneOff)
                {
                    if (line is not null) c.Delete(line);
                }
                else if (line is null)
                    c.Insert(new BudgetItem { Month = later, CategoryId = categoryId, Estimate = estimate });
                else
                {
                    line.Estimate = estimate;
                    line.OnlyThisMonth = false;
                    c.Update(line);
                }
            }
        });
    }

    /// <summary>
    /// Removes a category from the budget of <paramref name="month"/> and of every later month that
    /// already has a budget, so it doesn't come back next month (ADR 0019).
    /// </summary>
    public async Task RemoveBudgetAsync(string month, int categoryId)
    {
        await database.InitAsync();
        var lines = (await Db.Table<BudgetItem>().Where(b => b.CategoryId == categoryId).ToListAsync())
            .Where(b => string.CompareOrdinal(b.Month, month) >= 0)
            .ToList();
        await Db.RunInTransactionAsync(c =>
        {
            foreach (var line in lines) c.Delete(line);
        });
    }

    // ---------- Recurring bills ----------

    public async Task AddBillAsync(RecurringBill bill, DateTime today)
    {
        await database.InitAsync();
        if (string.IsNullOrWhiteSpace(bill.Name)) throw new FinanceException("Err_Name");
        if (bill.Amount <= 0) throw new FinanceException("Err_Amount");
        if (bill.DayOfMonth is < 1 or > 31) throw new FinanceException("Err_Day");
        if (string.IsNullOrEmpty(bill.StartMonth)) bill.StartMonth = MonthKey.Of(today);
        await Db.InsertAsync(bill);
        await GenerateDuesAsync(today);
    }

    /// <summary>Stops a bill from <paramref name="fromMonth"/> on; unpaid dues from that month are removed.</summary>
    public async Task StopBillAsync(int billId, string fromMonth)
    {
        var s = await LoadAsync();
        var bill = s.Bills.FirstOrDefault(b => b.Id == billId) ?? throw new FinanceException("Err_NotFound");
        var toRemove = s.Dues.Where(d => d.SourceType == DueSource.Bill && d.SourceId == billId
                                         && string.CompareOrdinal(d.DueMonth, fromMonth) >= 0 && d.PaidAmount == 0).ToList();
        bill.IsActive = false;
        bill.EndMonth = MonthKey.Add(fromMonth, -1);
        await Db.RunInTransactionAsync(c =>
        {
            foreach (var d in toRemove) c.Delete(d);
            c.Update(bill);
        });
    }

    // ---------- Personal borrowing / lending ----------

    /// <summary>Borrowed: money comes into AccountId (if set) and a Due is created. Lent: money leaves AccountId.</summary>
    public async Task AddPersonalDebtAsync(PersonalDebt debt)
    {
        await database.InitAsync();
        if (string.IsNullOrWhiteSpace(debt.PersonName)) throw new FinanceException("Err_Name");
        if (debt.Amount <= 0) throw new FinanceException("Err_Amount");
        if (debt.Direction == DebtDirection.Lent && debt.AccountId is null) throw new FinanceException("Err_Account");
        debt.PersonName = debt.PersonName.Trim();

        await Db.RunInTransactionAsync(c =>
        {
            c.Insert(debt);
            if (debt.AccountId is not null)
            {
                c.Insert(new Transaction
                {
                    Date = debt.Date,
                    Amount = debt.Amount,
                    Type = debt.Direction == DebtDirection.Borrowed ? TransactionType.BorrowIn : TransactionType.LendOut,
                    AccountId = debt.AccountId,
                    DebtId = debt.Id,
                    Note = debt.Note ?? debt.PersonName
                });
            }
            if (debt.Direction == DebtDirection.Borrowed)
                c.Insert(LiabilityEngine.BuildPersonalDue(debt));
        });
    }

    /// <summary>
    /// Sets or changes the month borrowed money is to be paid back (ADR 0030); null = no month yet.
    /// The debt is due on that month's last day, and counts in that month's dues from then on.
    /// </summary>
    public async Task SetDebtPayMonthAsync(int debtId, string? month)
    {
        var s = await LoadAsync();
        var debt = s.Debts.FirstOrDefault(d => d.Id == debtId && d.Direction == DebtDirection.Borrowed)
                   ?? throw new FinanceException("Err_NotFound");
        var due = s.DueFor(DueSource.Personal, debtId) ?? throw new FinanceException("Err_NotFound");

        debt.ExpectedReturnDate = month is null ? null : MonthKey.LastDay(month);
        due.DueMonth = month ?? "";
        due.DueDate = debt.ExpectedReturnDate;
        await Db.RunInTransactionAsync(c =>
        {
            c.Update(debt);
            c.Update(due);
        });
    }

    /// <summary>Someone returns (part of) the money you lent them.</summary>
    public async Task ReceiveLendReturnAsync(int debtId, long amount, int accountId, DateTime date)
    {
        var s = await LoadAsync();
        var debt = s.Debts.FirstOrDefault(d => d.Id == debtId && d.Direction == DebtDirection.Lent)
                   ?? throw new FinanceException("Err_NotFound");
        if (amount <= 0) throw new FinanceException("Err_Amount");
        if (amount > s.LendRemaining(debt)) throw new FinanceException("Err_TooMuch");

        await Db.InsertAsync(new Transaction
        {
            Date = date, Amount = amount, Type = TransactionType.LendReturn,
            AccountId = accountId, DebtId = debtId, Note = debt.PersonName
        });
    }

    // ---------- Paying dues ----------

    public async Task PayDueAsync(int dueId, long amount, int accountId, DateTime date)
    {
        await database.InitAsync();
        var due = await Db.FindAsync<Due>(dueId) ?? throw new FinanceException("Err_NotFound");
        if (amount <= 0) throw new FinanceException("Err_Amount");
        if (amount > due.Remaining) throw new FinanceException("Err_TooMuch");

        var payment = LiabilityEngine.ApplyPayment(due, amount, accountId, date);
        await Db.RunInTransactionAsync(c =>
        {
            c.Update(due);
            c.Insert(payment);
        });
    }

    // ---------- Backup ----------

    public async Task<BackupData> ExportAsync(DateTime now)
    {
        var s = await LoadAsync();
        return new BackupData
        {
            CreatedAt = now,
            Accounts = s.Accounts, Categories = s.Categories, Transactions = s.Transactions,
            Loans = s.Loans, Cards = s.Cards, Bills = s.Bills, Debts = s.Debts, Dues = s.Dues,
            Budget = s.Budget, SalaryRates = s.SalaryRates ?? []
        };
    }

    /// <summary>Replaces ALL data with the backup, keeping the original Ids so links stay intact.</summary>
    public async Task ImportAsync(BackupData data)
    {
        await database.InitAsync();
        await Db.RunInTransactionAsync(c =>
        {
            c.DeleteAll<BudgetItem>();
            c.DeleteAll<SalaryRate>();
            c.Execute("DELETE FROM AppMeta WHERE Key LIKE 'salary:%'"); // the backup's months are marked again below
            c.DeleteAll<Due>();
            c.DeleteAll<Transaction>();
            c.DeleteAll<PersonalDebt>();
            c.DeleteAll<RecurringBill>();
            c.DeleteAll<CreditCard>();
            c.DeleteAll<Loan>();
            c.DeleteAll<Category>();
            c.DeleteAll<Account>();

            // InsertOrReplace writes the primary key too; plain Insert would renumber AutoIncrement ids.
            foreach (var x in data.Accounts) c.InsertOrReplace(x);
            foreach (var x in data.Categories) c.InsertOrReplace(x);
            foreach (var x in data.Loans) c.InsertOrReplace(x);
            foreach (var x in data.Cards) c.InsertOrReplace(x);
            foreach (var x in data.Bills) c.InsertOrReplace(x);
            foreach (var x in data.Debts) c.InsertOrReplace(x);
            foreach (var x in data.Dues) c.InsertOrReplace(x);
            foreach (var x in data.Transactions) c.InsertOrReplace(x);
            foreach (var x in data.Budget) c.InsertOrReplace(x);
            foreach (var x in data.SalaryRates) c.InsertOrReplace(x);
        });
    }
}
