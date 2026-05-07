using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public partial class MainWindow
{
    private static void DrawHandStateLabel(Hand hand)
    {
        switch (hand.State)
        {
            case HandState.Bust:
                ImGui.TextColored(GameColors.BustRed, "Bust"); break;
            case HandState.Blackjack:
                ImGui.TextColored(GameColors.BlackjackGold, "Blackjack"); break;
            case HandState.Stand:
                ImGui.TextColored(GameColors.StandGrey, "Stand"); break;
            case HandState.Playing:
                if (hand.Cards.Count > 0)
                    ImGui.TextColored(GameColors.PlayingGreen, "Playing");
                break;
        }
    }

    /// <summary>
    /// Renders a player-hand score cell: bust → red, 21 → gold, otherwise plain.
    /// Stood hands collapse the soft/hard pair to a single number; everything
    /// else uses ScoreString.
    /// </summary>
    private static void DrawScoreCell(IReadOnlyList<int> cards, HandState state)
    {
        if (cards.Count == 0) return;
        var val = GameEngine.HandValue(cards);
        var s   = state == HandState.Stand ? val.ToString() : GameEngine.ScoreString(cards);
        if      (val > 21)  ImGui.TextColored(GameColors.BustRed, s);
        else if (val == 21) ImGui.TextColored(GameColors.BlackjackGold, s);
        else                ImGui.Text(s);
    }

    private static (string Label, Vector4 Color) PayoutDisplay(GameState state, int playerIndex, int handIndex) =>
        GameEngine.GetPayoutResult(state, playerIndex, handIndex) switch
        {
            PayoutResult.Win        => ("Win",     GameColors.ProfitGreen),
            PayoutResult.BjWin      => ("BJ Win",  GameColors.BlackjackGold),
            PayoutResult.CharlieWin => ("Charlie", GameColors.ProfitGreen),
            PayoutResult.Lose       => ("Lose",    GameColors.BustRed),
            PayoutResult.Push       => ("Push",    GameColors.StandGrey),
            _                       => (string.Empty, default),
        };

    private static uint ToU32(Vector4 c) =>
        ((uint)(c.X * 255) & 0xFF) |
        (((uint)(c.Y * 255) & 0xFF) << 8) |
        (((uint)(c.Z * 255) & 0xFF) << 16) |
        (((uint)(c.W * 255) & 0xFF) << 24);

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw()
    {
        // Reset venue-memory banner when the territory changes.
        var currentTerritory = clientState.TerritoryType;
        if (currentTerritory != lastSeenTerritory)
        {
            lastSeenTerritory       = currentTerritory;
            venueMemoryDismissed    = false;
            sessionBannerDismissed  = false;
        }

        // Drain outgoing queue — hold a roll entry until the previous roll's response has arrived.
        var isPublicChannel = config.ChatChannel is "/say" or "/yell" or "/shout";
        var cooldownMs      = isPublicChannel ? config.PublicChatCooldownMs : config.PrivateChatCooldownMs;
        chatQueue.TryDrain(DateTime.UtcNow, cooldownMs, config.SlashCommandCooldownMs, blockedByPendingHit: pendingHit != null);

#if DEBUG
        // Fast-forward: fire the next scenario step as soon as the chat queue and pending state drain
        if (Scenario.FastForward && Scenario.ActiveScenario?.PeekNext() != null
            && chatQueue.Count == 0 && pendingHit == null && !deferredRoll.HasValue)
            ExecuteNextScenarioStep();
        if (Scenario.ActiveScenario?.PeekNext() == null) Scenario.FastForward = false;
#endif

        // Process deferred roll from OnChatMessage
        if (deferredRoll.HasValue)
        {
            var (isDealer, pi, hi, roll) = deferredRoll.Value;
            deferredRoll = null;
            Apply(isDealer ? new AddDealerCard(roll) : new AddPlayerCard(pi, hi, roll));
            // Advance auto-deal if more cards are needed
            if (Phase == GamePhase.Deal && autoDealQueue.TryDequeue(out var next))
            {
                if (next.IsFirstCard)
                {
                    if (config.AutoTargetEnabled)
                    {
                        var dp = config.GameState.Players[next.PlayerIndex];
                        if (dp.World.Length > 0)
                            Plugin.TargetPlayer(dp.FullName, dp.World);
                    }
                    Apply(new AnnouncePlayerDeal(next.PlayerIndex));
                }
                QueueHitRoll(next.IsDealer, next.PlayerIndex, next.HandIndex);
            }
        }

        var uiBusy = chatQueue.Count > 0 || pendingHit != null || deferredRoll.HasValue;

        void DrawBankManageButton(int playerIndex, float cellRight, ReadOnlySpan<char> idSuffix)
        {
            if (uiBusy) ImGui.EndDisabled();
            var mw = ImGui.CalcTextSize("Manage").X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SameLine();
            if (ImGui.GetCursorPosX() < cellRight - mw)
                ImGui.SetCursorPosX(cellRight - mw);
            if (ImGui.SmallButton($"Manage##{playerIndex}{idSuffix}"))
            {
                bankManagePlayerIndex = bankManagePlayerIndex == playerIndex ? -1 : playerIndex;
                bankDepositBuf        = string.Empty;
                bankWithdrawBuf       = string.Empty;
            }
            if (uiBusy) ImGui.BeginDisabled();
        }

        // ── Bank manage window ─────────────────────────────────────────────────
        if (bankManagePlayerIndex >= 0 && bankManagePlayerIndex < State.Players.Count)
        {
            var bankWinOpen = true;
            var bmp     = State.Players[bankManagePlayerIndex];
            var bmpKey  = bmp.StatsKey();
            ImGui.SetNextWindowSize(new Vector2(380, 480), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Bank##bankManage", ref bankWinOpen, ImGuiWindowFlags.NoCollapse))
            {
                if (!config.PlayerStatsStore.TryGetValue(bmpKey, out var bmpStat))
                {
                    bmpStat = new PlayerStat { DisplayName = bmp.DisplayName };
                    config.PlayerStatsStore[bmpKey] = bmpStat;
                }
                var bmpBank = bmpStat.Bank;

                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{bmp.DisplayName}");
                ImGui.SameLine();
                ImGui.TextDisabled($"Bank: {bmpBank:N0}");
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText(bmpBank.ToString());
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to copy");
                if (bmp.World.Length > 0)
                {
                    ImGui.SameLine();
                    if (uiBusy) ImGui.BeginDisabled();
                    if (ImGui.Button("Trade##bankmantradetop"))
                        Plugin.TradePlayer(bmp.FullName, bmp.World);
                    if (uiBusy) ImGui.EndDisabled();
                }
                ImGui.Separator();
                ImGui.Spacing();

                // Deposit
                ImGui.AlignTextToFramePadding(); ImGui.Text("Deposit"); ImGui.SameLine(80);
                ImGui.SetNextItemWidth(140);
                ImGui.InputText("##bankdep", ref bankDepositBuf, 20);
                ImGui.SameLine();
                var canDep2 = long.TryParse(bankDepositBuf, out var depAmt2) && depAmt2 > 0;
                if (!canDep2) ImGui.BeginDisabled();
                if (ImGui.Button("Confirm##bankdepconfirm"))
                {
                    ApplyBank(bmpStat, new BankDeposit(depAmt2));
                    Apply(new AnnounceBankDeposit(bankManagePlayerIndex, depAmt2, bmpStat.Bank));
                    bankDepositBuf = string.Empty;
                }
                if (!canDep2) ImGui.EndDisabled();

                ImGui.Spacing();

                // Withdraw
                ImGui.AlignTextToFramePadding(); ImGui.Text("Withdraw"); ImGui.SameLine(80);
                ImGui.SetNextItemWidth(140);
                ImGui.InputText("##bankwd", ref bankWithdrawBuf, 20);
                ImGui.SameLine();
                var canWd2 = long.TryParse(bankWithdrawBuf, out var wdAmt2) && wdAmt2 > 0 && wdAmt2 <= bmpBank;
                if (!canWd2) ImGui.BeginDisabled();
                if (ImGui.Button("Confirm##bankwdconfirm"))
                {
                    ApplyBank(bmpStat, new BankWithdrawal(wdAmt2));
                    Apply(new AnnounceBankWithdraw(bankManagePlayerIndex, wdAmt2, bmpStat.Bank));
                    bankWithdrawBuf = string.Empty;
                }
                if (!canWd2) ImGui.EndDisabled();

                ImGui.Spacing();

                // Remind (bank > 0 + bet set)
                var bmpBetForRemind = betEdits.TryGetValue(bankManagePlayerIndex, out var bmpPending) ? bmpPending : bmp.Bet;
                if (bmpBank > 0 && !string.IsNullOrWhiteSpace(bmpBetForRemind))
                {
                    if (uiBusy) ImGui.BeginDisabled();
                    if (ImGui.Button("Remind##bankremind"))
                    {
                        if (betEdits.TryGetValue(bankManagePlayerIndex, out var pendingBet) && pendingBet != bmp.Bet)
                        {
                            betEdits.Remove(bankManagePlayerIndex);
                            Apply(new SetPlayerBet(bankManagePlayerIndex, pendingBet));
                        }
                        Apply(new AnnounceBankRemind(bankManagePlayerIndex, bmpBank));
                    }
                    if (uiBusy) ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remind player of their bet and bank balance");
                }

                ImGui.Spacing();

                // Clear all
                var ctrlDown = ImGui.GetIO().KeyCtrl;
                if (!ctrlDown) ImGui.BeginDisabled();
                if (ImGui.Button("Clear All##bankClear"))
                {
                    bmpStat.Bank = 0;
                    bmpStat.BankLog.Clear();
                    config.Save();
                }
                if (!ctrlDown) ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Hold Ctrl to clear balance and transaction history");

                ImGui.Spacing();

                // Transaction history
                ImGui.Separator();
                ImGui.Text("History");
                var log = bmpStat.BankLog;
                var tableH = ImGui.GetContentRegionAvail().Y;
                if (ImGui.BeginTable("##banklog", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
                    new Vector2(0, tableH)))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Time",    ImGuiTableColumnFlags.None, 60);
                    ImGui.TableSetupColumn("Type",    ImGuiTableColumnFlags.None, 80);
                    ImGui.TableSetupColumn("Amount",  ImGuiTableColumnFlags.None, 80);
                    ImGui.TableSetupColumn("Balance", ImGuiTableColumnFlags.None, 80);
                    ImGui.TableHeadersRow();

                    for (var li = log.Count - 1; li >= 0; li--)
                    {
                        var entry = log[li];
                        var isCredit = entry.Kind is BankTransactionKind.Deposit or BankTransactionKind.Win;
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(entry.Timestamp.ToString("HH:mm"));
                        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(entry.Kind switch
                        {
                            BankTransactionKind.Deposit    => "Deposit",
                            BankTransactionKind.Withdrawal => "Withdraw",
                            BankTransactionKind.Bet        => "Bet",
                            BankTransactionKind.Win        => "Win",
                            BankTransactionKind.DoubleDown => "Double",
                            BankTransactionKind.Split      => "Split",
                            _                              => "?"
                        });
                        ImGui.TableSetColumnIndex(2);
                        if (isCredit) ImGui.TextColored(GameColors.CreditGreen, $"+{entry.Amount:N0}");
                        else          ImGui.TextColored(GameColors.DebitRed, $"-{entry.Amount:N0}");
                        ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted($"{entry.Balance:N0}");
                    }
                    ImGui.EndTable();
                }

            }
            ImGui.End();
            if (!bankWinOpen)
            {
                bankManagePlayerIndex = -1;
                bankDepositBuf        = string.Empty;
                bankWithdrawBuf       = string.Empty;
            }
        }

        // ── Trade bet prompt modal ─────────────────────────────────────────────
        if (pendingBetPrompt.HasValue)
            ImGui.OpenPopup("Set bet from trade?##tradeBet");
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, GameColors.TransparentDimBg);
        var showBetModal = ImGui.BeginPopupModal("Set bet from trade?##tradeBet", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        if (showBetModal)
        {
            var (bpi, bgil) = pendingBetPrompt!.Value;
            var bplayer     = State.Players[bpi];
            ImGui.Text($"Set {bplayer.DisplayName}'s bet to {bgil:N0} gil?");
            ImGui.Spacing();
            if (ImGui.Button("Yes"))
            {
                betEdits.Remove(bpi);
                Apply(new SetPlayerBet(bpi, bgil.ToString()));
                pendingBetPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No"))
            {
                pendingBetPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // ── Bet-or-bank trade prompt modal (both options on, Betting phase) ──────
        if (pendingBetOrBankPrompt.HasValue)
            ImGui.OpenPopup("Trade received##betOrBank");
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, GameColors.TransparentDimBg);
        var showBetOrBankModal = ImGui.BeginPopupModal("Trade received##betOrBank", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        if (showBetOrBankModal)
        {
            var (bobpi, bobgil) = pendingBetOrBankPrompt!.Value;
            var bobPlayer = State.Players[bobpi];
            ImGui.Text($"Received {bobgil:N0} gil from {bobPlayer.DisplayName}.");
            ImGui.Spacing();
            if (ImGui.Button("Set as bet##bobBet"))
            {
                betEdits.Remove(bobpi);
                Apply(new SetPlayerBet(bobpi, bobgil.ToString()));
                pendingBetOrBankPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Bank deposit##bobBank"))
            {
                var bobKey = bobPlayer.StatsKey();
                if (!config.PlayerStatsStore.TryGetValue(bobKey, out var bobStat))
                {
                    bobStat = new PlayerStat { DisplayName = bobPlayer.DisplayName };
                    config.PlayerStatsStore[bobKey] = bobStat;
                }
                ApplyBank(bobStat, new BankDeposit(bobgil));
                Apply(new AnnounceBankDeposit(bobpi, bobgil, bobStat.Bank));
                pendingBetOrBankPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Ignore##bobIgnore"))
            {
                pendingBetOrBankPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // ── Bank trade prompt modal ────────────────────────────────────────────
        if (pendingBankTradePrompt.HasValue)
            ImGui.OpenPopup("Bank trade##bankTradePrompt");
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, GameColors.TransparentDimBg);
        var showBankModal = ImGui.BeginPopupModal("Bank trade##bankTradePrompt", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        if (showBankModal)
        {
            var (btpi, btamt, btwd) = pendingBankTradePrompt!.Value;
            var btplayer = State.Players[btpi];
            var btKey    = btplayer.StatsKey();
            if (!config.PlayerStatsStore.TryGetValue(btKey, out var btStat))
            {
                btStat = new PlayerStat { DisplayName = btplayer.DisplayName };
                config.PlayerStatsStore[btKey] = btStat;
            }
            var verb = btwd ? "Withdraw" : "Deposit";
            ImGui.Text($"{verb} {btamt:N0} gil {(btwd ? "from" : "to")} {btplayer.DisplayName}'s bank?");
            ImGui.Spacing();
            if (ImGui.Button("Yes##bankTradeYes"))
            {
                ApplyBank(btStat, btwd ? new BankWithdrawal(btamt) : new BankDeposit(btamt));
                Apply(btwd
                    ? new AnnounceBankWithdraw(btpi, btamt, btStat.Bank)
                    : new AnnounceBankDeposit (btpi, btamt, btStat.Bank));
                pendingBankTradePrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No##bankTradeNo"))
            {
                pendingBankTradePrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (isHistoryView)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GameColors.BannerGold);
            ImGui.TextUnformatted("Viewing previous round");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton("Exit History View"))
                ExitHistoryView();
            ImGui.Separator();
        }
#if DEBUG
        if (Scenario.ActiveScenario is { } activeScenario)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GameColors.ActiveOrange);
            var nextStep = activeScenario.PeekNext() ?? "(done)";
            ImGui.TextUnformatted($"[SCENARIO] {activeScenario.Name}  |  Next: {nextStep}  ({activeScenario.Remaining} left)");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton("Abort##scenBannerAbort"))
            {
                Scenario.ActiveScenario = null;
                Scenario.RollQueue.Clear();
            }
            ImGui.Separator();
        }
#endif

        if (!venueMemoryDismissed && !isHistoryView && GetVenueMemorySuggestion() is var suggestion && suggestion.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GameColors.ModalTitleBlue);
            ImGui.TextUnformatted($"The last time you were here you used \"{suggestion.Value.Name}\". Switch to it?");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton("Yes##venueMemoryYes"))
            {
                config.ActiveVenueIndex = suggestion.Value.Index;
                sessionLedgerWindow.SyncBuffers();
                config.Save();
                venueMemoryDismissed = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("X##venueMemoryDismiss"))
                venueMemoryDismissed = true;
            ImGui.Separator();
        }

        if (!sessionBannerDismissed && !isHistoryView)
        {
            var venue = config.ActiveVenue;
            var addrKey = Plugin.GetCurrentHousingAddressKey();
            if (SessionManager.ShouldShowSessionBanner(
                    venue.ActiveSessionStartedAt,
                    venue.ActiveSessionLocationKey,
                    addrKey,
                    venue.RoundHistory.Count,
                    DateTime.Now))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, GameColors.BannerGold);
                ImGui.TextUnformatted("It's been a while (or you're at a new location), want to mark a new session in the ledger?");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                var venueNames = config.Venues.ConvertAll(v => v.Name).ToArray();
                var vIdx = config.ActiveVenueIndex;
                var roundInProgress = config.GameState.IsRoundActive();
                ImGui.SetNextItemWidth(140);
                if (roundInProgress) ImGui.BeginDisabled();
                if (ImGui.Combo("##sessionVenueCombo", ref vIdx, venueNames, venueNames.Length)
                    && vIdx != config.ActiveVenueIndex)
                {
                    if (addrKey != null)
                        config.VenueMemory[addrKey] = config.Venues[vIdx].Id.ToString();
                    config.ActiveVenueIndex = vIdx;
                    sessionLedgerWindow.SyncBuffers();
                    config.Save();
                }
                if (roundInProgress) ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.SmallButton("New Session##sessionBannerStart"))
                {
                    sessionLedgerWindow.NewSession();
                    sessionBannerDismissed = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("X##sessionBannerDismiss"))
                    sessionBannerDismissed = true;
                ImGui.Separator();
            }
        }

        if (ImGui.SmallButton("Config"))
            configWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("Session Ledger"))
            sessionLedgerWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("History"))
            historyWindow.Toggle();
