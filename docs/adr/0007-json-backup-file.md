# 0007. Manual JSON backup through the share sheet

- Status: Accepted
- Date: 2026-10-02

## Context
Data lives only on the phone ([0003](0003-offline-sqlite-storage.md)). A lost or reset phone must
not mean lost history. Automatic cloud sync was rejected as too complex for v1.

## Decision
- **Export** (Settings → Export backup): `FinanceService.ExportAsync` → `BackupData` (all tables +
  `Version` + `CreatedAt`) → indented JSON file in the cache directory → Android **share sheet**, so
  the user saves it to Google Drive, Gmail, WhatsApp, etc.
- **Restore** (Settings → Restore from file): file picker → `BackupData.FromJson` (rejects invalid
  files and files from a newer `Version`) → confirm → `FinanceService.ImportAsync` replaces **all**
  data in one transaction.
- Restore uses `InsertOrReplace` so **original Ids are kept**; plain `Insert` would renumber
  AutoIncrement keys and break every link between transactions, dues and their sources.
- The Settings screen shows the last backup date, and warns in red when there has never been one.

## Consequences
- Human-readable, version-tagged, and independent of the SQLite file format.
- Backup is manual; the user must remember. A reminder notification is a candidate feature.
- Restore is all-or-nothing (no merge). That is intentional: merging two ledgers is ambiguous.
- Tested: export → wipe → import gives identical balances, dues and summaries.
