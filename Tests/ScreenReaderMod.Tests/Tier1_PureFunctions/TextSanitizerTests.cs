#nullable enable
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Tests.Tier1_PureFunctions;

public class TextSanitizerTests
{
    #region Clean - Null and Empty Input

    [Fact]
    public void Clean_NullInput_ReturnsEmpty()
    {
        var result = TextSanitizer.Clean(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_EmptyString_ReturnsEmpty()
    {
        var result = TextSanitizer.Clean(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TextSanitizer.Clean("   ");

        result.Should().BeEmpty();
    }

    #endregion

    #region Clean - Plain Text

    [Fact]
    public void Clean_PlainText_ReturnsTrimmed()
    {
        var result = TextSanitizer.Clean("Hello world");

        result.Should().Be("Hello world");
    }

    [Fact]
    public void Clean_TextWithLeadingWhitespace_Trims()
    {
        var result = TextSanitizer.Clean("   Hello");

        result.Should().Be("Hello");
    }

    [Fact]
    public void Clean_TextWithTrailingWhitespace_Trims()
    {
        var result = TextSanitizer.Clean("Hello   ");

        result.Should().Be("Hello");
    }

    [Fact]
    public void Clean_TextWithBothWhitespace_TrimsBoth()
    {
        var result = TextSanitizer.Clean("   Hello   ");

        result.Should().Be("Hello");
    }

    #endregion

    #region Clean - Color Formatting [c/COLOR:TEXT]

    [Fact]
    public void Clean_ColorToken_ExtractsText()
    {
        var result = TextSanitizer.Clean("[c/FF0000:Red Text]");

        result.Should().Be("Red Text");
    }

    [Fact]
    public void Clean_ColorTokenCaseInsensitive_ExtractsText()
    {
        var result = TextSanitizer.Clean("[C/FFFFFF:White Text]");

        result.Should().Be("White Text");
    }

    [Fact]
    public void Clean_ColorTokenWithoutColon_ExtractsNothing()
    {
        var result = TextSanitizer.Clean("[c/FF0000]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_ColorTokenInMiddle_ExtractsText()
    {
        var result = TextSanitizer.Clean("Start [c/FF0000:middle] end");

        result.Should().Be("Start middle end");
    }

    [Fact]
    public void Clean_MultipleColorTokens_ExtractsAll()
    {
        var result = TextSanitizer.Clean("[c/FF0000:Red] and [c/00FF00:Green]");

        result.Should().Be("Red and Green");
    }

    #endregion

    #region Clean - Name Tag [n:NAME]

    [Fact]
    public void Clean_NameTag_ExtractsNameWithComma()
    {
        var result = TextSanitizer.Clean("[n:PlayerName]");

        result.Should().Be("PlayerName,");
    }

    [Fact]
    public void Clean_NameTagCaseInsensitive_ExtractsName()
    {
        var result = TextSanitizer.Clean("[N:PlayerName]");

        result.Should().Be("PlayerName,");
    }

    [Fact]
    public void Clean_NameTagWithEscapedOpenBracket_UnescapesOpenBracket()
    {
        // Note: The parser finds the first ']' so escaped closing brackets
        // within the name require special handling by the caller.
        // This test verifies escaped open brackets work correctly.
        var result = TextSanitizer.Clean(@"[n:Player\[1]");

        result.Should().Be("Player[1,");
    }

    [Fact]
    public void Clean_NameTagEmpty_ReturnsEmpty()
    {
        var result = TextSanitizer.Clean("[n:]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_NameTagWhitespaceOnly_ReturnsEmpty()
    {
        var result = TextSanitizer.Clean("[n:   ]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_NameTagFollowedByText_BothIncluded()
    {
        var result = TextSanitizer.Clean("[n:Player]Hello world");

        result.Should().Be("Player,Hello world");
    }

    #endregion

    #region Clean - Stripped Tokens

    [Fact]
    public void Clean_ItemToken_StripsCompletely()
    {
        var result = TextSanitizer.Clean("[i:ItemName]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_ItemTokenCaseInsensitive_StripsCompletely()
    {
        var result = TextSanitizer.Clean("[I:ItemName]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_RbToken_StripsCompletely()
    {
        var result = TextSanitizer.Clean("[rb123]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_GToken_StripsCompletely()
    {
        var result = TextSanitizer.Clean("[g123]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_WaveToken_StripsCompletely()
    {
        var result = TextSanitizer.Clean("[wave:text]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_MixedStrippedTokens_StripsAll()
    {
        var result = TextSanitizer.Clean("[i:1] [rb1] [g1]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Clean_StrippedTokensWithText_KeepsText()
    {
        var result = TextSanitizer.Clean("Get the [i:1234] item");

        result.Should().Be("Get the  item");
    }

    #endregion

    #region Clean - Unknown Tokens

    [Fact]
    public void Clean_UnknownToken_LeavesUnchanged()
    {
        var result = TextSanitizer.Clean("[unknown:data]");

        result.Should().Be("[unknown:data]");
    }

    [Fact]
    public void Clean_PlainBrackets_LeavesUnchanged()
    {
        var result = TextSanitizer.Clean("[not a token]");

        result.Should().Be("[not a token]");
    }

    #endregion

    #region Clean - Edge Cases

    [Fact]
    public void Clean_UnmatchedOpenBracket_LeavesUnchanged()
    {
        var result = TextSanitizer.Clean("[c/FF0000:incomplete");

        result.Should().Be("[c/FF0000:incomplete");
    }

    [Fact]
    public void Clean_EmptyBrackets_LeavesUnchanged()
    {
        var result = TextSanitizer.Clean("[]");

        result.Should().Be("[]");
    }

    [Fact]
    public void Clean_NestedBrackets_HandlesOuter()
    {
        var result = TextSanitizer.Clean("[c/FF0000:[inner]]");

        result.Should().Be("[inner]");
    }

    [Fact]
    public void Clean_ComplexFormatting_ExtractsAllText()
    {
        var result = TextSanitizer.Clean("[c/FF0000:Red] [c/00FF00:Green] [c/0000FF:Blue]");

        result.Should().Be("Red Green Blue");
    }

    #endregion

    #region JoinWithComma Tests

    [Fact]
    public void JoinWithComma_EmptyArray_ReturnsEmpty()
    {
        var result = TextSanitizer.JoinWithComma();

        result.Should().BeEmpty();
    }

    [Fact]
    public void JoinWithComma_SingleItem_ReturnsItem()
    {
        var result = TextSanitizer.JoinWithComma("Hello");

        result.Should().Be("Hello");
    }

    [Fact]
    public void JoinWithComma_TwoItems_JoinsWithCommaSpace()
    {
        var result = TextSanitizer.JoinWithComma("Hello", "World");

        result.Should().Be("Hello, World");
    }

    [Fact]
    public void JoinWithComma_ThreeItems_JoinsAllWithCommaSpace()
    {
        var result = TextSanitizer.JoinWithComma("A", "B", "C");

        result.Should().Be("A, B, C");
    }

    [Fact]
    public void JoinWithComma_NullItems_SkipsNulls()
    {
        var result = TextSanitizer.JoinWithComma("Hello", null, "World");

        result.Should().Be("Hello, World");
    }

    [Fact]
    public void JoinWithComma_EmptyItems_SkipsEmpty()
    {
        var result = TextSanitizer.JoinWithComma("Hello", "", "World");

        result.Should().Be("Hello, World");
    }

    [Fact]
    public void JoinWithComma_WhitespaceItems_SkipsWhitespace()
    {
        var result = TextSanitizer.JoinWithComma("Hello", "   ", "World");

        result.Should().Be("Hello, World");
    }

    [Fact]
    public void JoinWithComma_AllNullOrEmpty_ReturnsEmpty()
    {
        var result = TextSanitizer.JoinWithComma(null, "", "   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void JoinWithComma_CleansEachItem()
    {
        var result = TextSanitizer.JoinWithComma("  Hello  ", "[c/FF0000:World]");

        result.Should().Be("Hello, World");
    }

    [Fact]
    public void JoinWithComma_ItemsWithFormatting_CleansAndJoins()
    {
        var result = TextSanitizer.JoinWithComma("[c/FF0000:Red]", "[c/00FF00:Green]");

        result.Should().Be("Red, Green");
    }

    #endregion
}