#if DEBUG
        ImGui.SameLine();
        if (ImGui.SmallButton("Debug"))
            debugWindow.Toggle();
#endif

        var canUndo = config.UndoStack.Count > 0;
        var canRedo = config.RedoStack.Count > 0;
        var undoW   = ImGui.CalcTextSize("Undo").X + ImGui.GetStyle().FramePadding.X * 2;
        var redoW   = ImGui.CalcTextSize("Redo").X + ImGui.GetStyle().FramePadding.X * 2;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetWindowWidth() - undoW - redoW - spacing * 2
                       - ImGui.GetStyle().WindowPadding.X);
        if (!canUndo) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Undo")) Undo();
        if (!canUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!canRedo) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Redo")) Redo();
        if (!canRedo) ImGui.EndDisabled();

        if (uiBusy) ImGui.BeginDisabled();

        ImGui.Separator();

        var dealerHitActive = GameEngine.CanHitDealer(State);

        // ── Dealer section ────────────────────────────────────────────────────
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        if (State.DealerHand.Cards.Count > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(GameEngine.HandString(State.DealerHand.Cards));
            ImGui.SameLine();

            var val      = GameEngine.HandValue(State.DealerHand.Cards);
            var scoreStr = GameEngine.ScoreString(State.DealerHand.Cards);
            if (val > 21)
                ImGui.TextColored(GameColors.BustRed, $"= {scoreStr}  BUST");
            else if (val == 21 && State.DealerHand.Cards.Count == 2)
                ImGui.TextColored(GameColors.BlackjackGold, $"= {scoreStr}  Blackjack");
            else
            {
                ImGui.Text($"= {scoreStr}");
                var rec = GameEngine.DealerRecommendation(State.DealerHand);
                var allBust = State.IsAllBust();
                if (rec.Length > 0 && Phase == GamePhase.DealerTurn && !allBust)
                {
                    ImGui.SameLine();
                    var rc = rec == "HIT"
                        ? GameColors.PlayingGreen
                        : GameColors.DisabledGrey;
                    ImGui.TextColored(rc, $"→ {rec}");
                }
            }
        }

        if (dealerHitActive)
        {
            if (State.DealerHand.Cards.Count > 0) ImGui.SameLine();
#if DEBUG
            var _scenDHit = Scenario.IsStep("DealerHit");
            if (!_scenDHit) ImGui.BeginDisabled();
#endif
            if (ImGui.SmallButton("Hit##dealer"))
            {
#if DEBUG
                Scenario.Advance();
#endif
                Apply(new AnnounceDealerHit());
                QueueHitRoll(isDealer: true, -1, -1);
            }
#if DEBUG
            if (!_scenDHit) ImGui.EndDisabled();
#endif
        }

        // ── Player table ──────────────────────────────────────────────────────
        ImGui.AlignTextToFramePadding();
        ImGui.Text("-- Players --");
        if (Phase == GamePhase.Betting)
        {
            if (isReorderMode)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Confirm"))
                {
                    Apply(new ReorderPlayers(reorderIndices));
                    isReorderMode = false;
                    reorderIndices = [];
                }
            }
            else if (State.ActivePlayerCount() > 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Reorder"))
                {
                    foreach (var (idx, val) in betEdits.ToList())
                    {
                        betEdits.Remove(idx);
                        if (val != State.Players[idx].Bet)
                            Apply(new SetPlayerBet(idx, val));
                    }
                    isReorderMode  = true;
                    reorderIndices = Enumerable.Range(0, State.Players.Count).ToList();
                }
            }
        }
        else if (isReorderMode)
        {
            isReorderMode  = false;
            reorderIndices = [];
        }
        ImGui.Separator();

        void DrawBankCell(int loopIdx, int actualIdx, Player player, float bankCellRight)
        {
            var bankKey = player.StatsKey();
            if (!config.PlayerStatsStore.TryGetValue(bankKey, out var bankStat))
            {
                bankStat = new PlayerStat { DisplayName = player.DisplayName };
                config.PlayerStatsStore[bankKey] = bankStat;
            }
            var bankVal         = bankStat.Bank;
            var effectiveBetStr = betEdits.TryGetValue(loopIdx, out var bEdit) ? bEdit : player.Bet;
            var parsedBet       = GameEngine.ParseBet(effectiveBetStr);
            var shortfall       = parsedBet > 0 ? Math.Max(0m, parsedBet - bankVal) : 0m;

            var bankDelta = 0m;
            if (Phase == GamePhase.Payout && bankVal > 0)
                for (var bhi = 0; bhi < player.Hands.Count; bhi++)
                {
                    var d = GameEngine.PayoutDelta(State, actualIdx, bhi);
                    if (d > 0) bankDelta += d.Value;
                }

            ImGui.AlignTextToFramePadding();
            if (bankStat.IsBanking())
            {
                var bankLabel = GameEngine.FormatGil(bankVal);
                if (shortfall > 0)
                    ImGui.TextColored(GameColors.WarningAmber, bankLabel);
                else
                    ImGui.TextUnformatted(bankLabel);
                if (ImGui.IsItemHovered())
                {
                    var tip = new System.Text.StringBuilder();
                    if (shortfall > 0)
                        tip.AppendLine($"Short by {GameEngine.FormatGil(shortfall)} — needs trade before deal");
                    if (Phase == GamePhase.Payout && bankDelta != 0)
                    {
                        var deltaStr = bankDelta > 0 ? $"+{GameEngine.FormatGil(bankDelta)}" : GameEngine.FormatGil(bankDelta);
                        tip.AppendLine($"This round: {deltaStr}");
                        tip.AppendLine($"After settlement: {GameEngine.FormatGil(Math.Max(0, bankVal + bankDelta))}");
                    }
                    tip.Append("Click to copy");
                    ImGui.SetTooltip(tip.ToString());
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        ImGui.SetClipboardText(bankVal.ToString());
                }

                if (Phase == GamePhase.Betting && shortfall > 0)
                {
                    var amber    = new Vector4(1f, 0.75f, 0.1f, 1f);
                    var fp       = ImGui.GetStyle().FramePadding.X;
                    var sp       = ImGui.GetStyle().ItemSpacing.X;
                    var manageW  = ImGui.CalcTextSize("Manage").X + fp * 2;
                    var shortW   = ImGui.CalcTextSize("Short").X  + fp * 2;
                    ImGui.SameLine();
                    if (ImGui.GetCursorPosX() < bankCellRight - manageW - sp - shortW)
                        ImGui.SetCursorPosX(bankCellRight - manageW - sp - shortW);
                    var amberHov = new Vector4(1f, 0.88f, 0.3f, 1f);
                    ImGui.PushStyleColor(ImGuiCol.Button,        amber    with { W = 0.25f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, amberHov with { W = 0.4f  });
                    ImGui.PushStyleColor(ImGuiCol.Text,          amber);
                    if (ImGui.SmallButton($"Short##{actualIdx}short"))
                    {
                        if (betEdits.TryGetValue(loopIdx, out var pendingBet) && pendingBet != player.Bet)
                        {
                            betEdits.Remove(loopIdx);
                            Apply(new SetPlayerBet(actualIdx, pendingBet));
                        }
                        Apply(new AnnounceBankShortfall(actualIdx, (long)Math.Ceiling(shortfall)));
                    }
                    ImGui.PopStyleColor(3);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Short by {GameEngine.FormatGil(shortfall)}\nClick to announce shortfall");
                }
            }
            else
            {
                ImGui.TextDisabled("—");
            }

            DrawBankManageButton(actualIdx, bankCellRight, "bank");
        }

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        var tableAvailWidth = ImGui.GetContentRegionAvail().X;
        int removeAt = -1;
        if (ImGui.BeginTable("##players"u8, 7, tableFlags))
        {
            ImGui.TableSetupColumn("Name"u8,      ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Bet"u8,       ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Bank"u8,      ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupColumn("Cards"u8,     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Score"u8,     ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Status"u8,    ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableHeadersRow();

            (int A, int B)? reorderSwap = null;
            for (var pi = 0; pi < State.Players.Count; pi++)
            {
                var displayPi = isReorderMode ? reorderIndices[pi] : pi;
                var p         = State.Players[displayPi];
                if (p.SittingOut) continue;
                var hasWorld    = p.World.Length > 0;
                var hasNickname = p.Nickname.Length > 0;
                var multiHand = p.Hands.Count > 1;

                // ── Summary row for split players ─────────────────────────────
                if (multiHand)
                {
                    ImGui.TableNextRow();

                    // Name
                    ImGui.TableSetColumnIndex(0);
                    var sumNameCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (renamingIndex == pi)
                    {
                        var okW = ImGui.CalcTextSize("OK").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - okW);
                        var submitted = ImGui.InputText($"##rename{pi}", ref renamingBuffer, 64,
                            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                        ImGui.SameLine();
                        var canConfirm = renamingBuffer.Length > 0 || p.World.Length > 0;
                        if (!canConfirm) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"OK##{pi}ok") || submitted)
                        {
                            if (canConfirm) Apply(new RenamePlayer(pi, renamingBuffer));
                            renamingIndex = -1;
                        }
                        if (!canConfirm) ImGui.EndDisabled();
                    }
                    else
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(p.DisplayName);
                        if (ImGui.IsItemHovered())
                        {
                            if (p.World.Length > 0)
                                ImGui.SetTooltip($"{p.FullName}@{p.World}");
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                renamingIndex  = pi;
                                renamingBuffer = p.Nickname;
                            }
                        }

                        var winnerKey = p.FullName.Length > 0 ? p.FullName : p.Nickname;
                        var isWinner  = config.GameState.LastRoundWinners.Contains(winnerKey);
                        var isPusher  = !isWinner && config.GameState.LastRoundPushers.Contains(winnerKey);
                        var sp        = ImGui.GetStyle().ItemSpacing.X;
                        var fp        = ImGui.GetStyle().FramePadding.X;
                        float SBW(string s) => ImGui.CalcTextSize(s).X + fp * 2;
                        var clearW  = hasWorld && hasNickname ? SBW("C") + sp : 0;
                        var targetW = hasWorld               ? SBW("@") + sp : 0;
                        var renameW = SBW("R");
                        var spadeW  = (isWinner || isPusher) ? ImGui.CalcTextSize("♠").X + sp : 0;
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(sumNameCellRight - spadeW - targetW - renameW - clearW);

                        if (isWinner)
                        {
                            ImGui.TextColored(new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f), "♠");
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Won last round"u8);
                            ImGui.SameLine();
                        }
                        else if (isPusher)
                        {
                            ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 1f, 1f), "♠");
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pushed last round"u8);
                            ImGui.SameLine();
                        }

                        if (hasWorld)
                        {
                            if (ImGui.SmallButton($"@##{pi}target"))
                                Plugin.TargetPlayer(p.FullName, p.World);
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Target {p.FullName}@{p.World}");
                            ImGui.SameLine();
                        }

                        if (ImGui.SmallButton($"R##{pi}rename"))
                        {
                            renamingIndex  = pi;
                            renamingBuffer = p.Nickname;
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rename"u8);

                        if (hasWorld && hasNickname)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"C##{pi}clear"))
                                Apply(new RenamePlayer(pi, ""));
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear nickname"u8);
                        }
                    }

                    // Bet (total of all hands)
                    ImGui.TableSetColumnIndex(1);
                    var sumBetCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    var totalHandBets = p.Hands.Sum(h => GameEngine.GetEffectiveBet(p, h));
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(totalHandBets > 0 ? GameEngine.FormatGil(totalHandBets) : p.Bet);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Click to copy total bet");
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                            ImGui.SetClipboardText(totalHandBets > 0 ? $"{totalHandBets:0.##}" : p.Bet);
                    }
                    if (hasWorld)
                    {
                        var sumTradeW = ImGui.CalcTextSize("Trade").X + ImGui.GetStyle().FramePadding.X * 2;
                        ImGui.SameLine();
                        if (ImGui.GetCursorPosX() < sumBetCellRight - sumTradeW)
                            ImGui.SetCursorPosX(sumBetCellRight - sumTradeW);
                        if (ImGui.SmallButton($"Trade##{pi}sumtrade"))
                            Plugin.TradePlayer(p.FullName, p.World);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip($"Trade {p.FullName}@{p.World}");
                    }

                    // Bank
                    ImGui.TableSetColumnIndex(2);
                    DrawBankCell(pi, displayPi, p, ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);

                    // Status (net payout summary or blank)
                    ImGui.TableSetColumnIndex(5);
                    if (Phase == GamePhase.Payout)
                    {
                        var green = GameColors.ProfitGreen;
                        var red   = GameColors.BustRed;
                        var grey  = GameColors.StandGrey;
                        var sumTotalOwed = 0m;
                        var sumNetDelta  = 0m;
                        for (var hh = 0; hh < p.Hands.Count; hh++)
                        {
                            var result = GameEngine.GetPayoutResult(State, pi, hh);
                            var eb     = GameEngine.GetEffectiveBet(p, p.Hands[hh]);
                            var d      = GameEngine.PayoutDelta(State, pi, hh) ?? 0m;
                            sumNetDelta  += d;
                            sumTotalOwed += result switch
                            {
                                PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin => eb + d,
                                PayoutResult.Push                                                  => eb,
                                _                                                                  => 0m,
                            };
                        }

                        var sumCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                        string sumLabel;
                        Vector4 sumColor;
                        if (sumNetDelta > 0)      (sumLabel, sumColor) = ($"Net: +{GameEngine.FormatGil(sumNetDelta)}", green);
                        else if (sumNetDelta < 0) (sumLabel, sumColor) = ($"Net: {GameEngine.FormatGil(sumNetDelta)}",  red);
                        else                      (sumLabel, sumColor) = ("Net: Even",                                   grey);

                        ImGui.TextColored(sumColor, sumLabel);

                        if (sumTotalOwed > 0)
                        {
                            var ctrlHeld   = ImGui.GetIO().KeyCtrl;
                            var initBet    = GameEngine.ParseBet(p.Bet);
                            var keepBetVal = sumTotalOwed - initBet;
                            var copyVal    = ctrlHeld ? $"{keepBetVal:0.##}" : $"{sumTotalOwed:0.##}";
                            var copyW      = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(sumCellRight - copyW + ImGui.GetStyle().ItemSpacing.X);
                            if (ImGui.SmallButton($"Copy##{pi}payout"))
                                ImGui.SetClipboardText(copyVal);
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(ctrlHeld
                                    ? $"Copy (keep initial bet): {keepBetVal:0.##}"
                                    : $"Copy total owed: {sumTotalOwed:0.##}\nCtrl+Click to copy minus initial bet: {keepBetVal:0.##}");
                        }
                    }
                }

                for (var hi = 0; hi < p.Hands.Count; hi++)
                {
                    var hand        = p.Hands[hi];
                    var isFirstHand = hi == 0;
                    var isActiveHand = Phase == GamePhase.PlayerTurns
                                    && pi == ActivePlayerIndex && hi == ActiveHandIndex;

                    ImGui.TableNextRow();
                    if (isActiveHand)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1,
                            ToU32(new Vector4(0.25f, 0.45f, 0.75f, 0.35f)));

                    // ── Name column ───────────────────────────────────────────
                    ImGui.TableSetColumnIndex(0);
                    var nameCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (isReorderMode && isFirstHand && !multiHand)
                    {
                        if (pi == 0) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"↑##{pi}reorderUp")) reorderSwap = (pi, pi - 1);
                        if (pi == 0) ImGui.EndDisabled();
                        ImGui.SameLine();
                        if (pi == State.Players.Count - 1) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"↓##{pi}reorderDown")) reorderSwap = (pi, pi + 1);
                        if (pi == State.Players.Count - 1) ImGui.EndDisabled();
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(p.DisplayName);
                    }
                    else if (isFirstHand && !multiHand)
                    {
                        if (renamingIndex == pi)
                        {
                            var okW = ImGui.CalcTextSize("OK").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - okW);
                            var submitted = ImGui.InputText($"##rename{pi}", ref renamingBuffer, 64,
                                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                            ImGui.SameLine();
                            var canConfirm = renamingBuffer.Length > 0 || p.World.Length > 0;
                            if (!canConfirm) ImGui.BeginDisabled();
                            if (ImGui.SmallButton($"OK##{pi}ok") || submitted)
                            {
                                if (canConfirm) Apply(new RenamePlayer(pi, renamingBuffer));
                                renamingIndex = -1;
                            }
                            if (!canConfirm) ImGui.EndDisabled();
                        }
                        else
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(p.DisplayName);
                            if (ImGui.IsItemHovered())
                            {
                                if (p.World.Length > 0)
                                    ImGui.SetTooltip($"{p.FullName}@{p.World}");
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                                {
                                    renamingIndex  = pi;
                                    renamingBuffer = p.Nickname;
                                }
                            }

                            var winnerKey = p.FullName.Length > 0 ? p.FullName : p.Nickname;
                            var isWinner  = config.GameState.LastRoundWinners.Contains(winnerKey);
                            var isPusher  = !isWinner && config.GameState.LastRoundPushers.Contains(winnerKey);
                            var sp      = ImGui.GetStyle().ItemSpacing.X;
                            var fp      = ImGui.GetStyle().FramePadding.X;
                            float BW(string s) => ImGui.CalcTextSize(s).X + fp * 2;
                            var clearW  = hasWorld && hasNickname ? BW("C") + sp : 0;
                            var targetW = hasWorld               ? BW("@") + sp : 0;
                            var renameW = BW("R");
                            var spadeW  = (isWinner || isPusher) ? ImGui.CalcTextSize("\u2660").X + sp : 0;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(nameCellRight - spadeW - targetW - renameW - clearW);

                            if (isWinner)
                            {
                                ImGui.TextColored(new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f), "\u2660");
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Won last round"u8);
                                ImGui.SameLine();
                            }
                            else if (isPusher)
                            {
                                ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 1f, 1f), "\u2660");
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pushed last round"u8);
                                ImGui.SameLine();
                            }

                            if (hasWorld)
                            {
                                if (ImGui.SmallButton($"@##{pi}target"))
                                    Plugin.TargetPlayer(p.FullName, p.World);
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Target {p.FullName}@{p.World}");
                                ImGui.SameLine();
                            }

                            if (ImGui.SmallButton($"R##{pi}rename"))
                            {
                                renamingIndex  = pi;
                                renamingBuffer = p.Nickname;
                            }
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rename"u8);

                            if (hasWorld && hasNickname)
                            {
                                ImGui.SameLine();
                                if (ImGui.SmallButton($"C##{pi}clear"))
                                    Apply(new RenamePlayer(pi, ""));
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear nickname"u8);
                            }
                        }
                    }
                    else
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextDisabled($"--> Hand {hi + 1}");
                    }

                    // ── Bet column ────────────────────────────────────────────
                    ImGui.TableSetColumnIndex(1);
                    if (isFirstHand && !multiHand)
                    {
                        var betCellRight   = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                        var confirmButtonW = Phase == GamePhase.Betting
                            ? ImGui.CalcTextSize("Confirm").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X
                            : 0;
                        var tradeButtonW = hasWorld
                            ? ImGui.CalcTextSize("Trade").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X
                            : 0;
                        if (Phase != GamePhase.Betting || isReorderMode)
                        {
                            var eb        = GameEngine.GetEffectiveBet(p, hand);
                            var betLabel  = eb > 0 ? GameEngine.FormatGil(eb) : p.Bet;
                            var betCopy   = eb > 0 ? $"{eb:0.##}" : p.Bet;
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextDisabled(betLabel);
                            if (!isReorderMode && ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip("Click to copy bet");
                                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                    ImGui.SetClipboardText(betCopy);
                            }
                        }
                        else
                        {
                            ImGui.SetNextItemWidth(betCellRight - ImGui.GetCursorPosX() - tradeButtonW - confirmButtonW);
                            var betVal = betEdits.TryGetValue(pi, out var e) ? e : p.Bet;
                            if (p.SittingOut) ImGui.BeginDisabled();
                            if (ImGui.InputText($"##bet{pi}", ref betVal, 16, ImGuiInputTextFlags.EnterReturnsTrue))
                            {
                                betEdits.Remove(pi);
                                Apply(new SetPlayerBet(pi, betVal));
                            }
                            else
                            {
                                betEdits[pi] = betVal;
                            }
                            if (p.SittingOut) ImGui.EndDisabled();
                        }
                        if (hasWorld)
                        {
                            var tradeOnlyW = ImGui.CalcTextSize("Trade").X + ImGui.GetStyle().FramePadding.X * 2;
                            var tradePosX  = betCellRight - tradeOnlyW - confirmButtonW;
                            ImGui.SameLine();
                            if (ImGui.GetCursorPosX() < tradePosX)
                                ImGui.SetCursorPosX(tradePosX);
                            if (ImGui.SmallButton($"Trade##{pi}trade"))
                            {
                                if (ImGui.GetIO().KeyShift)
                                {
                                    Apply(new AnnounceBetRequest(pi));
                                    if (hasWorld)
                                        Plugin.TargetPlayer(p.FullName, p.World);
                                    QueueTrade(p.FullName, p.World, config.PrivateChatCooldownMs);
                                }
                                else
                                    Plugin.TradePlayer(p.FullName, p.World);
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip($"Trade {p.FullName}@{p.World}\nShift+Click to announce bet request then open trade");
                        }
                        if (Phase == GamePhase.Betting)
                        {
                            ImGui.SameLine();
                            var betForConfirm = betEdits.TryGetValue(pi, out var bec) ? bec : p.Bet;
                            var canConfirm = !string.IsNullOrWhiteSpace(betForConfirm);
                            if (!canConfirm) ImGui.BeginDisabled();
                            if (ImGui.SmallButton($"Remind##{pi}confirm"))
                            {
                                if (betEdits.TryGetValue(pi, out var pendingBet))
                                {
                                    betEdits.Remove(pi);
                                    if (pendingBet != p.Bet)
                                        Apply(new SetPlayerBet(pi, pendingBet));
                                }
                                if (config.RemindTargetEnabled && hasWorld)
                                    QueueTarget(p.FullName, p.World);
                                Apply(new AnnounceBetConfirm(pi, p.BankBalance(config)));
                            }
                            if (!canConfirm) ImGui.EndDisabled();
                            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                ImGui.SetTooltip("Remind the player of their current bet in chat");
                        }
                    }
                    else
                    {
                        // Show the effective bet for this split hand (read-only)
                        var eb = GameEngine.GetEffectiveBet(p, hand);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextDisabled(eb > 0 ? GameEngine.FormatGil(eb) : (GameEngine.ParseBet(p.Bet) > 0 ? GameEngine.FormatGil(GameEngine.ParseBet(p.Bet)) : p.Bet));
                    }

                    // ── Bank column ───────────────────────────────────────────
                    ImGui.TableSetColumnIndex(2);
                    if (isFirstHand && !multiHand)
                        DrawBankCell(pi, displayPi, p, ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);

                    // ── Cards column ──────────────────────────────────────────
                    ImGui.TableSetColumnIndex(3);
                    if (hand.Cards.Count > 0)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(GameEngine.HandString(hand.Cards));
                        if (hand.Doubled)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "2x");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Player doubled down");
                        }
                    }

                    // ── Score column ──────────────────────────────────────────
                    ImGui.TableSetColumnIndex(4);
                    DrawScoreCell(hand.Cards, hand.State);

                    // ── Status column ─────────────────────────────────────────
                    ImGui.TableSetColumnIndex(5);
                    var statusCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (Phase == GamePhase.Payout)
                    {
                        var (lbl, col) = PayoutDisplay(State, pi, hi);
                        if (lbl.Length > 0)
                        {
                            ImGui.TextColored(col, lbl);
                            if (ImGui.IsItemHovered())
                            {
                                var amt = GameEngine.PayoutAmountString(State, pi, hi);
                                ImGui.SetTooltip(amt.Length > 0 ? $"{lbl} {amt}" : lbl);
                            }
                        }
                        if (p.Hands.Count == 1)
                        {
                            var totalOwed = 0m;
                            var result    = GameEngine.GetPayoutResult(State, pi, 0);
                            var eb        = GameEngine.GetEffectiveBet(p, p.Hands[0]);
                            var d         = GameEngine.PayoutDelta(State, pi, 0) ?? 0m;
                            totalOwed = result switch
                            {
                                PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin => eb + d,
                                PayoutResult.Push                                                  => eb,
                                _                                                                  => 0m,
                            };
                            if (totalOwed > 0)
                            {
                                var ctrlHeld   = ImGui.GetIO().KeyCtrl;
                                var initBet    = GameEngine.ParseBet(p.Bet);
                                var keepBetVal = totalOwed - initBet;
                                var copyVal    = ctrlHeld ? $"{keepBetVal:0.##}" : $"{totalOwed:0.##}";
                                var copyW      = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                                ImGui.SameLine();
                                ImGui.SetCursorPosX(statusCellRight - copyW + ImGui.GetStyle().ItemSpacing.X);
                                if (ImGui.SmallButton($"Copy##{pi}payout"))
                                    ImGui.SetClipboardText(copyVal);
                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip(ctrlHeld
                                        ? $"Copy (keep initial bet): {keepBetVal:0.##}"
                                        : $"Copy total owed: {totalOwed:0.##}\nCtrl+Click to copy minus initial bet: {keepBetVal:0.##}");
                            }
                        }
                    }
                    else
                    {
                        DrawHandStateLabel(hand);
                        if (Phase == GamePhase.Betting && hi == 0)
                        {
                            var sitW = ImGui.CalcTextSize("Sit Out").X + ImGui.GetStyle().FramePadding.X * 2;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(statusCellRight - sitW);
                            if (ImGui.SmallButton($"Sit Out##{pi}sitout"))
                                Apply(new ToggleSittingOut(pi));
                        }
                        else if (isActiveHand && !State.WaitingForNextPlayer)
                        {
                            var remindW = ImGui.CalcTextSize("Remind").X + ImGui.GetStyle().FramePadding.X * 2;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(statusCellRight - remindW);
                            if (ImGui.SmallButton($"Remind##{pi}_{hi}resend"))
                                Apply(new AnnouncePlayerTurn(pi, hi));
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Resend turn start message"u8);
                        }
                    }

                    // ── Actions column ────────────────────────────────────────
                    ImGui.TableSetColumnIndex(6);
                    var actionsCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    var hasAnyPending = pendingDouble.HasValue || pendingSplit.HasValue;
                    var isPendingDouble = pendingDouble.HasValue && pendingDouble.Value == (pi, hi);
                    var isPendingSplit  = pendingSplit.HasValue  && pendingSplit.Value  == (pi, hi);
                    var asp = ImGui.GetStyle().ItemSpacing.X;
                    float ABW(string s) => ImGui.CalcTextSize(s).X + ImGui.GetStyle().FramePadding.X * 2;
