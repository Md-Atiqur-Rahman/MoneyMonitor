namespace DailyAccount.Core.Data;

/// <summary>
/// A rule was broken (e.g. amount is zero). <see cref="Exception.Message"/> is a resource key
/// (e.g. "Err_Amount") that the UI translates into English or Bangla.
/// </summary>
public sealed class FinanceException(string key) : Exception(key)
{
    public string Key { get; } = key;
}
