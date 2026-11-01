using System.Collections.Generic;
using Joktan.Settings.Resources;

namespace Joktan.Settings.Themes;

/// <summary>One thing a theme sets: the resource it goes in, and how the settings pages name it.</summary>
public sealed record ThemeToken(string Key, string Label, string Group);

/// <summary>Every colour, font and text size a theme holds. Each key is a resource in Tokens.axaml.</summary>
public static class ThemeTokens
{
    public const string AccentBrush = "AccentBrush";
    public const string WarningBrush = "WarningBrush";
    public const string DangerBrush = "DangerBrush";
    public const string TextBrush = "TextBrush";
    public const string TextMutedBrush = "TextMutedBrush";
    public const string OnAccentBrush = "OnAccentBrush";
    public const string SurfaceBrush = "SurfaceBrush";
    public const string BorderBrush = "BorderBrush";

    public const string InterfaceFont = "ContentControlThemeFontFamily";
    public const string CodeFont = "CodeFontFamily";

    public static IReadOnlyList<ThemeToken> Colors { get; } =
    [
        new("SidebarBackgroundBrush", Strings.ThemeTokens_Background, Strings.ThemeTokens_Menu),
        new("SidebarActiveBrush", Strings.ThemeTokens_SelectedEntry, Strings.ThemeTokens_Menu),
        new("SidebarHoverBrush", Strings.ThemeTokens_EntryUnderThePointer, Strings.ThemeTokens_Menu),
        new("SidebarDividerBrush", Strings.ThemeTokens_Divider, Strings.ThemeTokens_Menu),
        new("SidebarTextBrush", Strings.ThemeTokens_Text, Strings.ThemeTokens_Menu),
        new("SidebarTextActiveBrush", Strings.ThemeTokens_SelectedTextAndBrand, Strings.ThemeTokens_Menu),

        new("AppBackgroundBrush", Strings.ThemeTokens_AppBackground, Strings.ThemeTokens_SurfacesAndText),
        new(SurfaceBrush, Strings.ThemeTokens_CardsAndTopBar, Strings.ThemeTokens_SurfacesAndText),
        new("SurfaceAltBrush", Strings.ThemeTokens_PageHeader, Strings.ThemeTokens_SurfacesAndText),
        new(BorderBrush, Strings.ThemeTokens_Borders, Strings.ThemeTokens_SurfacesAndText),
        new("HoverBrush", Strings.ThemeTokens_UnderThePointer, Strings.ThemeTokens_SurfacesAndText),
        new(TextBrush, Strings.ThemeTokens_Text, Strings.ThemeTokens_SurfacesAndText),
        new(TextMutedBrush, Strings.ThemeTokens_MutedText, Strings.ThemeTokens_SurfacesAndText),

        new(AccentBrush, Strings.ThemeTokens_Accent, Strings.ThemeTokens_AccentAndStatus),
        new(OnAccentBrush, Strings.ThemeTokens_TextOnTheAccent, Strings.ThemeTokens_AccentAndStatus),
        new("SuccessBrush", Strings.ThemeTokens_Success, Strings.ThemeTokens_AccentAndStatus),
        new(WarningBrush, Strings.ThemeTokens_Warning, Strings.ThemeTokens_AccentAndStatus),
        new(DangerBrush, Strings.ThemeTokens_Danger, Strings.ThemeTokens_AccentAndStatus),
        new("InfoBrush", Strings.ThemeTokens_Information, Strings.ThemeTokens_AccentAndStatus),
        new("TealBrush", Strings.ThemeTokens_Teal, Strings.ThemeTokens_AccentAndStatus),
        new("PinkBrush", Strings.ThemeTokens_Pink, Strings.ThemeTokens_AccentAndStatus),
        new("VioletBrush", Strings.ThemeTokens_Violet, Strings.ThemeTokens_AccentAndStatus),

        new("SearchMatchBrush", Strings.ThemeTokens_Highlight, Strings.ThemeTokens_Search),
        new("OnSearchMatchBrush", Strings.ThemeTokens_TextOnTheHighlight, Strings.ThemeTokens_Search),

        new("AgentPromptBrush", Strings.ThemeTokens_PromptsButton, Strings.ThemeTokens_Agent),

        new("Chart1Brush", Strings.ThemeTokens_Series1Blue, Strings.ThemeTokens_Graphs),
        new("Chart2Brush", Strings.ThemeTokens_Series2Orange, Strings.ThemeTokens_Graphs),
        new("Chart3Brush", Strings.ThemeTokens_Series3Aqua, Strings.ThemeTokens_Graphs),
        new("Chart4Brush", Strings.ThemeTokens_Series4Yellow, Strings.ThemeTokens_Graphs),
        new("Chart5Brush", Strings.ThemeTokens_Series5Magenta, Strings.ThemeTokens_Graphs),
        new("Chart6Brush", Strings.ThemeTokens_Series6Green, Strings.ThemeTokens_Graphs),
        new("Chart7Brush", Strings.ThemeTokens_Series7Violet, Strings.ThemeTokens_Graphs),
        new("Chart8Brush", Strings.ThemeTokens_Series8Red, Strings.ThemeTokens_Graphs),
    ];