#if DEBUG
                    var _scenHit        = Scenario.IsStep($"Hit:{pi}:{hi}");
                    var _scenStand      = Scenario.IsStep($"Stand:{pi}:{hi}");
                    var _scenDbl        = Scenario.IsStep($"Dbl:{pi}:{hi}");
                    var _scenSpl        = Scenario.IsStep($"Spl:{pi}:{hi}");
                    var _scenConfirmDbl = Scenario.IsStep($"ConfirmDbl:{pi}:{hi}");
                    var _scenConfirmSpl = Scenario.IsStep($"ConfirmSpl:{pi}:{hi}");
                    var _scenAdvPlayer  = Scenario.IsStep("AdvancePlayer");
#endif

                    if (Phase == GamePhase.PlayerTurns && State.WaitingForNextPlayer
                        && pi == ActivePlayerIndex && hi == ActiveHandIndex)
                    {
                        var moreHands = p.Hands.Skip(hi + 1).Any(h => h.State == HandState.Playing);
                        var advLabel  = moreHands ? "Next Hand ↓" : "Next Player ↓";
                        ImGui.SetCursorPosX(actionsCellRight - ABW(advLabel));
#if DEBUG
                        if (!_scenAdvPlayer) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"{advLabel}##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            Apply(new AdvanceToNextPlayer());
                        }
