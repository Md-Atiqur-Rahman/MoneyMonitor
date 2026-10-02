using System.Globalization;

namespace DailyAccount.App.Localization;

/// <summary>Current UI language and string lookup. Switching language rebuilds the Shell (see Settings).</summary>
public static class Loc
{
    public const string English = "en";
    public const string Bangla = "bn";

    private static readonly CultureInfo EnCulture = new("en-GB");
    private static readonly CultureInfo BnCulture = new("bn-BD");

    public static string Language { get; private set; } = English;
    public static bool IsBangla => Language == Bangla;

    /// <summary>Used only for month and day names. Numbers are always formatted by <see cref="Fmt"/>.</summary>
    public static CultureInfo Culture => IsBangla ? BnCulture : EnCulture;

    public static void SetLanguage(string language) => Language = language == Bangla ? Bangla : English;

    public static string T(string key) =>
        Strings.All.TryGetValue(key, out var s) ? (IsBangla ? s.Bn : s.En) : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);
}

/// <summary>XAML: Text="{loc:Tr Tab_Home}".</summary>
[ContentProperty(nameof(Key))]
public sealed class TrExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = "";

    public string ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