    public static IReadOnlyList<ThemeToken> Fonts { get; } =
    [
        new(InterfaceFont, Strings.ThemeTokens_Interface, Strings.ThemeTokens_Fonts),
        new("HeadingFontFamily", Strings.ThemeTokens_Headings, Strings.ThemeTokens_Fonts),
        new("BrandFontFamily", Strings.ThemeTokens_Brand, Strings.ThemeTokens_Fonts),
        new("MenuFontFamily", Strings.ThemeTokens_Menu, Strings.ThemeTokens_Fonts),
        new(CodeFont, Strings.ThemeTokens_Code, Strings.ThemeTokens_Fonts),
    ];

    public static IReadOnlyList<ThemeToken> TextSizes { get; } =
    [
        new("BrandFontSize", Strings.ThemeTokens_Brand, Strings.ThemeTokens_Menu),
        new("SplashBrandFontSize", Strings.ThemeTokens_BrandOnTheSplashScreen, Strings.ThemeTokens_Menu),
        new("SidebarCaptionFontSize", Strings.ThemeTokens_Caption, Strings.ThemeTokens_Menu),
        new("MenuFontSize", Strings.ThemeTokens_Entry, Strings.ThemeTokens_Menu),
        new("SubmenuFontSize", Strings.ThemeTokens_EntryWithinAGroup, Strings.ThemeTokens_Menu),

        new("PageTitleFontSize", Strings.ThemeTokens_PageTitle, Strings.ThemeTokens_Headings),
        new("DialogTitleFontSize", Strings.ThemeTokens_DialogTitle, Strings.ThemeTokens_Headings),
        new("SectionTitleFontSize", Strings.ThemeTokens_SectionTitle, Strings.ThemeTokens_Headings),
        new("CardTitleFontSize", Strings.ThemeTokens_CardTitle, Strings.ThemeTokens_Headings),
        new("TabFontSize", Strings.ThemeTokens_Tab, Strings.ThemeTokens_Headings),
        new("TileValueFontSize", Strings.ThemeTokens_ValueOnATile, Strings.ThemeTokens_Headings),
        new("NoteTitleFontSize", Strings.ThemeTokens_NoteTitle, Strings.ThemeTokens_Headings),

        new("ControlContentThemeFontSize", Strings.ThemeTokens_BodyAndControls, Strings.ThemeTokens_Text),
        new("SecondaryFontSize", Strings.ThemeTokens_Secondary, Strings.ThemeTokens_Text),
        new("SmallFontSize", Strings.ThemeTokens_Small, Strings.ThemeTokens_Text),
        new("TinyFontSize", Strings.ThemeTokens_Smallest, Strings.ThemeTokens_Text),

        new("CodeFontSize", Strings.ThemeTokens_Code, Strings.ThemeTokens_Code),
        new("InlineCodeFontSize", Strings.ThemeTokens_CodeWithinFormattedText, Strings.ThemeTokens_Code),
        new("MethodFontSize", Strings.ThemeTokens_RequestMethod, Strings.ThemeTokens_Code),
        new("NoteEditorFontSize", Strings.ThemeTokens_NoteEditor, Strings.ThemeTokens_Code),

        new("MarkdownBodyFontSize", Strings.ThemeTokens_Paragraph, Strings.ThemeTokens_FormattedText),
        new("MarkdownH1FontSize", Strings.ThemeTokens_Heading1, Strings.ThemeTokens_FormattedText),
        new("MarkdownH2FontSize", Strings.ThemeTokens_Heading2, Strings.ThemeTokens_FormattedText),
        new("MarkdownH3FontSize", Strings.ThemeTokens_Heading3, Strings.ThemeTokens_FormattedText),

        new("GraphAxisFontSize", Strings.ThemeTokens_Axis, Strings.ThemeTokens_Graphs),
        new("GraphLabelFontSize", Strings.ThemeTokens_Label, Strings.ThemeTokens_Graphs),
    ];
}