#if DEBUG
                        if (!_scenAdvPlayer) ImGui.EndDisabled();
#endif
                    }
                    else if (isPendingDouble)
                    {
                        ImGui.SetCursorPosX(actionsCellRight - ABW("Confirm Dbl") - asp - ABW("Cancel"));
#if DEBUG
                        if (!_scenConfirmDbl) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"Confirm Dbl##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            Apply(new AnnounceDoubleConfirm(pi, hi));
                            Apply(new DoubleDown(pi, hi));
                            var dblKey2    = p.StatsKey();
                            var dblAmt2    = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
                            if (config.PlayerStatsStore.TryGetValue(dblKey2, out var dblStat2) && dblAmt2 > 0 && dblStat2.IsBanking())
                            {
                                var before2 = dblStat2.Bank;
                                ApplyBank(dblStat2, new BankDoubleDown(dblAmt2));
                                config.NarrationLog.Add($"[Bank] {p.DisplayName}: doubled — {dblAmt2:N0} deducted (was {before2:N0} → {dblStat2.Bank:N0})");
                                config.Save();
                            }
                            pendingDouble = null;
                            QueueHitRoll(isDealer: false, pi, hi);
                        }
#if DEBUG
                        if (!_scenConfirmDbl) ImGui.EndDisabled();
