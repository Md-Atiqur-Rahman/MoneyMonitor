# 0006. Bangla/English via a C# dictionary, not .resx

- Status: Accepted (supersedes the ".resx" idea in the original plan)
- Date: 2026-10-02

## Context
The UI must switch between English and Bangla. The usual .NET approach is `.resx` files plus
satellite assemblies, which means two XML files kept in sync by hand, culture-based lookup, and
trimming/packaging concerns on Android.

## Decision
- All strings live in **`Localization/Strings.cs`** as `key → (English, Bangla)`, side by side.
- `Loc.T(key)` / `Loc.F(key, args)` look them up; XAML uses the `{loc:Tr Key}` markup extension.
- **Switching language rebuilds the Shell** (`App.ReloadShell`), so every page and view model is
  created again with the new language. No live-binding plumbing is needed.
- `Fmt` formats numbers: in Bangla mode digits are shown as ০–৯; month names use the `bn-BD`
  culture. Input parsing accepts both Latin and Bangla digits, commas and "৳".
- The system font is used (no bundled Latin-only fonts) so Bangla glyphs always render.
- Category names are stored in both languages (`Name`, `NameBn`).

## Consequences
- A missing translation is visible in one file; a missing key shows the key itself, not a crash.
- Switching language resets navigation to the Home tab (acceptable; it's a rare action).
- User-entered names (accounts, lenders, people) are shown as typed, in whatever language.
