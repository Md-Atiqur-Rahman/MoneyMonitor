# 0010. Distribute as a side-loaded APK

- Status: Accepted
- Date: 2026-10-02

## Context
The app is for the developer's own phone. Publishing on Google Play needs a developer account,
review, and an AAB. Android only lets an app update in place if the new APK is signed with the
**same key** as the installed one; otherwise it must be uninstalled first, which **deletes its data**.

## Decision
- Release builds produce a single **`.apk`** (`AndroidPackageFormat=apk` for Release), published to
  `artifacts/apk/` with `dotnet publish -f net10.0-android -c Release -o artifacts/apk`.
- For now the APK is signed with the .NET Android **debug keystore** of this PC
  (`%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore`).
- Install by USB (`adb install -r`) or by copying the APK to the phone.

## Consequences
- No store, no account, no review.
- **Back up the debug keystore file.** If this PC is reinstalled and the keystore is lost, the next
  APK can't update the installed app; the user would have to export a backup, uninstall, install,
  and restore. Moving to a dedicated release keystore (kept outside the repo) is a planned step and
  will get its own ADR.
- `artifacts/` is build output and is git-ignored.
