using System.Collections.Generic;

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
        new("SidebarBackgroundBrush", "Background", "Menu"),
        new("SidebarActiveBrush", "Selected entry", "Menu"),
        new("SidebarHoverBrush", "Entry under the pointer", "Menu"),
        new("SidebarDividerBrush", "Divider", "Menu"),
        new("SidebarTextBrush", "Text", "Menu"),
        new("SidebarTextActiveBrush", "Selected text and brand", "Menu"),

        new("AppBackgroundBrush", "App background", "Surfaces and text"),
        new(SurfaceBrush, "Cards and top bar", "Surfaces and text"),
        new("SurfaceAltBrush", "Page header", "Surfaces and text"),
        new(BorderBrush, "Borders", "Surfaces and text"),
        new("HoverBrush", "Under the pointer", "Surfaces and text"),
        new(TextBrush, "Text", "Surfaces and text"),
        new(TextMutedBrush, "Muted text", "Surfaces and text"),

        new(AccentBrush, "Accent", "Accent and status"),
        new(OnAccentBrush, "Text on the accent", "Accent and status"),
        new("SuccessBrush", "Success", "Accent and status"),
        new(WarningBrush, "Warning", "Accent and status"),
        new(DangerBrush, "Danger", "Accent and status"),
        new("InfoBrush", "Information", "Accent and status"),
        new("TealBrush", "Teal", "Accent and status"),
        new("PinkBrush", "Pink", "Accent and status"),
        new("VioletBrush", "Violet", "Accent and status"),

        new("SearchMatchBrush", "Highlight", "Search"),
        new("OnSearchMatchBrush", "Text on the highlight", "Search"),

        new("AgentPromptBrush", "Prompts button", "Agent"),

        new("Chart1Brush", "Series 1 (Blue)", "Graphs"),
        new("Chart2Brush", "Series 2 (Orange)", "Graphs"),
        new("Chart3Brush", "Series 3 (Aqua)", "Graphs"),
        new("Chart4Brush", "Series 4 (Yellow)", "Graphs"),
        new("Chart5Brush", "Series 5 (Magenta)", "Graphs"),
        new("Chart6Brush", "Series 6 (Green)", "Graphs"),
        new("Chart7Brush", "Series 7 (Violet)", "Graphs"),
        new("Chart8Brush", "Series 8 (Red)", "Graphs"),
    ];

    public static IReadOnlyList<ThemeToken> Fonts { get; } =
    [
        new(InterfaceFont, "Interface", "Fonts"),
        new("HeadingFontFamily", "Headings", "Fonts"),
        new("BrandFontFamily", "Brand", "Fonts"),
        new("MenuFontFamily", "Menu", "Fonts"),
        new(CodeFont, "Code", "Fonts"),
    ];

    public static IReadOnlyList<ThemeToken> TextSizes { get; } =
    [
        new("BrandFontSize", "Brand", "Menu"),
        new("SplashBrandFontSize", "Brand on the splash screen", "Menu"),
        new("SidebarCaptionFontSize", "Caption", "Menu"),
        new("MenuFontSize", "Entry", "Menu"),
        new("SubmenuFontSize", "Entry within a group", "Menu"),

        new("PageTitleFontSize", "Page title", "Headings"),
        new("DialogTitleFontSize", "Dialog title", "Headings"),
        new("SectionTitleFontSize", "Section title", "Headings"),
        new("CardTitleFontSize", "Card title", "Headings"),
        new("TabFontSize", "Tab", "Headings"),
        new("TileValueFontSize", "Value on a tile", "Headings"),
        new("NoteTitleFontSize", "Note title", "Headings"),

        new("ControlContentThemeFontSize", "Body and controls", "Text"),
        new("SecondaryFontSize", "Secondary", "Text"),
        new("SmallFontSize", "Small", "Text"),
        new("TinyFontSize", "Smallest", "Text"),

        new("CodeFontSize", "Code", "Code"),
        new("InlineCodeFontSize", "Code within formatted text", "Code"),
        new("MethodFontSize", "Request method", "Code"),
        new("NoteEditorFontSize", "Note editor", "Code"),

        new("MarkdownBodyFontSize", "Paragraph", "Formatted text"),
        new("MarkdownH1FontSize", "Heading 1", "Formatted text"),
        new("MarkdownH2FontSize", "Heading 2", "Formatted text"),
        new("MarkdownH3FontSize", "Heading 3", "Formatted text"),

        new("GraphAxisFontSize", "Axis", "Graphs"),
        new("GraphLabelFontSize", "Label", "Graphs"),
    ];
}
