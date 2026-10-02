# Daily Account: working rules

- **Every implementation or design decision gets an ADR** in `docs/adr/` (next number, Context /
  Decision / Consequences) and a row in `docs/adr/README.md`, in the same change. If a decision
  changes, write a new ADR that supersedes the old one.
- Business logic goes in `src/DailyAccount.Core` (testable, no MAUI). The App project only
  formats and displays. Add/extend tests in `tests/DailyAccount.Core.Tests` for any logic change.
- Money is `long` poisha; parse with `Fmt.ParseMoney`, display with `Fmt.Money` (ADR 0002).
- Every user-visible string goes in `src/DailyAccount.App/Localization/Strings.cs` in **both**
  English and Bangla (ADR 0006).
- Build/test:
  - `dotnet test tests/DailyAccount.Core.Tests`
  - `dotnet build src/DailyAccount.App -f net10.0-android`
  - APK: `dotnet publish src/DailyAccount.App -f net10.0-android -c Release -o artifacts/apk`
