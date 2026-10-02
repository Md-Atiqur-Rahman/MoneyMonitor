# 0003. Offline SQLite storage, no internet permission

- Status: Accepted
- Date: 2026-10-02

## Context
Personal finance data is sensitive. The app is used by one person on one phone. A server or
cloud sync adds cost, accounts, and an attack surface.

## Decision
- Store everything in a local **SQLite** file, `FileSystem.AppDataDirectory/dailyaccount.db3`,
  through **sqlite-net-pcl** (bundled native SQLite via SourceGear.sqlite3).
- Entities in `DailyAccount.Core.Models` carry sqlite-net attributes directly (no separate DTOs).
  Schema creation/migration is `CreateTableAsync<T>()` in `FinanceDatabase.InitAsync`, which also
  adds new columns automatically when an entity gains a property.
- The Android manifest declares **no INTERNET permission**.
- `android:allowBackup="true"` stays on, so Android's own Google backup may also include the DB.
- Small preferences (language, expected income, last-backup time) use MAUI `Preferences`.

## Consequences
- Works with no network, instantly, and nothing leaves the phone unless the user exports a backup.
- Losing the phone loses the data unless a backup was exported ([0007](0007-json-backup-file.md)).
- Debug builds still get INTERNET automatically (the .NET debugger needs it); release builds don't.
- Column renames or type changes are NOT handled automatically; such a change needs a manual
  migration step and a new ADR.