#endif
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Cancel##{pi}_{hi}dblcancel")) pendingDouble = null;
                    }
                    else if (isPendingSplit)
                    {
                        ImGui.SetCursorPosX(actionsCellRight - ABW("Confirm Spl") - asp - ABW("Cancel"));
#if DEBUG
                        if (!_scenConfirmSpl) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"Confirm Spl##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            var splKey2    = p.StatsKey();
                            var splAmt2    = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
                            if (config.PlayerStatsStore.TryGetValue(splKey2, out var splStat2) && splAmt2 > 0 && splStat2.IsBanking())
                            {
                                var before2 = splStat2.Bank;
                                ApplyBank(splStat2, new BankSplit(splAmt2));
                                config.NarrationLog.Add($"[Bank] {p.DisplayName}: split — {splAmt2:N0} deducted (was {before2:N0} → {splStat2.Bank:N0})");
                                config.Save();
                            }
                            Apply(new SplitHand(pi, hi));
                            pendingSplit = null;
                            // The 1-card split hands are auto-hit in Draw()
                        }
#if DEBUG
                        if (!_scenConfirmSpl) ImGui.EndDisabled();
#endif
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Cancel##{pi}_{hi}splcancel")) pendingSplit = null;
                    }
                    else if (Phase == GamePhase.Deal && PlayerHitActive(pi, hi))
                    {
                        ImGui.SetCursorPosX(actionsCellRight - ABW("Draw"));
                        if (ImGui.SmallButton($"Draw##{pi}_{hi}"))
                            QueueHitRoll(isDealer: false, pi, hi);
                    }
                    else
                    {
                        var total = ABW("Stand") + asp + ABW("Hit") + asp + ABW("Dbl") + asp + ABW("Spl")
                                  + (isFirstHand && !multiHand ? asp + ABW("X") : 0);
                        ImGui.SetCursorPosX(actionsCellRight - total);

                        var canStand = !hasAnyPending && Phase == GamePhase.PlayerTurns
                                    && pi == ActivePlayerIndex && hi == ActiveHandIndex
                                    && GameEngine.CanHit(hand);
                        if (!canStand) ImGui.BeginDisabled();
#if DEBUG
                        if (!_scenStand) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"Stand##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            Apply(new StandPlayer(pi, hi));
                        }
