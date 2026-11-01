using System;
using Avalonia.Controls;
using Joktan.Agent.ViewModels;

namespace Joktan.Agent.Views;

public partial class AgentSettingsView : UserControl
{
    public AgentSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AgentSettingsViewModel page)
        {
            _ = page.ShownAsync();
            page.CopyText = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            };
        }
    }
}
