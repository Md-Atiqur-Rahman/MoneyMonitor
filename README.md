# Daily Account (দৈনিক হিসাব)

A personal, offline Android app to track monthly income, expenses and liabilities (loan
installments, credit-card bills, recurring bills, personal borrowing), and to see savings, total
debt, every account's balance and whether you'll need to borrow next month. English and Bangla.

## Structure
```
src/DailyAccount.Core/        Models, calculations, SQLite data access (no MAUI → unit-testable)
src/DailyAccount.App/         .NET MAUI Android app: Views (XAML), ViewModels, Localization
tests/DailyAccount.Core.Tests xUnit: calculators + end-to-end FinanceService on real SQLite
docs/adr/                     Architecture Decision Records (one per decision)
```

## Build & install
```powershell
dotnet test tests/DailyAccount.Core.Tests
dotnet publish src/DailyAccount.App -f net10.0-android -c Release -o artifacts/apk
adb install -r artifacts/apk/com.himel.dailyaccount-Signed.apk   # phone with USB debugging on
```
Or copy the `-Signed.apk` to the phone and open it (allow "install unknown apps").

## First use
1. **Accounts → + Add account**: your bank(s), cash and bKash, each with today's balance.
2. **Settings (⚙ on Home)**: expected monthly income; language.
3. **Liabilities**: add loans (total + number of installments), credit cards (statement and due
   day), monthly bills, and money borrowed/lent.
4. Daily: **+** on Home → Income / Expense / Card purchase / Transfer / Borrow / Lend.
5. **Dues → Pay** when you pay an installment or bill.
6. **Settings → Export backup** regularly and save the file to Google Drive.

See `docs/adr/` for why things are built the way they are.