#if DEBUG
                        if (!_scenStand) ImGui.EndDisabled();
#endif
                        if (!canStand) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var hitActive = PlayerHitActive(pi, hi);
                        if (!hitActive) ImGui.BeginDisabled();
#if DEBUG
                        if (!_scenHit) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"Hit##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            Apply(new AnnouncePlayerHit(pi, hi));
                            QueueHitRoll(isDealer: false, pi, hi);
                        }
#if DEBUG
                        if (!_scenHit) ImGui.EndDisabled();
#endif
                        if (!hitActive) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var canDouble = !hasAnyPending && isActiveHand
                                     && GameEngine.CanDouble(hand, p.Bet);
                        if (!canDouble) ImGui.BeginDisabled();
#if DEBUG
                        if (!_scenDbl) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"Dbl##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            var dblBet     = GameEngine.GetEffectiveBet(p, hand);
                            var dblBank    = p.BankBalance(config);
                            var dblRounded = (long)Math.Ceiling(dblBet);
                            var fromBank   = dblBank >= dblRounded;
                            var bankAfter  = fromBank ? dblBank - dblRounded : dblRounded - dblBank;
                            pendingDouble = (pi, hi);
                            Apply(new AnnounceDouble(pi, hi, fromBank, bankAfter));
                            if (!fromBank && hasWorld && config.AutoTradeEnabled)
                                QueueTrade(p.FullName, p.World);
                        }
