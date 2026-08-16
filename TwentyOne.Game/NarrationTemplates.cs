using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwentyOne.Game;

[Serializable]
public class NarrationTemplates
{
    // Betting phase open announcement
    public List<List<string>> BettingOpen { get; set; } =
    [
        [
            "<wait.3> Collecting Bets!~ <se.15>",
            "Place your bets! Min: 50,000 / Max: 250,000 - I'll initiate a trade with you, please be patient! ♥",
            "/wringhands",
        ],
    ];

    // Dealer card draws (DealerTurn phase)
    // {dealer} = dealer's name (configured in Settings)
    public List<List<string>> DealerTurnStart   { get; set; } = [["My turn!~ I've got {score} so far. <se.3>"]];
    public List<List<string>> DealerHitAnnounce { get; set; } =
    [
        ["Let's see what I get! ♥ <se.8>", "/battlestance <wait.2>"],
        ["{dealer} hits! ♥ <se.8>",         "/battlestance"],
        ["Wish me luck! ♥ <se.8>",          "/battlestance"],
        ["Here goes nothing!~ <se.8>",      "/battlestance"],
    ];
    // Used instead of DealerHitAnnounce when all players have blackjack and dealer is checking for BJ.
    public List<List<string>> DealerBJCheck { get; set; } = [["Let's see if I get lucky! ♥ <se.7>"]];
    public List<List<string>> DealerHit         { get; set; } = [["{dealer} draws [{card}] {cards} = {score}"]];
    public List<List<string>> DealerBust        { get; set; } =
    [
        ["/upset",                          "Oh no, I busted! ;-; <wait.1>", "{dealer} busts with a total of {score} - {cards} <se.11>"],
        ["/panic",                          "Noooo I busted! ;-; <wait.1>",  "{dealer}: {cards} = {score} <se.11>"],
        ["/huh <wait.1>",                   "Aw, I busted! <wait.1>",        "{dealer} busts on {score} - {cards} <se.11>"],
    ];
    public List<List<string>> DealerBJ          { get; set; } =
    [
        ["{dealer} draws {card} -> {cards}", "/joy", "That's a ★blackjack★ for me!!! <se.10>"],
    ];
    public List<List<string>> DealerStand       { get; set; } = [["{dealer} stands with {score}"]];

    // Player actions (PlayerTurns phase)
    // {actions} = e.g. "Hit or Stand" or "Hit or Stand, Double Down, Split"
    public List<List<string>> PlayerHitAnnounce { get; set; } =
    [
        ["♠Hit♠ <se.3>", "One card comin' your way {name} ♥", "/battlestance"],
        ["Here comes a card for {name}!~ ♥ <se.3>",            "/battlestance"],
        ["Card incoming for {name}! ♠ <se.3>",                 "/battlestance"],
        ["Drawing for {name}!~ <se.3>",                        "/battlestance"],
    ];
    // Sent both when a player's turn begins and after each hit - the two used to
    // be separate templates that differed only in whether the dealer's card was
    // mentioned. Worded to read naturally in both positions.
    public List<List<string>> PlayerTurnStart   { get; set; } = [["{name}: {cards} ({score}) - Dealer has {dealerScore}. {actions} <se.3>"]];
    public List<List<string>> PlayerBust        { get; set; } =
    [
        ["<wait.1> /huh",       "Oh no! You busted ;-; <wait.1>", "{name} busts with a total of {score} - {cards} <se.11>"],
        ["/upset <wait.1>",     "Sorry {name}, that's a bust! ;-;", "{name}: {cards} = {score} <se.11>"],
        ["/comfort <wait.1>",   "Aw {name}, busted on {score} ;-; <wait.1>", "{cards} <se.11>"],
        ["<wait.1> Oof!",       "/panic", "{name} busts! {cards} = {score} <se.11>"],
    ];
    public List<List<string>> PlayerBJ          { get; set; } =
    [
        ["{name} - Blackjack! {cards}", "/y LETS GO THAT'S A NATURAL ★BLACKJACK★ FOR {name}!!! SEND THEM A DOTE!", "/joy"],
    ];
    // Shown in the old PlayerBJ position (player-turns phase) when multiple players are at the table.
    // {name} {cards}
    public List<List<string>> PlayerBJMovingAlong { get; set; } = [["{name} has a blackjack so we'll just move along ♥ <se.7>"]];
    public List<List<string>> PlayerStand       { get; set; } = [["{name} stands. {cards} = {score}"]];
    // Surrender - sent when the player surrenders their initial 2-card hand (-0.5x bet).
    public List<List<string>> PlayerSurrender   { get; set; } =
    [
        ["{name} surrenders this hand ;-;", "/comfort", "Sorry, those cards just weren't it~ Half your bet returns! ♥ <se.10>"],
    ];

    // Withdraw - the dealer pulls a player out of the round mid-hand (cashing out
    // right after the deal, or gone AFK / disconnected). Bet is refunded in full.
    public List<List<string>> PlayerWithdraw { get; set; } =
        [["{name} steps out of this round - bet returned. ♥"]];

    // Double down - sent when the card lands and the hand is auto-stood
    // {name} may include "(Hand N)" for split hands
    public List<List<string>> PlayerDouble { get; set; } = [["{name} doubles down -> {card} | {cards} = {score} - forced to stand <se.8>"]];

