using DailyAccount.App.Views;

namespace DailyAccount.App;

public partial class AppShell : Shell
{
    // Pushed (non-tab) pages. Query parameters are listed next to each route.
    public const string AddTransaction = "addtx";   // ?type=income|expense|card|transfer|borrow|lend
    public const string Pay = "pay";                // ?dueId= | ?debtId= (lent money back) | ?cardId=&month= (card bill)
    public const string AddAccount = "addaccount";  // ?id= to edit
    public const string AccountDetail = "account";  // ?id=
    public const string AddLoan = "addloan";
    public const string AddCard = "addcard";        // ?id= to edit
    public const string Settings = "settings";
    public const string Reports = "report";
    public const string Categories = "categories";
    public const string CategoryReport = "categoryreport"; // ?id=&month=yyyy-MM

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(AddTransaction, typeof(AddTransactionPage));
        Routing.RegisterRoute(Pay, typeof(PayPage));
        Routing.RegisterRoute(AddAccount, typeof(AddAccountPage));
        Routing.RegisterRoute(AccountDetail, typeof(AccountDetailPage));
        Routing.RegisterRoute(AddLoan, typeof(AddLoanPage));
        Routing.RegisterRoute(AddCard, typeof(AddCardPage));
        Routing.RegisterRoute(Settings, typeof(SettingsPage));
        Routing.RegisterRoute(Reports, typeof(ReportsPage));
        Routing.RegisterRoute(Categories, typeof(CategoriesPage));
        Routing.RegisterRoute(CategoryReport, typeof(CategoryReportPage));
    }
}
