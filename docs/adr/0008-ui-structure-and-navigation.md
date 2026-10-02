# 0008. Shell tabs + MVVM (CommunityToolkit) UI structure

- Status: Accepted
- Date: 2026-10-02

> **Amended by [0011](0011-monthly-budget.md):** tabs are now Home, Budget, Dues, Accounts, Liabilities; Reports opens from Home.
> **Amended 2026-10-02:** the floating "+" button on Home covered Pay buttons; it is now a full-width "+ Add income / expense" button in its own row below the scrolling content. Long due titles wrap instead of truncating.

> **Amended 2026-10-03 (bug report):** a grey placeholder "6" in the installments box looked like a typed
> value, so the user left the box empty and got "Installments must be between 1 and 600". Example
> placeholders now always read **"e.g. …"** (localized) and use a lighter colour (`Placeholder` #A3ABA6),
> and the error says what is missing: "Enter the number of monthly installments (1–600)." Amount boxes
> keep "0" as their placeholder, since empty really means 0 there.

## Context
The approved screen designs (10 screens) have five main areas reachable at any time, plus forms
that open on top. The code should be simple to follow for a single developer.

## Decision
- **Shell `TabBar`** with five tabs: Home, Dues, Accounts, Liabilities, Reports.
  Forms are pushed routes registered in `AppShell.xaml.cs` (`addtx`, `pay`, `addaccount`,
  `account`, `addloan`, `addcard`, `addbill`, `settings`) with query parameters (`?dueId=`, `?type=`).
- **MVVM with CommunityToolkit.Mvvm** (`[ObservableProperty]`, `[RelayCommand]`). Pages and view
  models are registered **transient** in DI; data services are singletons.
- Every page derives from `AppPage`, which calls `ViewModelBase.LoadAsync()` **each time the page
  appears**, so numbers are always fresh after paying or adding something.
- Read-only screens expose one immutable record (`DashboardModel`, `DuesModel`, `ReportModel`) built
  in one go; forms expose individual properties.
- **Compiled bindings** everywhere (`x:DataType`), XAML source generation (`MauiXamlInflator=SourceGen`).
- Shared row templates (`DueRowTemplate`, `TxRowTemplate`, `BarRowTemplate`) live in
  `Resources/Styles/Templates.xaml`; lists use `BindableLayout` (short lists inside a ScrollView).
- One `Pay` screen serves both "pay a due" and "lent money returned".
- Borrow/Lend are entered from the same Add-transaction form as income and expenses.
- Light theme only, palette in `Resources/Styles/Colors.xaml`, matching the design canvas.

## Consequences
- Each screen is one XAML + one tiny code-behind + one view model, easy to locate.
- Reloading on every appearance is simple and correct; cheap given [0004](0004-core-owns-data-and-in-memory-snapshot.md).
- `BindableLayout` doesn't virtualise; if a history list grows to many hundreds of rows, switch that
  list to `CollectionView`.
- Dark mode is not supported yet.