#if DEBUG
                        if (!_scenDbl) ImGui.EndDisabled();
#endif
                        if (!canDouble) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var canSplit = !hasAnyPending && isActiveHand
                                    && GameEngine.CanSplit(hand);
                        if (!canSplit) ImGui.BeginDisabled();
#if DEBUG
                        if (!_scenSpl) ImGui.BeginDisabled();
#endif
                        if (ImGui.SmallButton($"Spl##{pi}_{hi}"))
                        {
#if DEBUG
                            Scenario.Advance();
#endif
                            var splBet     = GameEngine.GetEffectiveBet(p, hand);
                            var splBank    = p.BankBalance(config);
                            var splRounded = (long)Math.Ceiling(splBet);
                            var fromBank   = splBank >= splRounded;
                            var bankAfter  = fromBank ? splBank - splRounded : splRounded - splBank;
                            pendingSplit = (pi, hi);
                            Apply(new AnnounceSplit(pi, hi, fromBank, bankAfter));
                            if (!fromBank && hasWorld && config.AutoTradeEnabled)
                                QueueTrade(p.FullName, p.World);
                        }
#if DEBUG
                        if (!_scenSpl) ImGui.EndDisabled();
#endif
                        if (!canSplit) ImGui.EndDisabled();

                        if (isFirstHand && !multiHand)
                        {
                            ImGui.SameLine();
                            var canRemove = Phase == GamePhase.Betting;
                            if (!canRemove) ImGui.BeginDisabled();
                            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.7f, 0.15f, 0.15f, 1f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.25f, 0.25f, 1f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.5f, 0.05f, 0.05f, 1f));
                            if (ImGui.SmallButton($"X##{pi}")) removeAt = pi;
                            ImGui.PopStyleColor(3);
                            if (!canRemove) ImGui.EndDisabled();
                        }
                    }
                }

            }

            if (reorderSwap.HasValue)
                (reorderIndices[reorderSwap.Value.A], reorderIndices[reorderSwap.Value.B]) =
                    (reorderIndices[reorderSwap.Value.B], reorderIndices[reorderSwap.Value.A]);

            // ── Sitting-out section ───────────────────────────────────────────
            var sittingOutPlayers = State.Players
                .Select((p, i) => (p, i))
                .Where(x => x.p.SittingOut)
                .ToList();
            if (sittingOutPlayers.Count > 0)
            {
                // Separator label row
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ToU32(new Vector4(0.10f, 0.10f, 0.10f, 1f)));
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ToU32(new Vector4(0.10f, 0.10f, 0.10f, 1f)));
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Sitting out");

                foreach (var (sp, spi) in sittingOutPlayers)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ToU32(new Vector4(0.18f, 0.18f, 0.18f, 1f)));
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ToU32(new Vector4(0.18f, 0.18f, 0.18f, 1f)));

                    // Name
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(sp.DisplayName);
                    if (ImGui.IsItemHovered() && sp.World.Length > 0)
                        ImGui.SetTooltip($"{sp.FullName}@{sp.World}");

                    // Bank
                    ImGui.TableSetColumnIndex(2);
                    {
                        var sitBankVal = sp.BankBalance(config);
                        var sitBankCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                        ImGui.AlignTextToFramePadding();
                        if (sitBankVal > 0)
                        {
                            ImGui.TextDisabled(GameEngine.FormatGil(sitBankVal));
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip("Click to copy");
                                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                    ImGui.SetClipboardText(sitBankVal.ToString());
                            }
                        }
                        else
                        {
                            ImGui.TextDisabled("—");
                        }
                        DrawBankManageButton(spi, sitBankCellRight, "sitbank");
                    }

                    // Status: Resume button
                    ImGui.TableSetColumnIndex(5);
                    var sitStatusCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    var resumeW = ImGui.CalcTextSize("Resume").X + ImGui.GetStyle().FramePadding.X * 2;
                    ImGui.SetCursorPosX(sitStatusCellRight - resumeW);
                    var canResume = Phase == GamePhase.Betting;
                    if (!canResume) ImGui.BeginDisabled();
                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.55f, 0.35f, 0.1f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.65f, 0.45f, 0.15f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.75f, 0.55f, 0.2f, 1f));
                    if (ImGui.SmallButton($"Resume##{spi}sitresume"))
                        Apply(new ToggleSittingOut(spi));
                    ImGui.PopStyleColor(3);
                    if (!canResume) ImGui.EndDisabled();
                    if (!canResume && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip("Players can only resume during the betting phase.");

                    // Actions: Remove (betting only)
                    ImGui.TableSetColumnIndex(6);
                    if (Phase == GamePhase.Betting)
                    {
                        var sitActCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                        var sitRemoveW = ImGui.CalcTextSize("X").X + ImGui.GetStyle().FramePadding.X * 2;
                        ImGui.SetCursorPosX(sitActCellRight - sitRemoveW);
                        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.7f, 0.15f, 0.15f, 1f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.25f, 0.25f, 1f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.5f, 0.05f, 0.05f, 1f));
                        if (ImGui.SmallButton($"X##{spi}sitremove")) removeAt = spi;
                        ImGui.PopStyleColor(3);
                    }
                }
            }

            ImGui.EndTable();
        }

        if (removeAt >= 0)
        {
            betEdits.Remove(removeAt);
            var shifted = betEdits.Where(kv => kv.Key > removeAt).ToList();
            foreach (var kv in shifted) { betEdits.Remove(kv.Key); betEdits[kv.Key - 1] = kv.Value; }
            Apply(new RemovePlayer(removeAt));
        }

        // ── Add player (Betting only) ─────────────────────────────────────────
        ImGui.Spacing();
        if (Phase == GamePhase.Betting)
        {
            ImGui.SetNextItemWidth(200);
            var nameSubmitted = ImGui.InputText("##newName"u8, ref newPlayerName, 64,
                ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            var canAdd = newPlayerName.Length > 0;
            if (!canAdd) ImGui.BeginDisabled();
            if (ImGui.Button("Add Player") || (nameSubmitted && canAdd))
            {
                Apply(new AddPlayer(Nickname: newPlayerName));
                newPlayerName = string.Empty;
            }
            if (!canAdd) ImGui.EndDisabled();
            ImGui.Spacing();
        }

        // ── Phase action bar ──────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.Spacing();

        var dealProgress = Phase == GamePhase.Deal
            ? $"  (dealer: {State.DealerHand.Cards.Count}/1  players: " +
              $"{(State.Players.Count > 0 ? State.Players.Min(p => p.Hands[0].Cards.Count) : 0)}-" +
              $"{(State.Players.Count > 0 ? State.Players.Max(p => p.Hands[0].Cards.Count) : 0)}/2)"
            : string.Empty;
        string phaseLabel;
        if (Phase == GamePhase.PlayerTurns
            && ActivePlayerIndex >= 0 && ActivePlayerIndex < State.Players.Count
            && ActiveHandIndex >= 0)
        {
            var ap   = State.Players[ActivePlayerIndex];
            var ah   = ActiveHandIndex < ap.Hands.Count ? ap.Hands[ActiveHandIndex] : null;
            var name = ap.Hands.Count > 1 ? $"{ap.DisplayName} (Hand {ActiveHandIndex + 1})" : ap.DisplayName;
            var acts = ah != null
                ? GameEngine.ValidActionsString(ah, GameEngine.CanDouble(ah, ap.Bet), GameEngine.CanSplit(ah))
                : string.Empty;
            phaseLabel = $"Phase: Player Actions  ({name}'s turn — {acts})";
        }
        else
        {
            phaseLabel = Phase switch
            {
                GamePhase.Betting    => "Phase: Betting",
                GamePhase.Deal       => $"Phase: Deal{dealProgress}",
                GamePhase.PlayerTurns => "Phase: Player Actions",
                GamePhase.DealerTurn => "Phase: Dealer Turn",
                GamePhase.Payout     => "Phase: Payout",
                _                    => string.Empty,
            };
        }
        ImGui.TextDisabled(phaseLabel);
        ImGui.Spacing();

        switch (Phase)
        {
            case GamePhase.Betting:
                if (ImGui.Button("Announce Betting Open"))
                    Apply(new AnnounceBettingOpen());
                ImGui.SameLine();
                var effectiveBets = State.Players.Select((p, i) =>
                    betEdits.TryGetValue(i, out var e) ? e : p.Bet).ToList();
                var shortfallPlayers = State.Players
                    .Select((p, i) => (p, i))
                    .Where(x => !x.p.SittingOut)
                    .Where(x => {
                        var key = x.p.StatsKey();
                        if (!config.PlayerStatsStore.TryGetValue(key, out var st)) return false;
                        if (!st.IsBanking()) return false;
                        var eb = GameEngine.ParseBet(effectiveBets[x.i]);
                        return eb > 0 && st.Bank < eb;
                    })
                    .Select(x => x.p.DisplayName)
                    .ToList();
                var canDeal = State.Players.Count > 0
                           && State.Players.Any(p => !p.SittingOut)
                           && State.Players.Select((p, i) => p.SittingOut || !string.IsNullOrWhiteSpace(effectiveBets[i])).All(x => x)
                           && shortfallPlayers.Count == 0
                           && !isReorderMode;
                if (!canDeal) ImGui.BeginDisabled();
#if DEBUG
                var _scenStartDeal = Scenario.IsStep("StartDeal");
                if (!_scenStartDeal) ImGui.BeginDisabled();
#endif
                if (ImGui.Button("Start Deal →"))
                {
#if DEBUG
                    Scenario.Advance();
#endif
                    // Flush uncommitted bet edits before transitioning
                    foreach (var (idx, val) in betEdits.ToList())
                    {
                        betEdits.Remove(idx);
                        if (val != State.Players[idx].Bet)
                            Apply(new SetPlayerBet(idx, val));
                    }
                    Apply(new StartDeal());
                    // Deduct initial bets from player banks (skip sitting-out players)
                    foreach (var p in State.Players)
                    {
                        if (p.SittingOut) continue;
                        var betAmt = (long)Math.Ceiling(GameEngine.ParseBet(p.Bet));
                        if (betAmt <= 0) continue;
                        if (!p.TryGetBankingStat(config, out var betStat)) continue;
                        ApplyBank(betStat, new BankBet(betAmt));
                    }
                    // Queue initial cards: dealer first, then each active player gets both cards in a pair
                    for (var i = 0; i < State.Players.Count; i++)
                    {
                        if (State.Players[i].SittingOut) continue;
                        autoDealQueue.Enqueue((false, i, 0, true));   // first card — announce
                        autoDealQueue.Enqueue((false, i, 0, false));  // second card
                    }
                    Apply(new AnnounceDealerDeal());
                    QueueHitRoll(isDealer: true, -1, -1);
                }
#if DEBUG
                if (!_scenStartDeal) ImGui.EndDisabled();
#endif
                if (!canDeal) ImGui.EndDisabled();
                if (!canDeal && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(State.Players.Count == 0
                        ? "Add at least one player first."
                        : shortfallPlayers.Count > 0
                            ? $"Bank shortfall — resolve before dealing:\n{string.Join("\n", shortfallPlayers)}"
                            : "All players need a bet before dealing.");
                break;

            case GamePhase.Deal:
                var dealDone = GameEngine.IsDealComplete(State);
                if (!dealDone) ImGui.BeginDisabled();
#if DEBUG
                var _scenBPT = Scenario.IsStep("BeginPlayerTurns");
                if (!_scenBPT) ImGui.BeginDisabled();
#endif
                if (ImGui.Button("Begin Player Turns →"))
                {
#if DEBUG
                    Scenario.Advance();
#endif
                    Apply(new BeginPlayerTurns());
                }
#if DEBUG
                if (!_scenBPT) ImGui.EndDisabled();
#endif
                if (!dealDone) ImGui.EndDisabled();
                if (!dealDone && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Dealer needs 1 card; each player needs 2 cards."u8);
                break;

            case GamePhase.PlayerTurns:
                break;

            case GamePhase.DealerTurn:
                if (State.WaitingForDealer)
                {
#if DEBUG
                    var _scenBDT = Scenario.IsStep("BeginDealerTurn");
                    if (!_scenBDT) ImGui.BeginDisabled();
#endif
                    if (ImGui.Button("Begin Dealer Turn →"))
                    {
#if DEBUG
                        Scenario.Advance();
#endif
                        Apply(new BeginDealerTurn());
                    }
#if DEBUG
                    if (!_scenBDT) ImGui.EndDisabled();
#endif
                }
                else
                {
                    var canPayout = GameEngine.CanGoToPayout(State);
                    if (!canPayout) ImGui.BeginDisabled();
#if DEBUG
                    var _scenGTP = Scenario.IsStep("GoToPayout");
                    if (!_scenGTP) ImGui.BeginDisabled();
#endif
                    if (ImGui.Button("Go to Payout →"))
                    {
#if DEBUG
                        Scenario.Advance();
#endif
                        Apply(new GoToPayout());
                        UpdatePlayerStats();
                    }
#if DEBUG
                    if (!_scenGTP) ImGui.EndDisabled();
#endif
                    if (!canPayout) ImGui.EndDisabled();
                    if (!canPayout && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip("Dealer must finish their hand first."u8);
                }
                break;

            case GamePhase.Payout:
#if DEBUG
                var _scenNR = Scenario.IsStep("NewRound");
                if (!_scenNR) ImGui.BeginDisabled();
#endif
                if (ImGui.Button("New Round"))
                {
#if DEBUG
                    Scenario.Advance();
#endif
                    Apply(new NewRound());
                }
#if DEBUG
                if (!_scenNR) ImGui.EndDisabled();
#endif
                break;
        }

        if (Phase != GamePhase.Payout && Phase != GamePhase.Betting)
        {
            ImGui.SameLine();
            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            if (!ctrlHeld) ImGui.BeginDisabled();
            if (ImGui.Button("Abort Round"))
            {
                config.NarrationLog.Add("Round aborted.");
                chatQueue.Clear();
                deferredRoll = null;
                Apply(new NewRound());
            }
            if (!ctrlHeld) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
                ImGui.SetTooltip("Hold Ctrl to abort the round."u8);
        }

        if (uiBusy) ImGui.EndDisabled();

        // ── Narration panel ───────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Chat Narration");
        ImGui.Separator();
        {
            var narUseCmd = config.NarrationUseChannelCommand;
            if (ImGui.Checkbox("Add channel command", ref narUseCmd))
            {
                config.NarrationUseChannelCommand = narUseCmd;
                config.Save();
            }

            ImGui.SameLine();
            if (config.NarrationLog.Count == 0) ImGui.BeginDisabled();
            if (ImGui.Button("Copy All"))
            {
                var sb = new StringBuilder();
                foreach (var line in config.NarrationLog)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(config.NarrationUseChannelCommand
                        ? config.ChatChannel + " " + line
                        : line);
                }
                ImGui.SetClipboardText(sb.ToString());
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear")) { config.NarrationLog.Clear(); config.Save(); }
            if (config.NarrationLog.Count == 0) ImGui.EndDisabled();

            ImGui.Spacing();
            if (ImGui.BeginChild("##narLog", new Vector2(0, 0), true))
            {
                for (var ni = 0; ni < config.NarrationLog.Count; ni++)
                {
                    var line    = config.NarrationLog[ni];
                    var display = config.NarrationUseChannelCommand
                        ? config.ChatChannel + " " + line
                        : line;
                    ImGui.PushID(ni);
                    if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(display);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy to clipboard"u8);
                    ImGui.PopID();
                    ImGui.SameLine();
                    ImGui.TextUnformatted(display);
                }
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                    ImGui.SetScrollHereY(1.0f);
            }
            ImGui.EndChild();
        }
    }
}
