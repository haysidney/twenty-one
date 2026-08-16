using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TwentyOne.Game;

/// <summary>Inline style flags carried by a run of help text.</summary>
[Flags]
public enum MdStyle
{
    None = 0,
    Bold = 1,
    Code = 2,
}

/// <summary>One styled word, plus whether a space separated it from the previous one.</summary>
public sealed record MdWord(string Text, MdStyle Style, bool SpaceBefore);

/// <summary>One rendered element of a help page.</summary>
public abstract record MdBlock
{
    /// <summary>Block type name, for diagnostics and test failure messages.</summary>
    public string Kind => GetType().Name;
}

public sealed record MdHeading(int Level, IReadOnlyList<MdWord> Words)            : MdBlock;
public sealed record MdParagraph(IReadOnlyList<MdWord> Words)                     : MdBlock;
public sealed record MdListItem(bool Ordered, string Marker, IReadOnlyList<MdWord> Words) : MdBlock;
public sealed record MdQuote(IReadOnlyList<MdWord> Words)                         : MdBlock;
public sealed record MdCode(string Text)                                          : MdBlock;

/// <summary>A horizontal rule. Carries no data; use <see cref="Instance"/>.</summary>
public sealed record MdRule : MdBlock
{
    public static readonly MdRule Instance = new();
}

public sealed record MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MdWord>>> Rows) : MdBlock;

/// <summary>
/// An <c>[[open:id|Label]]</c> directive on a line of its own. Renders as a
/// button that opens the named window, so the guide can say "click here" and
/// mean it.
/// </summary>
public sealed record MdAction(string Id, string Label) : MdBlock;

