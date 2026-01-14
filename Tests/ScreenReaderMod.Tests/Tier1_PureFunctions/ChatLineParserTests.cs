#nullable enable
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Tests.Tier1_PureFunctions;

public class ChatLineParserTests
{
    #region TryParseLeadingNameTagChat - Null and Empty Input

    [Fact]
    public void TryParseLeadingNameTagChat_NullInput_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat(null, out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_EmptyString_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat(string.Empty, out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_WhitespaceOnly_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("   ", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    #endregion

    #region TryParseLeadingNameTagChat - No Name Tag

    [Fact]
    public void TryParseLeadingNameTagChat_PlainText_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("Hello world", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_OtherBracketTag_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[c/FF0000:text]message", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_NameTagInMiddle_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("Hello [n:Player]world", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    #endregion

    #region TryParseLeadingNameTagChat - Valid Name Tags

    [Fact]
    public void TryParseLeadingNameTagChat_SimpleNameTag_ParsesCorrectly()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:Player]Hello world", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player");
        message.Should().Be("Hello world");
    }

    [Fact]
    public void TryParseLeadingNameTagChat_NameTagCaseInsensitive_ParsesCorrectly()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[N:Player]Hello world", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player");
        message.Should().Be("Hello world");
    }

    [Fact]
    public void TryParseLeadingNameTagChat_WithLeadingWhitespace_ParsesCorrectly()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("   [n:Player]Hello world", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player");
        message.Should().Be("Hello world");
    }

    [Fact]
    public void TryParseLeadingNameTagChat_NameWithSpaces_ParsesCorrectly()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:John Doe]Hello", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("John Doe");
        message.Should().Be("Hello");
    }

    [Fact]
    public void TryParseLeadingNameTagChat_MessageWithSpaces_TrimsLeading()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:Player]   Hello world", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player");
        message.Should().Be("Hello world");
    }

    #endregion

    #region TryParseLeadingNameTagChat - Escaped Brackets

    [Fact]
    public void TryParseLeadingNameTagChat_EscapedOpenBracketInName_Unescapes()
    {
        // Note: The parser finds the first ']' so escaped closing brackets
        // in the name tag cause parsing issues. This tests escaped open bracket only.
        var result = ChatLineParser.TryParseLeadingNameTagChat(@"[n:Player\[1]Hello world", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player[1");
        message.Should().Be("Hello world");
    }

    #endregion

    #region TryParseLeadingNameTagChat - Invalid Name Tags

    [Fact]
    public void TryParseLeadingNameTagChat_EmptyName_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:]Hello", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_WhitespaceName_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:   ]Hello", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_EmptyMessage_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:Player]", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_WhitespaceMessage_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:Player]   ", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_MissingClosingBracket_ReturnsFalse()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:PlayerHello", out var name, out var message);

        result.Should().BeFalse();
        name.Should().BeEmpty();
        message.Should().BeEmpty();
    }

    [Fact]
    public void TryParseLeadingNameTagChat_ClosingBracketTooEarly_ReturnsFalse()
    {
        // Closing bracket at position 3, but we need at least position > 3 for a name
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:]message", out var name, out var message);

        result.Should().BeFalse();
    }

    #endregion

    #region TryParseLeadingNameTagChat - Message with Formatting

    [Fact]
    public void TryParseLeadingNameTagChat_MessageWithColorTag_CleansMessage()
    {
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:Player][c/FF0000:Hello]", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player");
        message.Should().Be("Hello");
    }

    [Fact]
    public void TryParseLeadingNameTagChat_PlainNameAndFormattedMessage_Works()
    {
        // Color tags in the message part are properly cleaned
        var result = ChatLineParser.TryParseLeadingNameTagChat("[n:Player]Check this [c/00FF00:green] text", out var name, out var message);

        result.Should().BeTrue();
        name.Should().Be("Player");
        message.Should().Be("Check this green text");
    }

    #endregion

    #region FormatNameMessage Tests

    [Fact]
    public void FormatNameMessage_BothNameAndMessage_FormatsCorrectly()
    {
        var result = ChatLineParser.FormatNameMessage("Player", "Hello world");

        result.Should().Be("Player: Hello world");
    }

    [Fact]
    public void FormatNameMessage_EmptyName_ReturnsMessageOnly()
    {
        var result = ChatLineParser.FormatNameMessage("", "Hello world");

        result.Should().Be("Hello world");
    }

    [Fact]
    public void FormatNameMessage_NullName_ReturnsMessageOnly()
    {
        var result = ChatLineParser.FormatNameMessage(null!, "Hello world");

        result.Should().Be("Hello world");
    }

    [Fact]
    public void FormatNameMessage_WhitespaceName_ReturnsMessageOnly()
    {
        var result = ChatLineParser.FormatNameMessage("   ", "Hello world");

        result.Should().Be("Hello world");
    }

    [Fact]
    public void FormatNameMessage_EmptyMessage_ReturnsNameOnly()
    {
        var result = ChatLineParser.FormatNameMessage("Player", "");

        result.Should().Be("Player");
    }

    [Fact]
    public void FormatNameMessage_NullMessage_ReturnsNameOnly()
    {
        var result = ChatLineParser.FormatNameMessage("Player", null!);

        result.Should().Be("Player");
    }

    [Fact]
    public void FormatNameMessage_WhitespaceMessage_ReturnsNameOnly()
    {
        var result = ChatLineParser.FormatNameMessage("Player", "   ");

        result.Should().Be("Player");
    }

    [Fact]
    public void FormatNameMessage_BothEmpty_ReturnsEmpty()
    {
        var result = ChatLineParser.FormatNameMessage("", "");

        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatNameMessage_CleansName()
    {
        var result = ChatLineParser.FormatNameMessage("  Player  ", "Hello");

        result.Should().Be("Player: Hello");
    }

    [Fact]
    public void FormatNameMessage_CleansMessage()
    {
        var result = ChatLineParser.FormatNameMessage("Player", "  Hello  ");

        result.Should().Be("Player: Hello");
    }

    [Fact]
    public void FormatNameMessage_NameWithColorTag_CleansFormatting()
    {
        var result = ChatLineParser.FormatNameMessage("[c/FF0000:Player]", "Hello");

        result.Should().Be("Player: Hello");
    }

    [Fact]
    public void FormatNameMessage_MessageWithColorTag_CleansFormatting()
    {
        var result = ChatLineParser.FormatNameMessage("Player", "[c/00FF00:Hello]");

        result.Should().Be("Player: Hello");
    }

    #endregion
}
