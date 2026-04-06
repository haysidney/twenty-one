using System;

namespace TwentyOne.Game;

[Serializable]
public class NarrationTemplates
{
    // Betting phase open announcement
    public string BettingOpen { get; set; } = "Betting is now open! Min: 50,000 / Max: 250,000 — I'll initiate a trade with you, please be patient!";

    // Dealer card draws (DealerTurn phase)
    public string DealerHit  { get; set; } = "Dealer draws {card} → {cards} = {score}";
    public string DealerBust { get; set; } = "Dealer draws {card} → {cards} = {score} — Bust!";
    public string DealerBJ   { get; set; } = "Dealer draws {card} → {cards} — Blackjack!";

    // Player actions (PlayerTurns phase)
    // {actions} = e.g. "Hit, Stand" or "Hit, Stand, Split"
    public string PlayerTurnStart { get; set; } = "{name}'s turn — Dealer shows {dealerCards} ({dealerScore}). {actions}";
    public string PlayerHit   { get; set; } = "{name} hits → {card} | {cards} = {score}";
    public string PlayerBust  { get; set; } = "{name} busts! {cards} = {score}";
    public string PlayerBJ    { get; set; } = "{name} — Blackjack! {cards}";
    public string PlayerStand { get; set; } = "{name} stands. {cards} = {score}";

    // Initial deal announcements (Deal phase)
    public string DealDealerCard  { get; set; } = "Dealer's Card:";
    public string DealPlayerHand  { get; set; } = "{name}'s Hand:";

    // Deal summary (BeginPlayerTurns): prefix + one entry per player + dealer suffix
    public string DealSummaryPrefix { get; set; } = "Deal — ";
    public string DealSummaryPlayer { get; set; } = "{name}: {cards} ({score}){bj}";
    public string DealSummaryDealer { get; set; } = " | Dealer shows {cards}";

    // Payout
    public string PayoutDealerBust   { get; set; } = "Dealer busts ({score})";
    public string PayoutDealerStands { get; set; } = "Dealer {score}";
    // {bet} = " (bet: 100)" or ""; {amount} = " +150" or ""
    public string PayoutPlayer { get; set; } = "{name}: {result}{bet}{amount}";

    /// Use <c>{|}</c> in any template to split the output into multiple chat messages.
    public static string Fmt(string template, params (string Key, string Value)[] vars)
    {
        foreach (var (k, v) in vars)
            template = template.Replace("{" + k + "}", v);
        return template;
    }
}
