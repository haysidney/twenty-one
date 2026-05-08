namespace TwentyOne.Game;

/// <summary>Base type for side effects produced by GameEngine.Apply.</summary>
public interface ISideEffect;

/// <summary>A line of narration to display in the log and optionally send to FFXIV chat.</summary>
public record SendChat(string Text) : ISideEffect;

/// <summary>The engine requires a mandatory hit for a split hand before the player may act.</summary>
public record AutoHit(int PlayerIndex, int HandIndex) : ISideEffect;
