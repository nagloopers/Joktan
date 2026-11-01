using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;

namespace Joktan.Settings.ViewModels;

/// <summary>Chooses which entries the menu shows and in what order, and saves each change as it is made.</summary>
public partial class MenuSettingsViewModel : PageViewModel
{
    public MenuSettingsViewModel(ObservableCollection<NavItemViewModel> menu, MenuLayout layout) : base(Strings.MenuSettings_Menu, Strings.MenuSettings_SettingsMenu)
    {
        MenuItems = menu;
        layout.ApplyTo(menu);
        Watch(menu);
    }

    public ObservableCollection<NavItemViewModel> MenuItems { get; }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp(NavItemViewModel item) => Move(item, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown(NavItemViewModel item) => Move(item, 1);

    private bool CanMoveUp(NavItemViewModel? item) => item is not null && Siblings(item).IndexOf(item) > 0;

    private bool CanMoveDown(NavItemViewModel? item)
    {
        if (item is null)
            return false;

        var siblings = Siblings(item);
        return siblings.IndexOf(item) < siblings.Count - 1;
    }

    private void Move(NavItemViewModel item, int by)
    {
        var siblings = Siblings(item);
        var index = siblings.IndexOf(item);
        siblings.Move(index, index + by);

        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        Save();
    }

    private ObservableCollection<NavItemViewModel> Siblings(NavItemViewModel item) => item.Parent?.Children ?? MenuItems;

    private void Watch(IEnumerable<NavItemViewModel> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged += OnItemChanged;
            Watch(item.Children);
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavItemViewModel.IsShown))
            Save();
    }

    private void Save() => MenuLayout.From(MenuItems).Save();
}
