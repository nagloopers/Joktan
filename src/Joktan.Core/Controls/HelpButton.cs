using System;
using Avalonia.Controls;
using Joktan.Core.Resources;

namespace Joktan.Core.Controls;

/// <summary>Opens help on the topic its window names, or on the module whose page is being shown. Looks like any other button.</summary>
public sealed class HelpButton : Button
{
    public HelpButton()
    {
        Content = Strings.HelpButton_Help;
        ToolTip.SetTip(this, Strings.HelpButton_OpenHelpForWhatIsOn);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        base.OnClick();
        HelpContext.Open(TopLevel.GetTopLevel(this) as Window);
    }
}
