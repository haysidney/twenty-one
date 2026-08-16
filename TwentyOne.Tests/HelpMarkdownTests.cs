using System.Collections.Generic;
using System.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class HelpMarkdownTests
{
    // Every word is 1 unit wide plus 1 per character, so line widths in these
    // tests are predictable without an ImGui font.
    private static float Measure(string s) => s.Length;

    [Fact]
    public void Parse_EmptySource_ReturnsNoBlocks()
    {
        Assert.Empty(HelpMarkdown.Parse(""));
    }

    [Fact]
    public void Parse_Heading_CapturesLevelAndText()
    {
        var blocks = HelpMarkdown.Parse("## Running a night");

        var heading = Assert.IsType<MdHeading>(Assert.Single(blocks));
        Assert.Equal(2, heading.Level);
        Assert.Equal("Running a night", Join(heading.Words));
    }

    [Fact]
    public void Parse_HashWithoutSpace_IsNotAHeading()
    {
        var blocks = HelpMarkdown.Parse("#notaheading");

        Assert.IsType<MdParagraph>(Assert.Single(blocks));
    }

    [Fact]
    public void Parse_ConsecutiveLines_JoinIntoOneParagraph()
    {
        var blocks = HelpMarkdown.Parse("Bets are funded\nfrom the bank.\n\nSecond paragraph.");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Bets are funded from the bank.", Join(((MdParagraph)blocks[0]).Words));
        Assert.Equal("Second paragraph.", Join(((MdParagraph)blocks[1]).Words));
    }

    [Fact]
    public void Parse_Bullets_AndNumberedItems()
    {
        var blocks = HelpMarkdown.Parse("- Take the gil\n1. Open a session");

        var bullet = Assert.IsType<MdListItem>(blocks[0]);
        Assert.False(bullet.Ordered);
        Assert.Equal("Take the gil", Join(bullet.Words));

        var numbered = Assert.IsType<MdListItem>(blocks[1]);
        Assert.True(numbered.Ordered);
        Assert.Equal("1.", numbered.Marker);
        Assert.Equal("Open a session", Join(numbered.Words));
    }

    [Fact]
    public void Parse_QuoteAndRule()
    {
        var blocks = HelpMarkdown.Parse("> A session must be open.\n\n---");

        Assert.Equal("A session must be open.", Join(Assert.IsType<MdQuote>(blocks[0]).Words));
        Assert.IsType<MdRule>(blocks[1]);
    }

    [Fact]
    public void Parse_FencedCode_KeptVerbatim()
    {
        var blocks = HelpMarkdown.Parse("```\nTwentyOne.json\n  audit/\n```");

        var code = Assert.IsType<MdCode>(Assert.Single(blocks));
        Assert.Equal("TwentyOne.json\n  audit/", code.Text);
    }

    [Fact]
    public void Parse_Table_ReadsHeaderAndBodyRows()
    {
        var blocks = HelpMarkdown.Parse(
            "| Chip | Meaning |\n|---|---|\n| Books OK | Reconciled |\n| Drift | Not reconciled |");

        var table = Assert.IsType<MdTable>(Assert.Single(blocks));
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Chip", Join(table.Rows[0][0]));
        Assert.Equal("Meaning", Join(table.Rows[0][1]));
        Assert.Equal("Books OK", Join(table.Rows[1][0]));
        Assert.Equal("Not reconciled", Join(table.Rows[2][1]));
    }

    [Fact]
    public void Parse_PipeLineWithoutSeparator_IsAParagraph()
    {
        var blocks = HelpMarkdown.Parse("| not | a table |");

        Assert.IsType<MdParagraph>(Assert.Single(blocks));
    }

    [Fact]
    public void Parse_ActionDirective_OnItsOwnLine()
    {
        var blocks = HelpMarkdown.Parse("[[open:win:ledger|Open the Session Ledger]]");

        var action = Assert.IsType<MdAction>(Assert.Single(blocks));
        Assert.Equal("win:ledger", action.Id);
        Assert.Equal("Open the Session Ledger", action.Label);
    }

    [Fact]
    public void Parse_MalformedActionDirective_StaysText()
    {
        Assert.IsType<MdParagraph>(Assert.Single(HelpMarkdown.Parse("[[open:noLabel]]")));
    }

    [Fact]
    public void ParseInline_ResolvesBoldAndCode()
    {
        var words = HelpMarkdown.ParseInline("A **session** must use `/random 13`");

        Assert.Equal(MdStyle.None, WordFor(words, "A").Style);
        Assert.Equal(MdStyle.Bold, WordFor(words, "session").Style);
        Assert.Equal(MdStyle.Code, WordFor(words, "13").Style);
        // The markers themselves never survive into rendered text.
        Assert.DoesNotContain(words, w => w.Text.Contains('*') || w.Text.Contains('`'));
    }

    [Fact]
    public void ParseInline_FirstWordHasNoLeadingSpace()
    {
        var words = HelpMarkdown.ParseInline("Lorah wins");

        Assert.False(words[0].SpaceBefore);
        Assert.True(words[1].SpaceBefore);
    }

    [Fact]
    public void ParseInline_StyleChangeMidWord_DoesNotInventASpace()
    {
        // "**Bet**s" is one visual word split across two style runs.
        var words = HelpMarkdown.ParseInline("**Bet**s");

        Assert.Equal(2, words.Count);
        Assert.False(words[1].SpaceBefore);
    }

    [Fact]
    public void Wrap_BreaksAtTheGivenWidth()
    {
        var words = HelpMarkdown.ParseInline("aaa bbb ccc");

        // "aaa" = 3, " bbb" = 4 -> 7 fits; " ccc" would make 11.
        var lines = HelpMarkdown.Wrap(words, 8f, Measure);

        Assert.Equal(2, lines.Count);
        Assert.Equal("aaa bbb", Join(lines[0]));
        Assert.Equal("ccc", Join(lines[1]));
    }

    [Fact]
    public void Wrap_OverlongWord_IsNeverSplit()
    {
        var words = HelpMarkdown.ParseInline("1,250,000");

        var lines = HelpMarkdown.Wrap(words, 3f, Measure);

        Assert.Equal("1,250,000", Join(Assert.Single(lines)));
    }

    [Fact]
    public void Wrap_NoWords_ProducesNoLines()
    {
        Assert.Empty(HelpMarkdown.Wrap([], 100f, Measure));
    }

    private static MdWord WordFor(IEnumerable<MdWord> words, string text) =>
        words.First(w => w.Text == text);

    private static string Join(IReadOnlyList<MdWord> words) =>
        string.Concat(words.Select((w, i) => i > 0 && w.SpaceBefore ? " " + w.Text : w.Text));
}
