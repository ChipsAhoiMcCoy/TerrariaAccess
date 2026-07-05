#nullable enable
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Guidance;

internal sealed class GuidanceNamingDialogController
{
    private const int MaxWaypointNameLength = 40;

    private readonly TextInputDialogController _textDialog = new();
    private string _text = string.Empty;
    private string _fallbackName = string.Empty;
    private Vector2 _worldPosition;
    private int _playerIndex = -1;

    public bool IsActive => _textDialog.IsActive;

    public void Begin(Player player, string fallbackName, int existingWaypoints, System.Action<string> log)
    {
        if (IsActive)
        {
            log("BeginNaming skipped: naming already active.");
            return;
        }

        _worldPosition = player.Center;
        _fallbackName = fallbackName;
        _playerIndex = player.whoAmI;
        _text = string.Empty;

        log($"BeginNaming: WorldPos=({_worldPosition.X:F1}, {_worldPosition.Y:F1}), " +
            $"FallbackName=\"{_fallbackName}\", PlayerIndex={_playerIndex}, ExistingWaypoints={existingWaypoints}");

        _textDialog.Begin(initialText: string.Empty, log);

        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayOpen();
        Main.NewText("Waypoint naming: type a name, press Enter to save, or Escape to cancel.", Color.LightSkyBlue);
        ScreenReaderService.Announce("Type the waypoint name, then press Enter to save or Escape to cancel.");
        log("BeginNaming: Naming dialog opened, awaiting user input.");
    }

    public GuidanceNamingUpdateResult Update(System.Action<string> log)
    {
        _textDialog.UpdatePendingRestore(log);

        if (!IsActive)
        {
            return GuidanceNamingUpdateResult.None;
        }

        TextInputDialogUpdateResult dialogResult = _textDialog.Update(MaxWaypointNameLength, log);
        if (dialogResult.Kind == TextInputDialogUpdateKind.Confirmed)
        {
            string rawInput = string.IsNullOrWhiteSpace(dialogResult.Text) ? _fallbackName : dialogResult.Text.Trim();
            string resolvedName = TextSanitizer.Clean(rawInput);

            log($"UpdateNaming: Enter pressed. RawInput=\"{rawInput}\", ResolvedName=\"{resolvedName}\", " +
                $"WorldPos=({_worldPosition.X:F1}, {_worldPosition.Y:F1})");

            global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayOpen();
            _text = string.Empty;
            _fallbackName = string.Empty;
            int playerIndex = _playerIndex;
            _playerIndex = -1;
            return GuidanceNamingUpdateResult.Confirmed(resolvedName, _worldPosition, playerIndex);
        }

        if (dialogResult.Kind == TextInputDialogUpdateKind.Canceled)
        {
            log("UpdateNaming: Escape pressed. Cancelling waypoint creation.");
            ScreenReaderService.Announce("Waypoint creation cancelled", force: true);
            global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayClose();

            int playerIndex = _playerIndex;
            _text = string.Empty;
            _fallbackName = string.Empty;
            _playerIndex = -1;
            return GuidanceNamingUpdateResult.Canceled(playerIndex);
        }

        _text = _textDialog.Text;
        return GuidanceNamingUpdateResult.None;
    }

    public void Close(System.Action<string> log)
    {
        if (!IsActive)
        {
            log("CloseNaming skipped: naming not active.");
            return;
        }

        log($"CloseNaming: Closing naming dialog. FinalText=\"{_text}\"");
        _text = string.Empty;
        _fallbackName = string.Empty;
        _playerIndex = -1;
        _textDialog.Close(log);
        log("CloseNaming: Naming dialog closed.");
    }
}

internal readonly record struct GuidanceNamingUpdateResult(
    GuidanceNamingUpdateKind Kind,
    string ResolvedName,
    Vector2 WorldPosition,
    int PlayerIndex)
{
    public static GuidanceNamingUpdateResult None => new(GuidanceNamingUpdateKind.None, string.Empty, default, -1);

    public static GuidanceNamingUpdateResult Confirmed(string resolvedName, Vector2 worldPosition, int playerIndex)
        => new(GuidanceNamingUpdateKind.Confirmed, resolvedName, worldPosition, playerIndex);

    public static GuidanceNamingUpdateResult Canceled(int playerIndex)
        => new(GuidanceNamingUpdateKind.Canceled, string.Empty, default, playerIndex);
}

internal enum GuidanceNamingUpdateKind
{
    None,
    Confirmed,
    Canceled
}
