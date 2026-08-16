using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TwentyOne.Game;

namespace TwentyOne.Windows;

/// <summary>
/// Renders the parsed help-page blocks from <see cref="HelpMarkdown"/> with ImGui.
///
/// Only the drawing lives here - parsing and line wrapping are pure and sit in
/// TwentyOne.Game so they can be unit-tested. Dalamud ships no bold font, so
/// **bold** is rendered as a brighter colour rather than a heavier weight.
/// </summary>
internal static class MarkdownView
{
    private static readonly Vector4 BoldText   = new(1f,    0.95f, 0.80f, 1f);
    private static readonly Vector4 CodeText   = new(0.60f, 0.85f, 1f,    1f);
    private static readonly Vector4 HeadingTop = GameColors.BannerGold;
    private static readonly Vector4 HeadingSub = new(1f,    0.90f, 0.55f, 1f);

    private const float ListIndent  = 18f;
    private const float QuoteIndent = 12f;

    /// <param name="onAction">
    /// Invoked with the id of an <c>[[open:id|Label]]</c> button the reader clicks.
    /// </param>
    public static void Draw(IReadOnlyList<MdBlock> blocks, Action<string> onAction)
    {
        var tableSeq = 0;
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MdHeading h:      DrawHeading(h);            break;
                case MdParagraph p:    DrawWrapped(p.Words);
                                       ImGui.Spacing();           break;
                case MdListItem li:    DrawListItem(li);          break;
                case MdQuote q:        DrawQuote(q);              break;
                case MdCode c:         DrawCode(c);               break;
                case MdRule:           ImGui.Separator();
                                       ImGui.Spacing();           break;
                case MdTable t:        DrawTable(t, tableSeq++);  break;
                case MdAction a:       DrawAction(a, onAction);   break;
            }
        }
    }

    private static void DrawHeading(MdHeading h)
    {
        ImGui.Spacing();
        var color = h.Level <= 1 ? HeadingTop : HeadingSub;
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        DrawWrapped(h.Words);
        ImGui.PopStyleColor();
        if (h.Level <= 2) ImGui.Separator();
        ImGui.Spacing();
    }

    private static void DrawListItem(MdListItem li)
    {
        // The marker is drawn first, then the cursor is pushed to a fixed indent
        // so that "-" and "10." items line their text up in the same column.
        var marker  = li.Ordered ? li.Marker : "-";
        var markerX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted(marker);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorPosX(markerX + ListIndent);
        DrawWrapped(li.Words);
    }

    private static void DrawQuote(MdQuote q)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, GameColors.DisabledGrey);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + QuoteIndent);
        DrawWrapped(q.Words);
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static void DrawCode(MdCode c)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, CodeText);
        foreach (var line in c.Text.Split('\n'))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ListIndent);
            ImGui.TextUnformatted(line);
        }
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static void DrawTable(MdTable t, int seq)
    {
        if (t.Rows.Count == 0) return;
        var cols = t.Rows.Max(row => row.Count);
        if (cols == 0) return;

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                      ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"##mdtable{seq}", cols, flags)) return;

        for (var r = 0; r < t.Rows.Count; r++)
        {
            ImGui.TableNextRow(r == 0 ? ImGuiTableRowFlags.Headers : ImGuiTableRowFlags.None);
            for (var c = 0; c < cols; c++)
            {
                ImGui.TableSetColumnIndex(c);
                if (c >= t.Rows[r].Count) continue;
                if (r == 0) ImGui.PushStyleColor(ImGuiCol.Text, BoldText);
                DrawWrapped(t.Rows[r][c]);
                if (r == 0) ImGui.PopStyleColor();
            }
        }

        ImGui.EndTable();
        ImGui.Spacing();
    }

    private static void DrawAction(MdAction a, Action<string> onAction)
    {
        if (ImGui.Button($"{a.Label}##mdact{a.Id}"))
            onAction(a.Id);
        ImGui.Spacing();
    }

    /// <summary>
    /// Lays out a run of styled words, wrapping at the content edge. Words are
    /// emitted one ImGui item at a time (SameLine with no spacing) because a
    /// single Text call cannot change colour mid-line.
    /// </summary>
    private static void DrawWrapped(IReadOnlyList<MdWord> words)
    {
        if (words.Count == 0)
        {
            ImGui.NewLine();
            return;
        }

        // ContentRegionAvail is measured from the current cursor, so callers that
        // have already indented (list markers, quotes) need no adjustment here.
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail < 40f) avail = 40f;

        var lines  = HelpMarkdown.Wrap(words, avail, s => ImGui.CalcTextSize(s).X);
        var startX = ImGui.GetCursorPosX();

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) ImGui.SetCursorPosX(startX);
            DrawLine(lines[i]);
        }
    }

    private static void DrawLine(IReadOnlyList<MdWord> line)
    {
        for (var i = 0; i < line.Count; i++)
        {
            var word = line[i];
            var text = i > 0 && word.SpaceBefore ? " " + word.Text : word.Text;
            if (i > 0) ImGui.SameLine(0, 0);

            if (word.Style.HasFlag(MdStyle.Code))
                ImGui.TextColored(CodeText, text);
            else if (word.Style.HasFlag(MdStyle.Bold))
                ImGui.TextColored(BoldText, text);
            else
                ImGui.TextUnformatted(text);
        }
    }
}
