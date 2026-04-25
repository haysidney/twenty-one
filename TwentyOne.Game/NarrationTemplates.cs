using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace TwentyOne.Game;

[Serializable]
public class NarrationTemplates
{
    // Betting phase open announcement
    public List<string> BettingOpen { get; set; } =
    [
        "Betting is now open! Min: 50,000 / Max: 250,000 — I'll initiate a trade with you, please be patient!",
        "/wringhands",
    ];

    // Dealer card draws (DealerTurn phase)
    // {dealer} = dealer's name (configured in Settings)
    public List<string> DealerTurnStart   { get; set; } = ["{dealer}'s turn — showing {cards} ({score})."];
    public List<string> DealerHitAnnounce { get; set; } = ["{dealer} hits!"];
    // Used instead of DealerHitAnnounce when all players have blackjack and dealer is checking for BJ.
    public List<string> DealerBJCheck { get; set; } = ["Let's see if I get lucky! \u2665"];
    public List<string> DealerHit         { get; set; } = ["{dealer} draws {card} → {cards} = {score}"];
    public List<string> DealerBust        { get; set; } = ["{dealer} draws {card} → {cards} = {score} — Bust!"];
    public List<string> DealerBJ          { get; set; } = ["{dealer} draws {card} → {cards} — Blackjack!"];
    public List<string> DealerStand       { get; set; } = ["{dealer} stands. {cards} = {score}"];

    // Player actions (PlayerTurns phase)
    // {actions} = e.g. "Hit or Stand" or "Hit or Stand, Double Down, Split"
    public List<string> PlayerHitAnnounce { get; set; } = ["{name} hits!"];
    public List<string> PlayerTurnStart   { get; set; } = ["{name}'s turn: {cards} ({score}) — Dealer shows {dealerCards} ({dealerScore}). {actions}"];
    // After a hit that leaves the hand still Playing: show score and ask what to do next
    // {name} {cards} {score} {actions}
    public List<string> PlayerAfterHit    { get; set; } = ["{name} — {cards} = {score}. {actions}?"];
    public List<string> PlayerHit         { get; set; } = ["{name} hits → {card} | {cards} = {score}"];
    public List<string> PlayerBust        { get; set; } = ["{name} busts! {cards} = {score}"];
    public List<string> PlayerBJ          { get; set; } = ["{name} — Blackjack! {cards}"];
    // Shown in the old PlayerBJ position (player-turns phase) when multiple players are at the table.
    // {name} {cards}
    public List<string> PlayerBJMovingAlong { get; set; } = ["{name} has a blackjack so we'll just move along \u2665"];
    public List<string> PlayerStand       { get; set; } = ["{name} stands. {cards} = {score}"];

    // Double down — sent when the card lands and the hand is auto-stood
    // {name} may include "(Hand N)" for split hands
    public List<string> PlayerDouble { get; set; } = ["{name} doubles down → {card} | {cards} = {score}"];

    // Split ace mandatory card — card dealt, hand auto-stood per split-ace rule
    public List<string> PlayerSplitAce { get; set; } = ["{name} draws {card} — {cards} = {score} (split aces, auto-stand)"];

    // Five Card Charlie — {name} {card} {cards} {score}
    public List<string> PlayerCharlie { get; set; } = ["{name} — Five Card Charlie! {card} | {cards} = {score}"];

    // Bet-collection request (sent when dealer shift+clicks Trade during Betting)
    // {name} = player display name
    public List<string> PlayerBetRequest { get; set; } = ["{name}, please trade your bet to me."];

    // Bet-confirm announcement (sent when dealer clicks Confirm in the Bet cell during Betting)
    // {name} = player display name, {amount} = bet amount
    public List<string> PlayerBetConfirm { get; set; } = ["{name}, your current bet is {amount}. If you want to change it let me know."];