    // Split ace mandatory card - card dealt, hand auto-stood per split-ace rule
    public List<List<string>> PlayerSplitAce { get; set; } = [["{name} draws {card} - {cards} = {score} (split aces, auto-stand)"]];

    // Five Card Charlie - {name} {card} {cards} {score}
    public List<List<string>> PlayerCharlie { get; set; } = [["{name} - Five Card Charlie! {card} | {cards} = {score} <se.7>"]];

    // Bet-collection request (sent when dealer shift+clicks Trade during Betting)
    // {name} = player display name
    public List<List<string>> PlayerBetRequest { get; set; } = [["{name}, please place your bet with me. <se.15>", "/wringhands"]];

    // Bet-confirm announcement (sent when dealer clicks Confirm in the Bet cell during Betting)
    // {name} = player display name, {amount} = bet amount
    public List<List<string>> PlayerBetConfirm { get; set; } = [["{name}, your current bet is {amount}. If you want to change it let me know. <se.15>"]];

    // Bet-confirm with bank (sent when player has a bank balance)
    // {name} = player display name, {amount} = bet amount, {bank} = current bank balance, {bank-after-bet} = bank after bet deduction
    public List<List<string>> PlayerBetConfirmBank { get; set; } = [["{name}, your current bet is {amount}. Your bank is {bank}. Let me know if you want to change it. <se.15>"]];

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
    public List<List<string>> PlayerDoubleRequest     { get; set; } = [["{name} would like to double down! Please place your bet with me ({amount}) <se.8>"]];
    public List<List<string>> PlayerDoubleRequestBank { get; set; } = [["{name} is doubling down - {amount} deducted from bank. ({bank} remaining) <se.8>"]];
    public List<List<string>> PlayerSplitRequest      { get; set; } = [["{name} would like to split! Please place your bet with me ({amount}) <se.8>"]];
    public List<List<string>> PlayerSplitRequestBank  { get; set; } = [["{name} is splitting - {amount} deducted from bank. ({bank} remaining) <se.8>"]];

    // Sent when dealer clicks Confirm Dbl (trade received, card about to be drawn)
    // {name} = player display name
    public List<List<string>> PlayerDoubleConfirm { get; set; } =
    [
        ["/rally <wait.2>", "/y {name} is Doubling Down! Send them a dote for luck! ♥", "{name}'s last card is... <wait.1>", "/battlestance <wait.4>"],
    ];

    // Sent when the split is confirmed (after trade)
    public List<List<string>> PlayerSplit { get; set; } = [["Okay, {name}'s split bet is in!"]];

    // Sent before rolling the mandatory 2nd card for a split hand
    public List<List<string>> PlayerSplitRoll { get; set; } = [["Let's draw your second card for {name}..."]];

    // Initial deal announcements (Deal phase)
    public List<List<string>> DealDealerCard { get; set; } =
    [
        ["Alright! Bets are in! Let's get this started!~ <wait.1>", "/vpose <wait.4>", "{dealer}'s draw! <se.4>"],
    ];
    public List<List<string>> DealPlayerHand { get; set; } =
    [
        ["<wait.1> Two lucky cards for {name}~~~ <se.4> <wait.1>", "/battlestance <wait.2>"],
    ];

    // Deal summary building blocks - concatenated into a single chat message, not narrated independently
    public string DealSummaryPrefix { get; set; } = "Deal - ";
    public string DealSummaryPlayer { get; set; } = "{name}: {cards} ({score}){bj}";
    public string DealSummaryDealer { get; set; } = " | {dealer} shows {cards}";

    // Payout
    public List<List<string>> PayoutHeader { get; set; } = [["Summary: <se.15> <wait.1>"]];

    // Combined payout for split hands where all hands win - replaces per-hand lines
    // {name} = player name; {amount} = combined payout amount (e.g. "+300g")
    public List<List<string>> PayoutSplitCombined { get; set; } = [["{name}: Split wins {amount} <se.7> <wait.1>"]];

    public List<List<string>> PayoutDealerBust   { get; set; } = [["{dealer} busts ({score}) <se.11> <wait.1>"]];
    public List<List<string>> PayoutDealerStands { get; set; } = [["{dealer} {score} <wait.1>"]];
    // {name} may include "(Hand N)"; {bet} = "100g" or ""; {amount} = "+150g" or "-100g" or ""
    public List<List<string>> PayoutWin        { get; set; } = [["{name}: Win - Bet: {bet} - Winnings: {amount} <se.7> <wait.1>"]];
    public List<List<string>> PayoutBjWin      { get; set; } = [["{name}: Blackjack! - Bet: {bet} - Winnings: {amount} <se.7> <wait.1>"]];
    public List<List<string>> PayoutCharlieWin { get; set; } = [["{name}: Five Card Charlie! - Bet: {bet} - Winnings: {amount} <se.7> <wait.1>"]];
    public List<List<string>> PayoutLose       { get; set; } = [["{name}: Lose - Bet: {bet} <se.11> <wait.1>"]];
    public List<List<string>> PayoutPush       { get; set; } = [["{name}: Push - Bet: {bet} <se.10> <wait.1>"]];
    public List<List<string>> PayoutSurrender  { get; set; } = [["{name}: Surrendered - Bet: {bet} - Returned: {amount} <se.10> <wait.1>"]];

    [JsonExtensionData] public Dictionary<string, JToken> ExtraData { get; set; } = new();

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
