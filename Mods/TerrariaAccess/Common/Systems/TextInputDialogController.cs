#nullable enable
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems;

internal sealed class TextInputDialogController
{
    private InputSnapshot? _inputSnapshot;
    private string _text = string.Empty;
    private bool _restorePending;

    public bool IsActive { get; private set; }
    public string Text => _text;

    public void Begin(string initialText, System.Action<string> log)
    {
        if (IsActive)
        {
            log("TextInputDialog.Begin skipped: dialog already active.");
            return;
        }

        _text = initialText ?? string.Empty;
        IsActive = true;
        _restorePending = false;

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

        log($"TextInputDialog.Begin: InputSnapshot saved [BlockInput={_inputSnapshot.BlockInput}, " +
            $"WritingText={_inputSnapshot.WritingText}, PlayerInventory={_inputSnapshot.PlayerInventory}, " +
            $"EditSign={_inputSnapshot.EditSign}, EditChest={_inputSnapshot.EditChest}, " +
            $"DrawingPlayerChat={_inputSnapshot.DrawingPlayerChat}, InFancyUI={_inputSnapshot.InFancyUI}, " +
            $"GameMenu={_inputSnapshot.GameMenu}, ChatText=\"{_inputSnapshot.ChatText}\"]");

        Main.blockInput = true;
        Main.drawingPlayerChat = false;
        PlayerInput.WritingText = true;
        Main.clrInput();
        Main.chatRelease = false;

        log("TextInputDialog.Begin: Input state configured [blockInput=true, drawingPlayerChat=false, " +
            "WritingText=true, clrInput called, chatRelease=false]");
    }

    public TextInputDialogUpdateResult Update(int maxLength, System.Action<string> log)
    {
        if (!IsActive)
        {
            return TextInputDialogUpdateResult.None;
        }

        PlayerInput.WritingText = true;
        Main.chatRelease = false;

        string newText = Main.GetInputText(_text);
        if (maxLength > 0 && newText.Length > maxLength)
        {
            log($"TextInputDialog.Update: Text truncated from {newText.Length} to {maxLength} chars.");
            newText = newText.Substring(0, maxLength);
        }

        if (Main.inputTextEnter)
        {
            TextInputDialogUpdateResult result = TextInputDialogUpdateResult.Confirmed(newText);
            Close(log, deferRestoreUntilInventoryRelease: false);
            return result;
        }

        if (Main.inputTextEscape)
        {
            Close(log, deferRestoreUntilInventoryRelease: true);
            return TextInputDialogUpdateResult.Canceled();
        }

        if (!string.Equals(newText, _text, System.StringComparison.Ordinal))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        _text = newText;
        return TextInputDialogUpdateResult.None;
    }

    public void UpdatePendingRestore(System.Action<string> log)
    {
        if (!_restorePending)
        {
            return;
        }

        bool inventoryTriggerHeld = PlayerInput.Triggers.Current.Inventory || PlayerInput.Triggers.JustPressed.Inventory;
        bool escapeHeld = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape);
        if (inventoryTriggerHeld || escapeHeld)
        {
            Main.blockInput = true;
            PlayerInput.WritingText = false;
            Main.chatRelease = false;
            return;
        }

        RestoreInputState(log);
    }

    public void Close(System.Action<string> log, bool deferRestoreUntilInventoryRelease = false)
    {
        if (!IsActive && !_restorePending)
        {
            log("TextInputDialog.Close skipped: dialog not active.");
            return;
        }

        log($"TextInputDialog.Close: Closing dialog. FinalText=\"{_text}\", HasSnapshot={_inputSnapshot is not null}");

        IsActive = false;
        _text = string.Empty;

        Main.clrInput();

        if (deferRestoreUntilInventoryRelease)
        {
            _restorePending = true;
            Main.blockInput = true;
            PlayerInput.WritingText = false;
            Main.drawingPlayerChat = false;
            Main.chatRelease = false;
            log("TextInputDialog.Close: Deferring input restoration until inventory trigger is released.");
            return;
        }

        RestoreInputState(log);
    }

    private void RestoreInputState(System.Action<string> log)
    {
        _restorePending = false;

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
            log($"TextInputDialog.Close: Input state restored from snapshot [BlockInput={snapshot.BlockInput}, " +
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
            log("TextInputDialog.Close: No snapshot found, reset all input state to defaults.");
        }

        _inputSnapshot = null;
        log("TextInputDialog.Close: Dialog closed.");
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

internal readonly record struct TextInputDialogUpdateResult(
    TextInputDialogUpdateKind Kind,
    string Text)
{
    public static TextInputDialogUpdateResult None => new(TextInputDialogUpdateKind.None, string.Empty);

    public static TextInputDialogUpdateResult Confirmed(string text)
        => new(TextInputDialogUpdateKind.Confirmed, text);

    public static TextInputDialogUpdateResult Canceled()
        => new(TextInputDialogUpdateKind.Canceled, string.Empty);
}

internal enum TextInputDialogUpdateKind
{
    None,
    Confirmed,
    Canceled
}
