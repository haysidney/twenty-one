using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace TwentyOne.Game;

[Serializable]
public class NarrationTemplates
{
    // Betting phase open announcement
    public List<List<string>> BettingOpen { get; set; } =
    [
        [
            "Betting is now open! Min: 50,000 / Max: 250,000 - I'll initiate a trade with you, please be patient!",
            "/wringhands",
        ],
    ];

    // Dealer card draws (DealerTurn phase)
    // {dealer} = dealer's name (configured in Settings)
    public List<List<string>> DealerTurnStart   { get; set; } = [["{dealer}'s turn - showing {cards} ({score})."]];
    public List<List<string>> DealerHitAnnounce { get; set; } = [["{dealer} hits!"]];
    // Used instead of DealerHitAnnounce when all players have blackjack and dealer is checking for BJ.
    public List<List<string>> DealerBJCheck { get; set; } = [["Let's see if I get lucky! ♥"]];
    public List<List<string>> DealerHit         { get; set; } = [["{dealer} draws {card} → {cards} = {score}"]];
    public List<List<string>> DealerBust        { get; set; } = [["{dealer} draws {card} → {cards} = {score} - Bust!"]];
    public List<List<string>> DealerBJ          { get; set; } = [["{dealer} draws {card} → {cards} - Blackjack!"]];
    public List<List<string>> DealerStand       { get; set; } = [["{dealer} stands. {cards} = {score}"]];

    // Player actions (PlayerTurns phase)
    // {actions} = e.g. "Hit or Stand" or "Hit or Stand, Double Down, Split"
    public List<List<string>> PlayerHitAnnounce { get; set; } = [["{name} hits!"]];
    public List<List<string>> PlayerTurnStart   { get; set; } = [["{name}'s turn: {cards} ({score}) - Dealer shows {dealerCards} ({dealerScore}). {actions}"]];
    // After a hit that leaves the hand still Playing: show score and ask what to do next
    // {name} {cards} {score} {actions}
    public List<List<string>> PlayerAfterHit    { get; set; } = [["{name} - {cards} = {score}. {actions}?"]];
    public List<List<string>> PlayerHit         { get; set; } = [["{name} hits → {card} | {cards} = {score}"]];
    public List<List<string>> PlayerBust        { get; set; } = [["{name} busts! {cards} = {score}"]];
    public List<List<string>> PlayerBJ          { get; set; } = [["{name} - Blackjack! {cards}"]];
    // Shown in the old PlayerBJ position (player-turns phase) when multiple players are at the table.
    // {name} {cards}
    public List<List<string>> PlayerBJMovingAlong { get; set; } = [["{name} has a blackjack so we'll just move along ♥"]];
    public List<List<string>> PlayerStand       { get; set; } = [["{name} stands. {cards} = {score}"]];

    // Double down - sent when the card lands and the hand is auto-stood
    // {name} may include "(Hand N)" for split hands
    public List<List<string>> PlayerDouble { get; set; } = [["{name} doubles down → {card} | {cards} = {score}"]];

    // Split ace mandatory card - card dealt, hand auto-stood per split-ace rule
    public List<List<string>> PlayerSplitAce { get; set; } = [["{name} draws {card} - {cards} = {score} (split aces, auto-stand)"]];

    // Five Card Charlie - {name} {card} {cards} {score}
    public List<List<string>> PlayerCharlie { get; set; } = [["{name} - Five Card Charlie! {card} | {cards} = {score}"]];

    // Bet-collection request (sent when dealer shift+clicks Trade during Betting)
    // {name} = player display name
    public List<List<string>> PlayerBetRequest { get; set; } = [["{name}, please trade your bet to me."]];

    // Bet-confirm announcement (sent when dealer clicks Confirm in the Bet cell during Betting)
    // {name} = player display name, {amount} = bet amount
    public List<List<string>> PlayerBetConfirm { get; set; } = [["{name}, your current bet is {amount}. If you want to change it let me know."]];

    // Bet-confirm with bank (sent when player has a bank balance)
    // {name} = player display name, {amount} = bet amount, {bank} = current bank balance, {bank-after-bet} = bank after bet deduction
    public List<List<string>> PlayerBetConfirmBank { get; set; } = [["{name}, your current bet is {amount} and your bank is {bank}. Afterwards your bank would be {bank-after-bet}."]];

    // Bank remind - sent from Bank column Remind button
    // {name} = player display name, {amount} = bet amount, {bank} = bank balance
    public List<List<string>> PlayerBankRemind { get; set; } = [["{name}, your bet is {amount} and your bank balance is {bank}."]];

