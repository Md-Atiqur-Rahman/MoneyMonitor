using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

public sealed record CategoryRow(string Name, bool IsChild, ICommand Tap)
{
    public Thickness Indent => IsChild ? new Thickness(24, 0, 0, 0) : new Thickness(0);
    public FontAttributes Weight => IsChild ? FontAttributes.None : FontAttributes.Bold;
}

/// <summary>Add people (Parents, Children…) and sub-categories (Bajar → Fish); rename any category (ADR 0013).</summary>
public sealed partial class CategoriesViewModel(FinanceService finance) : ViewModelBase
{
    [ObservableProperty] private List<CategoryRow> _expense = [];
    [ObservableProperty] private List<CategoryRow> _income = [];

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        Expense = Rows(s, CategoryKind.Expense);
        Income = Rows(s, CategoryKind.Income);
    }

    private List<CategoryRow> Rows(FinanceSnapshot s, CategoryKind kind)
    {
        var rows = new List<CategoryRow>();
        foreach (var top in s.TopCategories(kind))
        {
            rows.Add(new CategoryRow(Display.CategoryName(top.Id, s), false, new AsyncRelayCommand(() => TapAsync(top, true))));
            rows.AddRange(s.Children(top.Id).Select(child =>
                new CategoryRow(Display.CategoryName(child.Id, s), true, new AsyncRelayCommand(() => TapAsync(child, false)))));
        }
        return rows;
    }

    private async Task TapAsync(Category category, bool canHaveChildren)
    {
        var addSub = Loc.F("NewSub_Prompt", category.Name);
        var delete = Loc.T("Delete");
        string[] options = canHaveChildren && category.Kind == CategoryKind.Expense
            ? [Loc.T("Rename"), addSub, delete]
            : [Loc.T("Rename"), delete];

        var choice = await Ui.Choose(category.Name, options);
        if (choice is null) return;

        if (choice == delete)
        {
            await DeleteAsync(category);
            return;
        }

        if (choice == addSub)
        {
            var name = await Ui.PromptText(addSub);
            if (name is not null && await Ui.Try(() => finance.AddCategoryAsync(name, category.Kind, category.Id)))
                await LoadAsync();
        }
        else
        {
            var name = await Ui.PromptText(Loc.T("Rename_Prompt"), category.Name);
            if (name is not null && await Ui.Try(() => finance.RenameCategoryAsync(category.Id, name)))
                await LoadAsync();
        }
    }

    /// <summary>A sub-category's entries move to its parent; a main category must be unused (ADR 0016).</summary>
    private async Task DeleteAsync(Category category)
    {
        var s = await finance.LoadAsync();
        var question = category.ParentId is { } parentId
            ? Loc.F("Confirm_DeleteSub", category.Name, Display.CategoryName(parentId, s))
            : Loc.F("Confirm_DeleteCategory", category.Name);

        if (await Ui.Confirm(question) && await Ui.Try(() => finance.DeleteCategoryAsync(category.Id)))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task Add()
    {
        var expense = Loc.T("ExpenseCategories");
        var kind = await Ui.Choose(Loc.T("AddCategory"), expense, Loc.T("IncomeCategories"));
        if (kind is null) return;

        var name = await Ui.PromptText(Loc.T("NewCategory_Prompt"));
        if (name is not null && await Ui.Try(() => finance.AddCategoryAsync(name, kind == expense ? CategoryKind.Expense : CategoryKind.Income, null)))
            await LoadAsync();
    }
}
