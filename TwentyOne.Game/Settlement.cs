using System;

namespace TwentyOne.Game;

/// <summary>
/// What the venue does with the table's losses on a losing night. Wins are
/// always split by <c>VenueCutPct</c>; losses are a venue-policy question,
/// because the dealer is the one physically holding (and short) the gil.
/// </summary>
public enum LossCoverage
{
    /// <summary>Venue eats the whole loss - the dealer walks away whole.</summary>
    VenueCoversAll,
    /// <summary>Venue eats the same percentage of the loss that it takes of a win.</summary>
    VenueCoversShare,
    /// <summary>Venue covers nothing; the dealer absorbs the loss.</summary>
    DealerAbsorbs,
}

/// <summary>
/// End-of-night split between dealer and venue.
///
/// The framing that matters: the dealer physically holds <em>all</em> of this
/// gil already - table winnings, tips, service charges, the lot. Nobody
/// "receives" anything except through a single trade at the end of the night.
/// So the headline number is <see cref="NetTransfer"/>: signed, one direction,
/// one amount to type into a trade window.
/// </summary>
public readonly record struct Settlement(
    long TableNet,
    long Tips,
    long ServiceToDealer,
    long ServiceToVenue,
    long VenueShare,
    long DealerShare)
{
    /// <summary>
    /// Gil moving between dealer and venue. Positive = the dealer pays the
    /// venue; negative = the venue pays the dealer (loss coverage exceeded the
    /// venue's service-charge claim).
    /// </summary>
    public long NetTransfer => VenueShare + ServiceToVenue;

    /// <summary>True when gil flows dealer -> venue.</summary>
    public bool DealerPaysVenue => NetTransfer > 0;

    /// <summary>Unsigned magnitude of <see cref="NetTransfer"/>, for display.</summary>
    public long TransferAmount => Math.Abs(NetTransfer);

    /// <summary>
    /// What the dealer is left with after settling up. Tips never enter the
    /// split - they pass straight through to this line.
    /// </summary>
    public long DealerTake => DealerShare + Tips + ServiceToDealer;

    /// <summary>
    /// Compute the split. <paramref name="tableNet"/> is the reconciled house
    /// win/loss off the players (the session ledger's adjusted difference):
    /// tips, service charges, bets in play and player banks are already out of
    /// it, so it is purely the real gil the table took or gave up.
    ///
    /// <b>Venue-funded credit deliberately does not appear here.</b> Credit moves
    /// no real gil when issued, so it only reaches settlement through whatever
    /// gil actually left the pile - which <paramref name="tableNet"/> already
    /// measures. A session cannot close with player banks outstanding
    /// (<see cref="SessionManager.CheckClose"/>), so by settlement time every
    /// credit has resolved: lost back (no gil moved, nothing to settle) or cashed
    /// out (a real loss, covered by <paramref name="lossCoverage"/>). Adding a
    /// credit reimbursement line on top would pay the dealer twice.
    /// </summary>
    public static Settlement Compute(
        long tableNet, long tips, long serviceToDealer, long serviceToVenue,
        int venueCutPct, LossCoverage lossCoverage)
    {
        var pct = Math.Clamp(venueCutPct, 0, 100) / 100.0;

        long venueShare;
        if (tableNet > 0)
        {
            // Floor the venue's slice so the rounding gil falls to the dealer.
            venueShare = (long)Math.Floor(tableNet * pct);
        }
        else if (tableNet < 0)
        {
            venueShare = lossCoverage switch
            {
                LossCoverage.VenueCoversAll => tableNet,
                // Ceiling the magnitude keeps the rounding gil favouring the
                // dealer here too - the venue absorbs the extra 1 gil, not them.
                LossCoverage.VenueCoversShare => -(long)Math.Ceiling(-tableNet * pct),
                _ => 0,
            };
        }
        else
        {
            venueShare = 0;
        }

        return new Settlement(
            TableNet:        tableNet,
            Tips:            tips,
            ServiceToDealer: serviceToDealer,
            ServiceToVenue:  serviceToVenue,
            VenueShare:      venueShare,
            DealerShare:     tableNet - venueShare);
    }
}
