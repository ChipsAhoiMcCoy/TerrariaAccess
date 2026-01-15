#nullable enable
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Tests.Tier1_PureFunctions;

public class GlyphTagFormatterTests
{
    #region Normalize - Null and Empty Input

    [Fact]
    public void Normalize_NullInput_ReturnsNull()
    {
        var result = GlyphTagFormatter.Normalize(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        var result = GlyphTagFormatter.Normalize(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsWhitespace()
    {
        var result = GlyphTagFormatter.Normalize("   ");

        result.Should().Be("   ");
    }

    [Fact]
    public void Normalize_PlainText_ReturnsUnchanged()
    {
        var result = GlyphTagFormatter.Normalize("Hello world");

        result.Should().Be("Hello world");
    }

    #endregion

    #region Normalize - Bracketed Glyph Tokens [g:X]

    [Fact]
    public void Normalize_BracketedNumber1_ReturnsAButton()
    {
        var result = GlyphTagFormatter.Normalize("[g:1]");

        result.Should().Be("A button");
    }

    [Fact]
    public void Normalize_BracketedNumber2_ReturnsBButton()
    {
        var result = GlyphTagFormatter.Normalize("[g:2]");

        result.Should().Be("B button");
    }

    [Fact]
    public void Normalize_BracketedNumber3_ReturnsXButton()
    {
        var result = GlyphTagFormatter.Normalize("[g:3]");

        result.Should().Be("X button");
    }

    [Fact]
    public void Normalize_BracketedNumber4_ReturnsYButton()
    {
        var result = GlyphTagFormatter.Normalize("[g:4]");

        result.Should().Be("Y button");
    }

    [Theory]
    [InlineData("[g:5]", "Right bumper")]
    [InlineData("[g:6]", "Left bumper")]
    [InlineData("[g:7]", "Left trigger")]
    [InlineData("[g:8]", "Right trigger")]
    public void Normalize_BracketedBumpersTriggers_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:9]", "View button")]
    [InlineData("[g:10]", "Menu button")]
    [InlineData("[g:11]", "Left stick")]
    [InlineData("[g:12]", "Right stick")]
    public void Normalize_BracketedSystemButtons_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:13]", "D-pad up")]
    [InlineData("[g:14]", "D-pad down")]
    [InlineData("[g:15]", "D-pad left")]
    [InlineData("[g:16]", "D-pad right")]
    public void Normalize_BracketedDPad_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:17]", "Left stick click")]
    [InlineData("[g:18]", "Right stick click")]
    public void Normalize_BracketedStickClicks_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_BracketedUpperCaseG_WorksSameAsLowerCase()
    {
        var result = GlyphTagFormatter.Normalize("[G:1]");

        result.Should().Be("A button");
    }

    #endregion

    #region Normalize - Text Token Aliases

