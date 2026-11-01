using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Tests;

/// <summary>The text of the app stays where a translation can reach it. Specified in docs/features/localization/spec.md.</summary>
public partial class LocalizationTests
{
    // The product's name, and examples that read the same in every language.
    private static readonly HashSet<string> NotTranslated =
    [
        "Joktan", "joktan", "true", "false", "ZA", "https://…", "°C", "ada", "plant/line1/telemetry",
        "sensor-01, sensor-02, sensor-03", "localhost, 127.0.0.1, ::1", "localhost, 127.0.0.1, ::1, broker.example.com",
        "{}{{token}}", "https://api.example.com/items or {{baseUrl}}/items",
    ];

    private static string[] Assemblies => Directory.GetDirectories(RepositoryFiles.At("src"));

    private static IEnumerable<string> SourceFiles(string assembly, string pattern) =>
        Directory.EnumerateFiles(assembly, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex("""(?<![\w.:])(?:Text|Content|Header|Title|PlaceholderText|ToolTip\.Tip|OnContent|OffContent)="(?<value>[^"]*)"[^>]*?""")]
    private static partial Regex TextAttribute();

    [Fact]
    public void Views_take_their_text_from_the_resources()
    {
        var written = from assembly in Assemblies
                      from file in SourceFiles(assembly, "*.axaml")
                      from Match match in TextAttribute().Matches(File.ReadAllText(file))
                      let value = match.Groups["value"].Value
                      where value.Any(char.IsLetter) && (!value.StartsWith('{') || value.StartsWith("{}"))
                      where !NotTranslated.Contains(value)
                      select $"{Path.GetFileName(file)}: {value}";

        Assert.Empty(written);
    }

    [Fact]
    public void Every_resource_is_used()
    {
        var unused = from assembly in Assemblies
                     let resources = Path.Combine(assembly, "Resources", "Strings.resx")
                     where File.Exists(resources)
                     let source = string.Concat(SourceFiles(assembly, "*.cs").Concat(SourceFiles(assembly, "*.axaml")).Select(File.ReadAllText))
                     from data in XDocument.Load(resources).Root!.Elements("data")
                     let key = data.Attribute("name")!.Value
                     where !Regex.IsMatch(source, $@"Strings\.{key}\b")
                     select $"{Path.GetFileName(assembly)}: {key}";

        Assert.Empty(unused);
    }
}
