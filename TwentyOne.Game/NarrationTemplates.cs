using System;

namespace TwentyOne.Game;

[Serializable]
public class NarrationTemplates
{
    // Betting phase open announcement
    public string BettingOpen { get; set; } = "Betting is now open! Min: 50,000 / Max: 250,000 — I'll initiate a trade with you, please be patient!{|}/wringhands";

    // Dealer card draws (DealerTurn phase)
    // {dealer} = dealer's name (configured in Settings)
    public string DealerTurnStart   { get; set; } = "{dealer}'s turn — showing {cards} ({score}).";
    public string DealerHitAnnounce { get; set; } = "{dealer} hits!";
    public string DealerHit  { get; set; } = "{dealer} draws {card} → {cards} = {score}";
    public string DealerBust  { get; set; } = "{dealer} draws {card} → {cards} = {score} — Bust!";
    public string DealerBJ    { get; set; } = "{dealer} draws {card} → {cards} — Blackjack!";
    public string DealerStand { get; set; } = "{dealer} stands. {cards} = {score}";

    // Player actions (PlayerTurns phase)
    // {actions} = e.g. "Hit or Stand" or "Hit or Stand, Double, Split"
    public string PlayerHitAnnounce { get; set; } = "{name} hits!";
    public string PlayerTurnStart { get; set; } = "{name}'s turn ({score}) — Dealer shows {dealerCards} ({dealerScore}). {actions}";
    // After a hit that leaves the hand still Playing: show score and ask what to do next
    // {name} {cards} {score} {actions}
    public string PlayerAfterHit { get; set; } = "{name} — {cards} = {score}. {actions}?";
    public string PlayerHit   { get; set; } = "{name} hits → {card} | {cards} = {score}";
    public string PlayerBust  { get; set; } = "{name} busts! {cards} = {score}";
    public string PlayerBJ    { get; set; } = "{name} — Blackjack! {cards}";
    public string PlayerStand { get; set; } = "{name} stands. {cards} = {score}";

    // Double down — sent when the card lands and the hand is auto-stood
    // {name} may include "(Hand N)" for split hands
    public string PlayerDouble { get; set; } = "{name} doubles down → {card} | {cards} = {score}";

    // Split ace mandatory card — card dealt, hand auto-stood per split-ace rule
    public string PlayerSplitAce { get; set; } = "{name} draws {card} — {cards} = {score} (split aces, auto-stand)";

    // Bet-collection request (sent when dealer shift+clicks Trade during Betting)
    // {name} = player display name
    public string PlayerBetRequest { get; set; } = "{name}, please trade your bet to me.";

    // Bet-confirm announcement (sent when dealer clicks Confirm in the Bet cell during Betting)
    // {name} = player display name, {amount} = bet amount
    public string PlayerBetConfirm { get; set; } = "{name}, your current bet is {amount}. If you want to change it let me know.";

    // Trade-request announcements (sent when dealer clicks Double/Split, before confirming trade)
    // {amount} = the extra chips required
    public string PlayerDoubleRequest { get; set; } = "{name} would like to double down! Please trade {amount} gil to the dealer.";
    public string PlayerSplitRequest  { get; set; } = "{name} would like to split! Please trade {amount} gil to the dealer.";

    // Sent when the split is confirmed (after trade)
    public string PlayerSplit { get; set; } = "{name} splits into two hands!";

    // Sent before rolling the mandatory 2nd card for a split hand
    public string PlayerSplitRoll { get; set; } = "Rolling 2nd card for {name}...";

    // Initial deal announcements (Deal phase)
    public string DealDealerCard  { get; set; } = "{dealer}'s Card:";
    public string DealPlayerHand  { get; set; } = "{name}'s Hand:";

    // Deal summary (BeginPlayerTurns): prefix + one entry per player + dealer suffix
    public string DealSummaryPrefix { get; set; } = "Deal — ";
    public string DealSummaryPlayer { get; set; } = "{name}: {cards} ({score}){bj}";
    public string DealSummaryDealer { get; set; } = " | {dealer} shows {cards}";

    // Payout
    public string PayoutDealerBust   { get; set; } = "{dealer} busts ({score})";
    public string PayoutDealerStands { get; set; } = "{dealer} {score}";
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
