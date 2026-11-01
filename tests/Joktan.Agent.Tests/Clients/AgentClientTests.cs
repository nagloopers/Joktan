using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Agent.Clients;
using Xunit;

namespace Joktan.Agent.Tests.Clients;

public class AgentClientTests
{
    public static TheoryData<AgentClientKind> Kinds() => [.. AgentClient.All.Select(c => c.Kind)];

    // Copilot's parser stops at a byte order mark, with "expected value at line 1 column 1".
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Configuration_files_are_json_from_the_first_byte(AgentClientKind kind)
    {
        var folder = Directory.CreateTempSubdirectory("joktan-agent-client-").FullName;
        try
        {
            var turn = new AgentTurn("Hello", null, "http://127.0.0.1:1/mcp", "token", folder, new AgentClientProfile(), false);

            AgentClient.Of(kind).Prepare(turn);

            var file = Assert.Single(Directory.GetFiles(folder));
            var bytes = File.ReadAllBytes(file);
            Assert.Equal((byte)'{', bytes[0]);
            using var document = JsonDocument.Parse(bytes);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
