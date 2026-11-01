using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Help.Tests;

public class HelpTopicTests
{
    private static readonly string TopicsFolder = RepositoryFiles.At("src", "Joktan.Help", "Topics");

    [Fact]
    public Task A_topic_with_no_translation_is_shown_in_English() => Headless.Run(() =>
    {
        var language = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var topic = HelpTopic.All[0];
            Assert.Equal(File.ReadAllText(Path.Combine(TopicsFolder, topic.Id + ".md")), topic.ReadMarkdown());
        }
        finally
        {
            CultureInfo.CurrentUICulture = language;
        }
    });

    [Fact]
    public void Getting_started_comes_first()
    {
        Assert.Equal(HelpTopic.Home, HelpTopic.All[0].Id);
    }

    [Fact]
    public void Topics_have_different_IDs_and_titles()
    {
        Assert.Equal(HelpTopic.All.Count, HelpTopic.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(HelpTopic.All.Count, HelpTopic.All.Select(t => t.Title).Distinct().Count());
    }

    [Fact]
    public void Every_topic_has_its_page()
    {
        Assert.All(HelpTopic.All, topic => Assert.True(File.Exists(Path.Combine(TopicsFolder, topic.Id + ".md")), topic.Id));
    }

    [Fact]
    public void Every_page_is_listed_as_a_topic()
    {
        var pages = Directory.EnumerateFiles(TopicsFolder, "*.md").Select(Path.GetFileNameWithoutExtension);

        Assert.All(pages, page => Assert.Contains(page, HelpTopic.All.Select(t => t.Id)));
    }

    [Fact]
    public void A_topic_sits_below_a_topic_at_the_top_that_comes_before_it()
    {
        for (var i = 0; i < HelpTopic.All.Count; i++)
        {
            if (HelpTopic.All[i].Parent is not { } parent)
                continue;

            var above = HelpTopic.All.Take(i).Where(t => t.Parent is null).Select(t => t.Title);
            Assert.Contains(parent, above);
        }
    }
}
