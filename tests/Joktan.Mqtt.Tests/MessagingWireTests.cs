using System;
using System.Text;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class MessagingWireTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void A_message_survives_the_trip_except_who_it_went_to()
    {
        var sent = new ChatMessage { Id = "1", Sender = "c", Name = "ada", Text = "hi", Sent = Now, To = "bob", ReplyName = "bob", ReplyText = "there" };

        var read = MessagingWire.ReadMessage(MessagingWire.Write(sent), Now.AddHours(1))!;

        Assert.Equal(("1", "c", "ada", "hi", Now), (read.Id, read.Sender, read.Name, read.Text, read.Sent));
        Assert.Equal(("bob", "there"), (read.ReplyName, read.ReplyText));
        Assert.Null(read.To);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"sender":"c","text":"t"}""")]
    [InlineData("""{"id":"1","text":"t"}""")]
    [InlineData("""{"id":"1","sender":"c","text":"  "}""")]
    [InlineData("""{"id":1,"sender":"c","text":"t"}""")]
    public void A_message_without_its_parts_is_dropped(string payload) =>
        Assert.Null(MessagingWire.ReadMessage(Bytes(payload), Now));

    [Theory]
    [InlineData("Ada Lovelace The First", "adalovelacethef")]
    [InlineData("ADMIN", "admin")]
    [InlineData("!!!", MessagingSettings.AnonymousName)]
    public void A_name_from_elsewhere_is_made_to_fit(string name, string expected)
    {
        var payload = $$"""{"id":"1","sender":"c","name":"{{name}}","text":"t"}""";

        Assert.Equal(expected, MessagingWire.ReadMessage(Bytes(payload), Now)!.Name);
    }

    [Theory]
    [InlineData("ada", true)]
    [InlineData("", true)]
    [InlineData("a_b-9", true)]
    [InlineData("123456789012345", true)]
    [InlineData("1234567890123456", false)]
    [InlineData("Ada", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("é", false)]
    public void The_name_rule(string name, bool allowed) =>
        Assert.Equal(allowed, MessagingSettings.CheckName(name) is null);

    [Theory]
    [InlineData("warning", AlertSeverity.Warning)]
    [InlineData("CRITICAL", AlertSeverity.Critical)]
    [InlineData("info", AlertSeverity.Info)]
    [InlineData("something else", AlertSeverity.Info)]
    public void An_alert_has_its_level(string level, AlertSeverity expected)
    {
        var payload = $$"""{"title":"Disk","text":"full","severity":"{{level}}"}""";

        var alert = MessagingWire.ReadAlert("joktan/alerts/plant", "joktan", Bytes(payload), Now)!;

        Assert.Equal(("Disk", "full", expected), (alert.Title, alert.Text, alert.Severity));
    }

    [Fact]
    public void Plain_text_is_an_alert_named_after_its_topic_and_an_empty_one_is_dropped()
    {
        var alert = MessagingWire.ReadAlert("joktan/alerts/plant/line1", "joktan", Bytes("hot"), Now)!;

        Assert.Equal(("plant/line1", "hot", AlertSeverity.Info), (alert.Title, alert.Text, alert.Severity));
        Assert.Equal("Alert", MessagingWire.ReadAlert("joktan/alerts", "joktan", Bytes("hot"), Now)!.Title);
        Assert.Null(MessagingWire.ReadAlert("joktan/alerts/x", "joktan", [], Now));
    }

    [Fact]
    public void Only_a_small_png_is_taken_for_a_picture()
    {
        byte[] Png(uint width, uint height)
        {
            var png = new byte[32];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), width);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), height);
            return png;
        }

        Assert.NotNull(MessagingWire.ReadPersonPicture(MessagingWire.WritePerson("a", Png(128, 128))));
        Assert.Null(MessagingWire.ReadPersonPicture(MessagingWire.WritePerson("a", Png(60000, 60000))));
        Assert.Null(MessagingWire.ReadPersonPicture(MessagingWire.WritePerson("a", Bytes("not a picture at all, not at all"))));
        Assert.Null(MessagingWire.ReadPersonPicture(MessagingWire.WritePerson("a", null)));
        Assert.Null(MessagingWire.ReadPersonPicture(Bytes("""{"avatar":"%%%"}""")));
    }

    [Fact]
    public void Someone_anonymous_has_no_name_to_write_to()
    {
        Assert.Equal("ada", MessagingWire.ReadPersonName(MessagingWire.WritePerson("ada", null)));
        Assert.Null(MessagingWire.ReadPersonName(MessagingWire.WritePerson(MessagingSettings.AnonymousName, null)));
        Assert.Null(MessagingWire.ReadPersonName([]));
    }
}
