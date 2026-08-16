using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

/// <summary>
/// The in-plugin dealer's guide. Pages are Markdown files under <c>Help/</c>,
/// embedded into the assembly at build time, so the prose is edited as prose and
/// the same files are readable in the repository.
///
/// Parsed lazily and cached per page - a page is only parsed the first time it
/// is opened, and never re-parsed while the plugin is loaded.
/// </summary>
public sealed class HelpWindow : Window
{
    /// <param name="Id">Stable id used by <c>[[open:...]]</c> cross-links.</param>
    private readonly record struct Topic(string Id, string Title, string Resource);

    private static readonly Topic[] Topics =
    [
        new("start",      "Start here",            "00-start-here.md"),
        new("setup",      "Before your first night","01-first-night.md"),
        new("night",      "Running a night",       "02-running-a-night.md"),
        new("banks",      "Banks and trades",      "03-banks-and-trades.md"),
        new("books",      "Reading the books",     "04-the-books.md"),
        new("rules",      "Rules and house edge",  "05-rules-and-edge.md"),
        new("settling",   "Settling up",           "06-settling-up.md"),
        new("trouble",    "When something goes wrong", "07-troubleshooting.md"),
    ];

    private readonly Action<string> onAction;
    private readonly Dictionary<string, IReadOnlyList<MdBlock>> pageCache = [];
    private int selected;
    // Set on every navigation: a new page must start at the top rather than
    // inheriting the previous page's scroll position.
    private bool scrollToTop;

    /// <param name="onAction">
    /// Handles an <c>[[open:win:name|Label]]</c> click by opening that window.
    /// Directives naming a help topic instead (<c>[[open:books|...]]</c>) are
    /// handled here as in-page navigation and never reach the caller.
    /// </param>
    public HelpWindow(Action<string> onAction)
        : base("Twenty One - Guide##TwentyOneHelp")
    {
        this.onAction = onAction;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size          = new Vector2(760, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags         = ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Opens the window on a specific page (used by contextual Help links).</summary>
    public void ShowTopic(string id)
    {
        var idx = Array.FindIndex(Topics, t => t.Id == id);
        if (idx >= 0) Select(idx);
        IsOpen = true;
    }

    private void Select(int index)
    {
        selected    = index;
        scrollToTop = true;
    }

    public override void Draw()
    {
        if (ImGui.BeginChild("##helpNav", new Vector2(190, 0), true))
        {
            for (var i = 0; i < Topics.Length; i++)
            {
                if (ImGui.Selectable($"{Topics[i].Title}##helpTopic{i}", selected == i))
                    Select(i);
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##helpBody", new Vector2(0, 0), false))
        {
            if (scrollToTop)
            {
                ImGui.SetScrollY(0);
                scrollToTop = false;
            }
            MarkdownView.Draw(GetPage(Topics[selected]), HandleAction);
        }
        ImGui.EndChild();
    }

    // A link to another help page navigates in place; anything else is a window
    // the host plugin owns.
    private void HandleAction(string id)
    {
        var idx = Array.FindIndex(Topics, t => t.Id == id);
        if (idx >= 0)
        {
            Select(idx);
            return;
        }
        onAction(id);
    }

    private IReadOnlyList<MdBlock> GetPage(Topic topic)
    {
        if (pageCache.TryGetValue(topic.Resource, out var cached)) return cached;
        var parsed = HelpMarkdown.Parse(LoadResource(topic.Resource));
        pageCache[topic.Resource] = parsed;
        return parsed;
    }

    // A missing page is a packaging error, not a user-facing failure mode - show
    // it plainly rather than throwing out of Draw.
    private static string LoadResource(string fileName)
    {
        var name = "TwentyOne.Help." + fileName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return $"# Missing page\n\nThe help page `{name}` was not embedded in this build.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