    // Bank shortfall request - sent when dealer shift+clicks Deposit and bank < bet
    // {name} = player display name, {amount} = shortfall amount
    public List<List<string>> PlayerBankShortfall { get; set; } = [["{name}, please trade {amount} to cover your bet."]];

    // Bank deposit/withdraw log entries - {name} = player, {amount} = changed amount, {bank} = new balance
    public List<List<string>> PlayerBankDeposit  { get; set; } = [["{name} deposited {amount}. Bank: {bank}."]];
    public List<List<string>> PlayerBankWithdraw { get; set; } = [["{name} withdrew {amount}. Bank: {bank}."]];

    // Trade-request announcements (sent when dealer clicks Double/Split, before confirming trade)
    // {amount} = the extra chips required
    public List<List<string>> PlayerDoubleRequest     { get; set; } = [["{name} would like to double down! Please trade {amount} gil to the dealer."]];
    public List<List<string>> PlayerDoubleRequestBank { get; set; } = [["{name} is doubling down - {amount} deducted from bank. ({bank} remaining.)"]];
    public List<List<string>> PlayerSplitRequest      { get; set; } = [["{name} would like to split! Please trade {amount} gil to the dealer."]];
    public List<List<string>> PlayerSplitRequestBank  { get; set; } = [["{name} is splitting - {amount} deducted from bank. ({bank} remaining.)"]];

    // Sent when dealer clicks Confirm Dbl (trade received, card about to be drawn)
    // {name} = player display name
    public List<List<string>> PlayerDoubleConfirm { get; set; } = [["Good luck, {name}!"]];

    // Sent when the split is confirmed (after trade)
    public List<List<string>> PlayerSplit { get; set; } = [["{name} splits into two hands!"]];

    // Sent before rolling the mandatory 2nd card for a split hand
    public List<List<string>> PlayerSplitRoll { get; set; } = [["Rolling 2nd card for {name}..."]];

    // Initial deal announcements (Deal phase)
    public List<List<string>> DealDealerCard { get; set; } = [["{dealer}'s Card:"]];
    public List<List<string>> DealPlayerHand { get; set; } = [["{name}'s Hand:"]];

    // Deal summary building blocks - concatenated into a single chat message, not narrated independently
    public string DealSummaryPrefix { get; set; } = "Deal - ";
    public string DealSummaryPlayer { get; set; } = "{name}: {cards} ({score}){bj}";
    public string DealSummaryDealer { get; set; } = " | {dealer} shows {cards}";

    // Payout
    public List<List<string>> PayoutHeader { get; set; } = [["Summary:"]];

    // Combined payout for split hands where all hands win - replaces per-hand lines
    // {name} = player name; {amount} = combined payout amount (e.g. "+300g")
    public List<List<string>> PayoutSplitCombined { get; set; } = [["{name}: Split wins {amount}"]];

    public List<List<string>> PayoutDealerBust   { get; set; } = [["{dealer} busts ({score})"]];
    public List<List<string>> PayoutDealerStands { get; set; } = [["{dealer} {score}"]];
    // {name} may include "(Hand N)"; {bet} = "100g" or ""; {amount} = "+150g" or "-100g" or ""
    public List<List<string>> PayoutWin        { get; set; } = [["{name}: Win (bet: {bet}) {amount}"]];
    public List<List<string>> PayoutBjWin      { get; set; } = [["{name}: Blackjack! (bet: {bet}) {amount}"]];
    public List<List<string>> PayoutCharlieWin { get; set; } = [["{name}: Five Card Charlie! (bet: {bet}) {amount}"]];
    public List<List<string>> PayoutLose       { get; set; } = [["{name}: Lose (bet: {bet}) {amount}"]];
    public List<List<string>> PayoutPush       { get; set; } = [["{name}: Push (bet: {bet})"]];

    // Newtonsoft.Json reuses existing List instances and appends to them.
    // Clear all lists before deserialization so defaults don't accumulate on reload.
    [OnDeserializing]
    internal void OnDeserializing(StreamingContext _)
    {
        foreach (var prop in GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(List<List<string>>)))
            prop.SetValue(this, new List<List<string>>());
    }

    public static string Fmt(string template, params (string Key, string Value)[] vars)
    {
        foreach (var (k, v) in vars)
            template = template.Replace("{" + k + "}", v);
        return template;
    }
}
