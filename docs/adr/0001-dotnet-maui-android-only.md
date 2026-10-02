# 0001. .NET MAUI (C#), Android only

- Status: Accepted
- Date: 2026-10-02

## Context
The app is a personal finance book for one person's Android phone. The developer works in C#/.NET
daily. Options considered: Kotlin + Jetpack Compose (native, smallest APK, new language),
.NET MAUI (C#, ~30–60 MB APK), Flutter (Dart, new language).

## Decision
Use **.NET MAUI on .NET 10**, targeting **only `net10.0-android`** (minSdk 24 / Android 7.0).
The iOS, Mac Catalyst and Windows targets and platform folders from the template were removed.
.NET 10 is used instead of the originally planned .NET 9 because .NET 10 is the SDK installed.

## Consequences
- Fastest development: one language the developer already knows, Visual Studio tooling.
- APK is larger and cold start slightly slower than native Kotlin; acceptable for a personal app.
- Builds are quicker and simpler with one target framework.
- Adding iOS/Windows later means re-adding target frameworks and `Platforms/` folders; no code in
  `DailyAccount.Core` is Android-specific, so that work is limited to the App project.