/// <summary>
/// Minimal Markdown subset used by the in-plugin help guide. Deliberately not a
/// general Markdown implementation - it parses exactly the constructs the help
/// pages use, and nothing else.
///
/// Lives here rather than in the plugin project for the same reason as
/// <see cref="ChatRouting"/>: the plugin assembly is unreachable from tests, and
/// the parsing and line-wrapping are the parts worth pinning.
///
/// Supported: ATX headings, paragraphs, <c>-</c> bullets, <c>N.</c> numbered
/// items, <c>&gt;</c> quotes, fenced code blocks, <c>---</c> rules, pipe tables,
/// and the <c>[[open:id|Label]]</c> action directive. Inline: <c>**bold**</c>
/// and <c>`code`</c>.
/// </summary>
public static class HelpMarkdown
{
    public static IReadOnlyList<MdBlock> Parse(string source)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrEmpty(source)) return blocks;

        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var para  = new List<string>();

        void FlushParagraph()
        {
            if (para.Count == 0) return;
            blocks.Add(new MdParagraph(ParseInline(string.Join(" ", para))));
            para.Clear();
        }

        // A while loop rather than a for: the fenced-code and table cases consume
        // several source lines at once, so the cursor is advanced by hand.
        var i = 0;
        while (i < lines.Length)
        {
            var trimmed = lines[i].Trim();
            i++;

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // Fenced code block - consumed verbatim until the closing fence.
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var code = new StringBuilder();
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                i++; // step past the closing fence
                blocks.Add(new MdCode(code.ToString()));
                continue;
            }

            if (IsRule(trimmed))
            {
                FlushParagraph();
                blocks.Add(MdRule.Instance);
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                if (level <= 6 && level < trimmed.Length && trimmed[level] == ' ')
                {
                    FlushParagraph();
                    blocks.Add(new MdHeading(level, ParseInline(trimmed[(level + 1)..].Trim())));
                    continue;
                }
            }

            if (TryParseAction(trimmed, out var action))
            {
                FlushParagraph();
                blocks.Add(action);
                continue;
            }

            // Pipe table: a header row followed by a |---|---| separator.
            if (trimmed.StartsWith('|') && i < lines.Length && IsTableSeparator(lines[i].Trim()))
            {
                FlushParagraph();
                var rows = new List<IReadOnlyList<IReadOnlyList<MdWord>>> { ParseTableRow(trimmed) };
                i++; // step past the separator
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    rows.Add(ParseTableRow(lines[i].Trim()));
                    i++;
                }
                blocks.Add(new MdTable(rows));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new MdQuote(ParseInline(trimmed[2..].Trim())));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new MdListItem(false, "-", ParseInline(trimmed[2..].Trim())));
                continue;
            }

            if (TryParseOrderedMarker(trimmed, out var marker, out var rest))
            {
                FlushParagraph();
                blocks.Add(new MdListItem(true, marker, ParseInline(rest)));
                continue;
            }

            para.Add(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>
    /// Greedy word wrap. Pure so it can be unit-tested: the caller supplies the
    /// text measurement (ImGui's CalcTextSize in the plugin, a stub in tests).
    /// A single word wider than the line is never split - it simply overhangs,
    /// which is preferable to slicing a gil figure in half.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<MdWord>> Wrap(
        IReadOnlyList<MdWord> words, float maxWidth, Func<string, float> measure)
    {
        var lines = new List<IReadOnlyList<MdWord>>();
        var line  = new List<MdWord>();
        var width = 0f;

        foreach (var word in words)
        {
            var spaced = line.Count > 0 && word.SpaceBefore;
            var text   = spaced ? " " + word.Text : word.Text;
            var w      = measure(text);

            if (line.Count > 0 && width + w > maxWidth)
            {
                lines.Add(line);
                line  = [word];
                width = measure(word.Text);
                continue;
            }

            line.Add(word);
            width += w;
        }

        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    // ── Inline ────────────────────────────────────────────────────────────────

    /// <summary>Splits a line into styled words, resolving **bold** and `code`.</summary>
    public static IReadOnlyList<MdWord> ParseInline(string text)
    {
        var words = new List<MdWord>();
        var buf   = new StringBuilder();
        var style = MdStyle.None;
        // False only for the very first word on the line, and after a space.
        var spaceBefore = false;

        void Flush()
        {
            if (buf.Length == 0) return;
            words.Add(new MdWord(buf.ToString(), style, spaceBefore));
            buf.Clear();
            spaceBefore = false;
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            i++;

            if (c == ' ')
            {
                Flush();
                spaceBefore = true;
                continue;
            }

            if (c == '*' && i < text.Length && text[i] == '*')
            {
                Flush();
                style ^= MdStyle.Bold;
                i++;
                continue;
            }

            if (c == '`')
            {
                Flush();
                style ^= MdStyle.Code;
                continue;
            }

            buf.Append(c);
        }

        Flush();
        return words;
    }

    // ── Line classification ───────────────────────────────────────────────────

    private static bool IsRule(string trimmed) =>
        trimmed.Length >= 3 && trimmed.All(c => c == '-');

    private static bool IsTableSeparator(string trimmed)
    {
        if (!trimmed.StartsWith('|')) return false;
        var sawDash = false;
        foreach (var c in trimmed)
        {
            if (c == '-') sawDash = true;
            else if (c is not ('|' or ' ' or ':')) return false;
        }
        return sawDash;
    }

    private static IReadOnlyList<IReadOnlyList<MdWord>> ParseTableRow(string line)
    {
        var body  = line.Trim('|');
        var cells = new List<IReadOnlyList<MdWord>>();
        foreach (var cell in body.Split('|'))
            cells.Add(ParseInline(cell.Trim()));
        return cells;
    }

    private static bool TryParseOrderedMarker(string trimmed, out string marker, out string rest)
    {
        marker = string.Empty;
        rest   = string.Empty;
        var d = 0;
        while (d < trimmed.Length && char.IsAsciiDigit(trimmed[d])) d++;
        if (d == 0 || d + 1 >= trimmed.Length) return false;
        if (trimmed[d] != '.' || trimmed[d + 1] != ' ') return false;
        marker = trimmed[..(d + 1)];
        rest   = trimmed[(d + 2)..].Trim();
        return true;
    }

    private static bool TryParseAction(string trimmed, out MdAction action)
    {
        action = null!;
        if (!trimmed.StartsWith("[[open:", StringComparison.Ordinal)) return false;
        if (!trimmed.EndsWith("]]", StringComparison.Ordinal)) return false;

        var inner = trimmed[7..^2];
        var pipe  = inner.IndexOf('|');
        if (pipe <= 0 || pipe == inner.Length - 1) return false;

        action = new MdAction(inner[..pipe].Trim(), inner[(pipe + 1)..].Trim());
        return true;
    }
}
