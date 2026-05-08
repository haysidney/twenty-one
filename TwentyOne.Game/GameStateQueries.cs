using System.Collections.Generic;
using System.Linq;

namespace TwentyOne.Game;

/// <summary>
/// Read-only convenience queries over <see cref="GameState"/>. Pure-core only —
/// these are derived predicates that show up at many call sites and benefit
/// from a shared name and definition.
/// </summary>
public static class GameStateQueries
{
    public static bool IsBetting(this GameState s) => s.Phase == GamePhase.Betting;

    /// <summary>True for any phase except Betting (i.e. a round is in progress).</summary>
    public static bool IsRoundActive(this GameState s) => s.Phase != GamePhase.Betting;

    /// <summary>Players that are not sitting out.</summary>
    public static IEnumerable<Player> ActivePlayers(this GameState s) =>
        s.Players.Where(p => !p.SittingOut);

    public static int ActivePlayerCount(this GameState s) =>
        s.Players.Count(p => !p.SittingOut);

    /// <summary>
    /// True if every non-sitting-out player has busted on every hand. Returns
    /// false for an empty roster — there is no round to evaluate.
    /// </summary>
    public static bool IsAllBust(this GameState s) =>
        s.Players.Length > 0
        && s.Players.All(p => p.SittingOut || p.Hands.All(h => h.State == HandState.Bust));
}
