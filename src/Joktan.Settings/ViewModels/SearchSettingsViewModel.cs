using System;
using System.Runtime.CompilerServices;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;

namespace Joktan.Settings.ViewModels;

/// <summary>Settings / Search: chooses what the search box looks through, and saves each change as it is made.</summary>
public class SearchSettingsViewModel(SearchSettings settings) : PageViewModel(Strings.SearchSettings_Search, Strings.SearchSettings_SettingsSearch)
{
    /// <summary>Raised after a switch changes.</summary>
    public event Action? Changed;

    public bool Notes
    {
        get => settings.Notes;
        set
        {
            settings.Notes = value;
            Save();
        }
    }

    public bool Help
    {
        get => settings.Help;
        set
        {
            settings.Help = value;
            Save();
        }
    }

    public bool Messages
    {
        get => settings.Messages;
        set
        {
            settings.Messages = value;
            Save();
        }
    }

    public bool Alerts
    {
        get => settings.Alerts;
        set
        {
            settings.Alerts = value;
            Save();
        }
    }

    private void Save([CallerMemberName] string? property = null)
    {
        settings.Save();
        OnPropertyChanged(property);
        Changed?.Invoke();
    }
}
