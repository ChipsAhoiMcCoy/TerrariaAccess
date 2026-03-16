#nullable enable
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Guidance;

internal sealed class GuidanceNamingDialogController
{
    private InputSnapshot? _inputSnapshot;
    private string _text = string.Empty;
    private string _fallbackName = string.Empty;
    private Vector2 _worldPosition;
    private int _playerIndex = -1;

    public bool IsActive { get; private set; }

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
        IsActive = true;

        log($"BeginNaming: WorldPos=({_worldPosition.X:F1}, {_worldPosition.Y:F1}), " +
            $"FallbackName=\"{_fallbackName}\", PlayerIndex={_playerIndex}, ExistingWaypoints={existingWaypoints}");

        _inputSnapshot = new InputSnapshot
        {
            BlockInput = Main.blockInput,
            WritingText = PlayerInput.WritingText,
            PlayerInventory = Main.playerInventory,
            EditSign = Main.editSign,
            EditChest = Main.editChest,
            DrawingPlayerChat = Main.drawingPlayerChat,
            InFancyUI = Main.inFancyUI,
            GameMenu = Main.gameMenu,
            ChatText = Main.chatText ?? string.Empty
        };

        log($"BeginNaming: InputSnapshot saved [BlockInput={_inputSnapshot.BlockInput}, " +
            $"WritingText={_inputSnapshot.WritingText}, PlayerInventory={_inputSnapshot.PlayerInventory}, " +
            $"EditSign={_inputSnapshot.EditSign}, EditChest={_inputSnapshot.EditChest}, " +
            $"DrawingPlayerChat={_inputSnapshot.DrawingPlayerChat}, InFancyUI={_inputSnapshot.InFancyUI}, " +
            $"GameMenu={_inputSnapshot.GameMenu}, ChatText=\"{_inputSnapshot.ChatText}\"]");

        Main.blockInput = true;
        Main.drawingPlayerChat = false;
        PlayerInput.WritingText = true;
        Main.clrInput();
        Main.chatRelease = false;

        log("BeginNaming: Input state configured [blockInput=true, drawingPlayerChat=false, " +
            "WritingText=true, clrInput called, chatRelease=false]");

        SoundEngine.PlaySound(SoundID.MenuOpen);
        Main.NewText("Waypoint naming: type a name, press Enter to save, or Escape to cancel.", Color.LightSkyBlue);
        ScreenReaderService.Announce("Type the waypoint name, then press Enter to save or Escape to cancel.");
        log("BeginNaming: Naming dialog opened, awaiting user input.");
    }

    public GuidanceNamingUpdateResult Update(System.Action<string> log)
    {
        if (!IsActive)
        {
            return GuidanceNamingUpdateResult.None;
        }

        PlayerInput.WritingText = true;
        Main.chatRelease = false;

        string newText = Main.GetInputText(_text);
        if (newText.Length > 40)
        {
            log($"UpdateNaming: Text truncated from {newText.Length} to 40 chars.");
            newText = newText.Substring(0, 40);
        }

        if (Main.inputTextEnter)
        {
            string rawInput = string.IsNullOrWhiteSpace(newText) ? _fallbackName : newText.Trim();
            string resolvedName = TextSanitizer.Clean(rawInput);

            log($"UpdateNaming: Enter pressed. RawInput=\"{rawInput}\", ResolvedName=\"{resolvedName}\", " +
                $"WorldPos=({_worldPosition.X:F1}, {_worldPosition.Y:F1})");

            SoundEngine.PlaySound(SoundID.MenuOpen);
            GuidanceNamingUpdateResult result = GuidanceNamingUpdateResult.Confirmed(resolvedName, _worldPosition, _playerIndex);
            Close(log);
            return result;
        }

        if (Main.inputTextEscape)
        {
            log("UpdateNaming: Escape pressed. Cancelling waypoint creation.");
            ScreenReaderService.Announce("Waypoint creation cancelled");
            SoundEngine.PlaySound(SoundID.MenuClose);

            int playerIndex = _playerIndex;
            Close(log);
            return GuidanceNamingUpdateResult.Canceled(playerIndex);
        }

        if (!string.Equals(newText, _text, System.StringComparison.Ordinal))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        _text = newText;
        return GuidanceNamingUpdateResult.None;
    }

    public void Close(System.Action<string> log)
    {
        if (!IsActive)
        {
            log("CloseNaming skipped: naming not active.");
            return;
        }

        log($"CloseNaming: Closing naming dialog. FinalText=\"{_text}\", HasSnapshot={_inputSnapshot is not null}");

        IsActive = false;
        _text = string.Empty;
        _fallbackName = string.Empty;
        _playerIndex = -1;

        Main.clrInput();

        if (_inputSnapshot is InputSnapshot snapshot)
        {
            PlayerInput.WritingText = snapshot.WritingText;
            Main.blockInput = snapshot.BlockInput;
            Main.playerInventory = snapshot.PlayerInventory;
            Main.editSign = snapshot.EditSign;
            Main.editChest = snapshot.EditChest;
            Main.drawingPlayerChat = snapshot.DrawingPlayerChat;
            Main.inFancyUI = snapshot.InFancyUI;
            Main.gameMenu = snapshot.GameMenu;
            Main.chatText = snapshot.ChatText;
            log($"CloseNaming: Input state restored from snapshot [BlockInput={snapshot.BlockInput}, " +
                $"WritingText={snapshot.WritingText}, PlayerInventory={snapshot.PlayerInventory}, " +
                $"DrawingPlayerChat={snapshot.DrawingPlayerChat}]");
        }
        else
        {
            PlayerInput.WritingText = false;
            Main.blockInput = false;
            Main.playerInventory = false;
            Main.editSign = false;
            Main.editChest = false;
            Main.drawingPlayerChat = false;
            Main.inFancyUI = false;
            Main.gameMenu = false;
            Main.chatText = string.Empty;
            log("CloseNaming: No snapshot found, reset all input state to defaults.");
        }

        _inputSnapshot = null;
        log("CloseNaming: Naming dialog closed.");
    }

    private sealed class InputSnapshot
    {
        public bool BlockInput;
        public bool WritingText;
        public bool PlayerInventory;
        public bool EditSign;
        public bool EditChest;
        public bool DrawingPlayerChat;
        public bool InFancyUI;
        public bool GameMenu;
        public string ChatText = string.Empty;
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
