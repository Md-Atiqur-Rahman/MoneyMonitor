# 0036. Splash picture, app icon and name: the user's money photo (kept out of git), "মাসিক হিসাব"

- Status: Accepted
- Date: 2026-10-03

> **Amended 2026-10-04 (user request):** the title inside the app (top of Home, title of every dialog) is
> **মাসিক হিসাব** too, in English and Bangla mode (`AppName`).

## Context
The user wants a photo of ৳1000 notes (`docs/money.png`, 633×462) shown when the app opens. It is a
**watermarked stock image** (pngtree), fine on the user's own phone but not to be published in the repo.
Android 12+ shows the splash picture only inside a circle (2/3 of a square icon), so a wide photo would be cut.

## Decision
- **App name** under the launcher icon and on the splash: **মাসিক হিসাব** (`ApplicationTitle`, manifest label).
  Inside the app the title stays "Daily Account" / its Bangla text.
- **Splash**: the photo with "মাসিক হিসাব" under it. The name is drawn by Windows (GDI+, Nirmala UI) into a
  PNG, because Bangla vowel signs need proper text shaping that the SVG renderer doesn't guarantee.
- **App icon**: the photo on a #F5F5F5 adaptive icon (`Resources/AppIconMoney/appicon.svg` background,
  `appiconfg.svg` foreground, photo 300 wide in the 456 canvas so the notes sit in the visible middle).
  The folder is git-ignored; without it the default green icon is used. The background file must be named
  `appicon.svg` because the manifest points at `@mipmap/appicon`.
- A local script (in the scratchpad, not in git) wraps the photo in a square SVG,
  `Resources/Splash/moneysplash.svg` (432×432, photo scaled to 276×201 in the middle), which MAUI turns
  into the splash image.
- The splash background is **#F5F5F5**, the photo's own background, so the photo has no visible box.
- `DailyAccount.App.csproj` uses `moneysplash.svg` **when it exists**, else the default green splash
  (`splash.svg`); a clean clone builds as before.
- `docs/money.png` and `moneysplash.svg` are git-ignored.
- Switching the splash file needs a clean `obj/` (`rm -rf obj/Release bin/Release`), otherwise aapt can't
  find the new drawable.

## Consequences
- The user's phone shows the money picture at start; GitHub never gets the stock image.
- The picture is small on Android 12+ (system limit for splash icons); a bigger one would need an in-app
  start page.
