namespace TwentyOne.Game;

public abstract record SideEffect;

/// <summary>A line of narration to display in the log and optionally send to FFXIV chat.</summary>
public record SendChat(string Text) : SideEffect;