    // Bank remind — sent from Bank column Remind button
    // {name} = player display name, {amount} = bet amount, {bank} = bank balance
    public List<string> PlayerBankRemind { get; set; } = ["{name}, your bet is {amount} and your bank balance is {bank}."];

    // Bank shortfall request — sent when dealer shift+clicks Deposit and bank < bet
    // {name} = player display name, {amount} = shortfall amount
    public List<string> PlayerBankShortfall { get; set; } = ["{name}, please trade {amount} to cover your bet."];

    // Bank deposit/withdraw log entries — {name} = player, {amount} = changed amount, {bank} = new balance
    public List<string> PlayerBankDeposit  { get; set; } = ["{name} deposited {amount}. Bank: {bank}."];
    public List<string> PlayerBankWithdraw { get; set; } = ["{name} withdrew {amount}. Bank: {bank}."];

    // Trade-request announcements (sent when dealer clicks Double/Split, before confirming trade)
    // {amount} = the extra chips required
    public List<string> PlayerDoubleRequest { get; set; } = ["{name} would like to double down! Please trade {amount} gil to the dealer."];
    public List<string> PlayerSplitRequest  { get; set; } = ["{name} would like to split! Please trade {amount} gil to the dealer."];

    // Sent when dealer clicks Confirm Dbl (trade received, card about to be drawn)
    // {name} = player display name
    public List<string> PlayerDoubleConfirm { get; set; } = ["Good luck, {name}!"];

    // Sent when the split is confirmed (after trade)
    public List<string> PlayerSplit { get; set; } = ["{name} splits into two hands!"];

    // Sent before rolling the mandatory 2nd card for a split hand
    public List<string> PlayerSplitRoll { get; set; } = ["Rolling 2nd card for {name}..."];

    // Initial deal announcements (Deal phase)
    public List<string> DealDealerCard { get; set; } = ["{dealer}'s Card:"];
    public List<string> DealPlayerHand { get; set; } = ["{name}'s Hand:"];

    // Deal summary building blocks — concatenated into a single chat message, not narrated independently
    public string DealSummaryPrefix { get; set; } = "Deal — ";
    public string DealSummaryPlayer { get; set; } = "{name}: {cards} ({score}){bj}";
    public string DealSummaryDealer { get; set; } = " | {dealer} shows {cards}";

    // Payout
    public List<string> PayoutHeader { get; set; } = ["Summary:"];

    // Combined payout for split hands where all hands win — replaces per-hand lines
    // {name} = player name; {amount} = combined payout amount (e.g. "+300g")
    public List<string> PayoutSplitCombined { get; set; } = ["{name}: Split wins {amount}"];

    public List<string> PayoutDealerBust   { get; set; } = ["{dealer} busts ({score})"];
    public List<string> PayoutDealerStands { get; set; } = ["{dealer} {score}"];
    // {name} may include "(Hand N)"; {bet} = "100g" or ""; {amount} = "+150g" or "-100g" or ""
    public List<string> PayoutWin        { get; set; } = ["{name}: Win (bet: {bet}) {amount}"];
    public List<string> PayoutBjWin      { get; set; } = ["{name}: Blackjack! (bet: {bet}) {amount}"];
    public List<string> PayoutCharlieWin { get; set; } = ["{name}: Five Card Charlie! (bet: {bet}) {amount}"];
    public List<string> PayoutLose       { get; set; } = ["{name}: Lose (bet: {bet}) {amount}"];
    public List<string> PayoutPush       { get; set; } = ["{name}: Push (bet: {bet})"];

    // Newtonsoft.Json reuses existing List instances and appends to them.
    // Clear all lists before deserialization so defaults don't accumulate on reload.
    [OnDeserializing]
    internal void OnDeserializing(StreamingContext _)
    {
        foreach (var prop in GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(List<string>)))
            prop.SetValue(this, new List<string>());
    }

    public static string Fmt(string template, params (string Key, string Value)[] vars)
    {
        foreach (var (k, v) in vars)
            template = template.Replace("{" + k + "}", v);
        return template;
    }
}