    [Theory]
    [InlineData("[g:lb]", "Left bumper")]
    [InlineData("[g:rb]", "Right bumper")]
    [InlineData("[g:lt]", "Left trigger")]
    [InlineData("[g:rt]", "Right trigger")]
    public void Normalize_BracketedBumperTriggerAliases_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:ls]", "Left stick")]
    [InlineData("[g:rs]", "Right stick")]
    public void Normalize_BracketedStickAliases_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:back]", "View button")]
    [InlineData("[g:select]", "View button")]
    [InlineData("[g:menu]", "Menu button")]
    [InlineData("[g:start]", "Menu button")]
    public void Normalize_BracketedMenuButtonAliases_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:up]", "D-pad up")]
    [InlineData("[g:down]", "D-pad down")]
    [InlineData("[g:left]", "D-pad left")]
    [InlineData("[g:right]", "D-pad right")]
    public void Normalize_BracketedDPadAliases_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    #endregion

    #region Normalize - Mouse Buttons

    [Theory]
    [InlineData("[g:mouseleft]", "Left mouse button")]
    [InlineData("[g:mouseright]", "Right mouse button")]
    [InlineData("[g:mousemiddle]", "Middle mouse button")]
    public void Normalize_BracketedMouseButtons_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:mousewheelup]", "Mouse wheel up")]
    [InlineData("[g:mousewheeldown]", "Mouse wheel down")]
    public void Normalize_BracketedMouseWheel_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[g:mousexbutton1]", "Mouse button four")]
    [InlineData("[g:mousexbutton2]", "Mouse button five")]
    public void Normalize_BracketedMouseExtraButtons_ReturnsExpected(string input, string expected)
    {
        var result = GlyphTagFormatter.Normalize(input);

        result.Should().Be(expected);
    }

    #endregion

    #region Normalize - Embedded Tokens in Text

    [Fact]
    public void Normalize_TokenInMiddleOfText_ReplacesToken()
    {
        var result = GlyphTagFormatter.Normalize("Press [g:1] to continue");

        result.Should().Be("Press A button to continue");
    }

    [Fact]
    public void Normalize_MultipleTokensInText_ReplacesAll()
    {
        var result = GlyphTagFormatter.Normalize("Press [g:1] or [g:2] to select");

        result.Should().Be("Press A button or B button to select");
    }

    [Fact]
    public void Normalize_AdjacentTokens_ReplacesAll()
    {
        var result = GlyphTagFormatter.Normalize("[g:1][g:2]");

        result.Should().Be("A buttonB button");
    }

    #endregion

    #region Normalize - Bare g Tokens

    [Fact]
    public void Normalize_BareGlb_ReplacesToken()
    {
        var result = GlyphTagFormatter.Normalize("Press glb to grapple");

        result.Should().Be("Press Left bumper to grapple");
    }

    [Fact]
    public void Normalize_BareGTokenAtStart_Replaces()
    {
        var result = GlyphTagFormatter.Normalize("grb toggles");

        result.Should().Be("Right bumper toggles");
    }

    [Fact]
    public void Normalize_BareGTokenCaseInsensitive_Replaces()
    {
        var result = GlyphTagFormatter.Normalize("Press GLB now");

        result.Should().Be("Press Left bumper now");
    }

    [Fact]
    public void Normalize_GFollowedByLetterOrDigit_DoesNotReplace()
    {
        // "grapple" starts with g but is a normal word
        var result = GlyphTagFormatter.Normalize("grab the grapple");

        result.Should().Be("grab the grapple");
    }

    #endregion

    #region Normalize - Contextual Number Replacement

    [Fact]
    public void Normalize_NumberAfterPress_ReplacesWithButton()
    {
        var result = GlyphTagFormatter.Normalize("Press 1 to jump");

        result.Should().Be("Press A button to jump");
    }

    [Fact]
    public void Normalize_NumberAfterColon_ReplacesWithButton()
    {
        var result = GlyphTagFormatter.Normalize("Jump: 1");

        result.Should().Be("Jump: A button");
    }

    [Fact]
    public void Normalize_NumberWithToAfter_ReplacesWithButton()
    {
        var result = GlyphTagFormatter.Normalize("1 to jump");

        result.Should().Be("A button to jump");
    }

    [Fact]
    public void Normalize_NumberWithButtonAfter_ReplacesWithButton()
    {
        var result = GlyphTagFormatter.Normalize("Press 1 button");

        result.Should().Be("Press A button button");
    }

    [Fact]
    public void Normalize_NumberInNonButtonContext_DoesNotReplace()
    {
        var result = GlyphTagFormatter.Normalize("You have 5 items");

        result.Should().Be("You have 5 items");
    }

    [Fact]
    public void Normalize_NumberInMathContext_DoesNotReplace()
    {
        var result = GlyphTagFormatter.Normalize("2 + 3 = 5");

        result.Should().Be("2 + 3 = 5");
    }

    #endregion

    #region Normalize - Humanization of Unknown Tokens

    [Fact]
    public void Normalize_PascalCaseToken_HumanizesToTitleCase()
    {
        // Token like [g:LeftTrigger] should humanize
        var result = GlyphTagFormatter.Normalize("[g:SomeNewButton]");

        result.Should().Be("Some New Button");
    }

    [Fact]
    public void Normalize_SnakeCaseToken_HumanizesToTitleCase()
    {
        var result = GlyphTagFormatter.Normalize("[g:some_new_button]");

        result.Should().Be("Some New Button");
    }

    [Fact]
    public void Normalize_MixedCaseWithNumbers_SplitsCorrectly()
    {
        var result = GlyphTagFormatter.Normalize("[g:Button2Press]");

        result.Should().Be("Button 2 Press");
    }

    #endregion

    #region NormalizeNameCandidate Tests

    [Fact]
    public void NormalizeNameCandidate_NullInput_ReturnsEmpty()
    {
        var result = GlyphTagFormatter.NormalizeNameCandidate(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeNameCandidate_EmptyString_ReturnsEmpty()
    {
        var result = GlyphTagFormatter.NormalizeNameCandidate(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeNameCandidate_WhitespaceOnly_ReturnsEmpty()
    {
        var result = GlyphTagFormatter.NormalizeNameCandidate("   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeNameCandidate_PlainText_ReturnsSanitized()
    {
        var result = GlyphTagFormatter.NormalizeNameCandidate("Iron Pickaxe");

        result.Should().Be("Iron Pickaxe");
    }

    [Fact]
    public void NormalizeNameCandidate_WithGlyphToken_ReplacesToken()
    {
        var result = GlyphTagFormatter.NormalizeNameCandidate("Press [g:1]");

        result.Should().Be("Press A button");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Normalize_UnmatchedOpenBracket_LeavesUnchanged()
    {
        var result = GlyphTagFormatter.Normalize("[g:1 incomplete");

        // The bracket is left unchanged since there's no closing bracket
        result.Should().Be("[g:1 incomplete");
    }

    [Fact]
    public void Normalize_EmptyBracketedToken_LeavesUnchanged()
    {
        var result = GlyphTagFormatter.Normalize("[g:]");

        result.Should().Be("[g:]");
    }

    [Fact]
    public void Normalize_NonGlyphBracket_LeavesUnchanged()
    {
        var result = GlyphTagFormatter.Normalize("[other:tag]");

        result.Should().Be("[other:tag]");
    }

    [Fact]
    public void Normalize_NestedBrackets_HandlesCorrectly()
    {
        var result = GlyphTagFormatter.Normalize("[[g:1]]");

        // Should replace the inner token
        result.Should().Be("[A button]");
    }

    [Fact]
    public void Normalize_UnknownNumericToken_HumanizesToNumber()
    {
        var result = GlyphTagFormatter.Normalize("[g:999]");

        // 999 is not a mapped button but looks like a glyph token (numeric),
        // so it gets humanized to just "999"
        result.Should().Be("999");
    }

    [Fact]
    public void Normalize_TokenCaseInsensitivity_Works()
    {
        GlyphTagFormatter.Normalize("[g:LB]").Should().Be("Left bumper");
        GlyphTagFormatter.Normalize("[g:Lb]").Should().Be("Left bumper");
        GlyphTagFormatter.Normalize("[G:lb]").Should().Be("Left bumper");
    }

    #endregion
}
