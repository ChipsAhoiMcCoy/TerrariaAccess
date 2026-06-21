#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems.Guidance;

internal sealed class GuidanceCustomTargetDialogController
{
    private const int MaxCustomTargetInputLength = 80;

    private readonly TextInputDialogController _textDialog = new();
    private string _text = string.Empty;
    private int _playerIndex = -1;

    public bool IsActive => _textDialog.IsActive;

    public void Begin(Player player, System.Action<string> log)
    {
        if (IsActive)
        {
            log("BeginCustomTargetInput skipped: custom target input already active.");
            return;
        }

        _playerIndex = player.whoAmI;
        _text = string.Empty;

        log($"BeginCustomTargetInput: PlayerIndex={_playerIndex}");

        _textDialog.Begin(initialText: string.Empty, log);

        SoundEngine.PlaySound(SoundID.MenuOpen);
        Main.NewText("Custom tracker: type tile, object, item, NPC, enemy, critter, or projectile ID/name. Press Enter to save, or Escape to cancel.", Color.LightSkyBlue);
        ScreenReaderService.Announce("Type a custom tracker target, such as tile 15, item 9, NPC 17, enemy 3, or projectile 12. Press Enter to save or Escape to cancel.");
        log("BeginCustomTargetInput: Dialog opened, awaiting user input.");
    }

    public GuidanceCustomTargetInputUpdateResult Update(System.Action<string> log)
    {
        _textDialog.UpdatePendingRestore(log);

        if (!IsActive)
        {
            return GuidanceCustomTargetInputUpdateResult.None;
        }

        TextInputDialogUpdateResult dialogResult = _textDialog.Update(MaxCustomTargetInputLength, log);
        if (dialogResult.Kind == TextInputDialogUpdateKind.Confirmed)
        {
            string rawInput = dialogResult.Text.Trim();
            log($"UpdateCustomTargetInput: Enter pressed. RawInput=\"{rawInput}\"");

            SoundEngine.PlaySound(SoundID.MenuOpen);
            _text = string.Empty;
            int playerIndex = _playerIndex;
            _playerIndex = -1;
            return GuidanceCustomTargetInputUpdateResult.Confirmed(rawInput, playerIndex);
        }

        if (dialogResult.Kind == TextInputDialogUpdateKind.Canceled)
        {
            log("UpdateCustomTargetInput: Escape pressed. Cancelling custom tracker creation.");
            ScreenReaderService.Announce("Custom tracker creation cancelled", force: true);
            SoundEngine.PlaySound(SoundID.MenuClose);

            int playerIndex = _playerIndex;
            _text = string.Empty;
            _playerIndex = -1;
            return GuidanceCustomTargetInputUpdateResult.Canceled(playerIndex);
        }

        _text = _textDialog.Text;
        return GuidanceCustomTargetInputUpdateResult.None;
    }

    public void Close(System.Action<string> log)
    {
        if (!IsActive)
        {
            log("CloseCustomTargetInput skipped: custom target input not active.");
            return;
        }

        log($"CloseCustomTargetInput: Closing custom target input dialog. FinalText=\"{_text}\"");
        _text = string.Empty;
        _playerIndex = -1;
        _textDialog.Close(log);
        log("CloseCustomTargetInput: Dialog closed.");
    }
}

internal readonly record struct GuidanceCustomTargetInputUpdateResult(
    GuidanceCustomTargetInputUpdateKind Kind,
    string RawInput,
    int PlayerIndex)
{
    public static GuidanceCustomTargetInputUpdateResult None => new(GuidanceCustomTargetInputUpdateKind.None, string.Empty, -1);

    public static GuidanceCustomTargetInputUpdateResult Confirmed(string rawInput, int playerIndex)
        => new(GuidanceCustomTargetInputUpdateKind.Confirmed, rawInput, playerIndex);

    public static GuidanceCustomTargetInputUpdateResult Canceled(int playerIndex)
        => new(GuidanceCustomTargetInputUpdateKind.Canceled, string.Empty, playerIndex);
}

internal enum GuidanceCustomTargetInputUpdateKind
{
    None,
    Confirmed,
    Canceled
}
