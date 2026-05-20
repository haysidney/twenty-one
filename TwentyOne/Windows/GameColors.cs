using System.Numerics;

namespace TwentyOne.Windows;

/// <summary>
/// Centralised ImGui colour palette. Every coloured text/style call site in the
/// plugin's windows should pull from this class so that semantic names - and
/// the actual RGB values - stay in one place.
/// </summary>
internal static class GameColors
{
    // ── Hand-state / outcome semantic colours (shared with winners/losers lists) ──
    public static readonly Vector4 BustRed       = new(1f,    0.35f, 0.35f, 1f); // Bust, Lose, losers list
    public static readonly Vector4 BlackjackGold = new(1f,    0.85f, 0f,    1f); // Blackjack, BJ Win
    public static readonly Vector4 PlayingGreen  = new(0.4f,  0.9f,  0.4f,  1f); // Playing label, Charlie
    public static readonly Vector4 StandGrey     = new(0.55f, 0.55f, 0.55f, 1f); // Stood hand
    public static readonly Vector4 ProfitGreen   = new(0.35f, 0.9f,  0.35f, 1f); // Win, winners list, profit
    public static readonly Vector4 PushGrey      = new(0.7f,  0.7f,  0.7f,  1f); // Push, pushers list
    public static readonly Vector4 DisabledGrey  = new(0.6f,  0.6f,  0.6f,  1f); // muted secondary text

    // ── Bank-ledger entry colours (deposit/withdraw rows) ──
    public static readonly Vector4 CreditGreen   = new(0.4f,  1f,    0.4f,  1f); // +amount
    public static readonly Vector4 DebitRed      = new(1f,    0.4f,  0.4f,  1f); // -amount

    // ── Banner / warning gradient (low → high amber) ──
    public static readonly Vector4 WarningAmber  = new(1f,    0.8f,  0.2f,  1f); // shortfall warning
    public static readonly Vector4 BannerGold    = new(1f,    0.85f, 0.3f,  1f); // session / venue banner
    public static readonly Vector4 ActiveOrange  = new(1f,    0.7f,  0.2f,  1f); // active scenario / bet-bank prompt

    // ── Modal ──
    public static readonly Vector4 ModalTitleBlue   = new(0.4f, 0.85f, 1f, 1f);
    public static readonly Vector4 TransparentDimBg = new(0,    0,    0,  0); // suppresses modal background dim
}
