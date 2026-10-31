using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Joktan.Core.ViewModels;

public partial class NavItemViewModel : ViewModelBase
{
    public NavItemViewModel(string title, Geometry? icon = null, params NavItemViewModel[] children)
    {
        Title = title;
        Icon = icon;
        Children = new ObservableCollection<NavItemViewModel>(children);

        foreach (var child in children)
            child.Parent = this;
    }

    public string Title { get; }

    /// <summary>The title this entry had before it was renamed, by which a saved menu layout may still know it.</summary>
    public string? FormerTitle { get; init; }

    public Geometry? Icon { get; }

    /// <summary>The group this entry is in; null at the top of the menu.</summary>
    public NavItemViewModel? Parent { get; private set; }

    /// <summary>The entries below this one in menu order, hidden ones included.</summary>
    public ObservableCollection<NavItemViewModel> Children { get; }

    public bool HasChildren => Children.Count > 0;

    /// <summary>The page this entry opens; null shows the placeholder page.</summary>
    public PageViewModel? Page { get; set; }

    /// <summary>The help topic for this entry's page.</summary>
    public string? HelpTopic { get; init; }

    /// <summary>False for an entry that must stay on the menu.</summary>
    public bool CanHide { get; init; } = true;

    /// <summary>Whether the user wants this entry on the menu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInMenu))]
    public partial bool IsShown { get; set; } = true;

    /// <summary>Whether the menu draws this entry: a group with nothing left to show goes too.</summary>
    public bool IsInMenu => IsShown && (!HasChildren || Children.Any(child => child.IsInMenu));

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsShownChanged(bool value)
    {
        for (var group = Parent; group is not null; group = group.Parent)
            group.OnPropertyChanged(nameof(IsInMenu));
    }
}
