using CommunityToolkit.Mvvm.ComponentModel;

namespace DailyAccount.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>Called by the page every time it appears, so data is always fresh after navigation.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;
}

/// <summary>A choice in a Picker. Picker shows ToString().</summary>
public sealed record Option(int Id, string Display)
{
    public override string ToString() => Display;
}
