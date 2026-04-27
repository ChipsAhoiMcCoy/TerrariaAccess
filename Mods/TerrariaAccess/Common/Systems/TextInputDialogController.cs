#nullable enable
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Microsoft.Xna.Framework.Input;

namespace TerrariaAccess.Common.Systems;

internal sealed class TextInputDialogController
{
    private InputSnapshot? _inputSnapshot;
    private string _text = string.Empty;
    private bool _restorePending;
    private KeyboardState _previousKeyboardState;

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
            ChatText = Main.chatText ?? string.Empty,
            InputTextTakerOverride = Main.CurrentInputTextTakerOverride
        };

        log($"TextInputDialog.Begin: InputSnapshot saved [BlockInput={_inputSnapshot.BlockInput}, " +
            $"WritingText={_inputSnapshot.WritingText}, PlayerInventory={_inputSnapshot.PlayerInventory}, " +
            $"EditSign={_inputSnapshot.EditSign}, EditChest={_inputSnapshot.EditChest}, " +
            $"DrawingPlayerChat={_inputSnapshot.DrawingPlayerChat}, InFancyUI={_inputSnapshot.InFancyUI}, " +
            $"GameMenu={_inputSnapshot.GameMenu}, ChatText=\"{_inputSnapshot.ChatText}\"]");

        Main.blockInput = true;
        Main.drawingPlayerChat = false;
        Main.CurrentInputTextTakerOverride = this;
        PlayerInput.CurrentInputMode = InputMode.Keyboard;
        SetWritingText(true);
        Main.clrInput();
        Main.chatRelease = false;
        _previousKeyboardState = Keyboard.GetState();

        log("TextInputDialog.Begin: Input state configured [blockInput=true, drawingPlayerChat=false, " +
            "WritingText=true, clrInput called, chatRelease=false]");
    }

    public TextInputDialogUpdateResult Update(int maxLength, System.Action<string> log)
    {
        if (!IsActive)
        {
            return TextInputDialogUpdateResult.None;
        }

        SetWritingText(true);
        Main.CurrentInputTextTakerOverride = this;
        PlayerInput.CurrentInputMode = InputMode.Keyboard;
        Main.blockInput = true;
        Main.chatRelease = false;

        KeyboardState keyboard = Keyboard.GetState();
        bool enterJustPressed = IsJustPressed(keyboard, Keys.Enter);
        bool escapeJustPressed = IsJustPressed(keyboard, Keys.Escape);

        string newText = Main.GetInputText(_text);
        if (maxLength > 0 && newText.Length > maxLength)
        {
            log($"TextInputDialog.Update: Text truncated from {newText.Length} to {maxLength} chars.");
            newText = newText.Substring(0, maxLength);
        }

        if (!string.Equals(newText, _text, System.StringComparison.Ordinal))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
        }
        else if (TryApplyRawKeyboardInput(keyboard, _text, maxLength, out string rawText))
        {
            newText = rawText;
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        _previousKeyboardState = keyboard;

        if (Main.inputTextEnter || enterJustPressed)
        {
            TextInputDialogUpdateResult result = TextInputDialogUpdateResult.Confirmed(newText);
            Close(log, deferRestoreUntilInventoryRelease: false);
            return result;
        }

        if (Main.inputTextEscape || escapeJustPressed)
        {
            Close(log, deferRestoreUntilInventoryRelease: true);
            return TextInputDialogUpdateResult.Canceled();
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
            SetWritingText(false);
            if (ReferenceEquals(Main.CurrentInputTextTakerOverride, this))
            {
                Main.CurrentInputTextTakerOverride = null;
            }
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
            SetWritingText(false);
            if (ReferenceEquals(Main.CurrentInputTextTakerOverride, this))
            {
                Main.CurrentInputTextTakerOverride = null;
            }
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
            SetWritingText(snapshot.WritingText);
            Main.blockInput = snapshot.BlockInput;
            Main.playerInventory = snapshot.PlayerInventory;
            Main.editSign = snapshot.EditSign;
            Main.editChest = snapshot.EditChest;
            Main.drawingPlayerChat = snapshot.DrawingPlayerChat;
            Main.inFancyUI = snapshot.InFancyUI;
            Main.gameMenu = snapshot.GameMenu;
            Main.chatText = snapshot.ChatText;
            if (ReferenceEquals(Main.CurrentInputTextTakerOverride, this) ||
                Main.CurrentInputTextTakerOverride is null)
            {
                Main.CurrentInputTextTakerOverride = snapshot.InputTextTakerOverride;
            }
            log($"TextInputDialog.Close: Input state restored from snapshot [BlockInput={snapshot.BlockInput}, " +
                $"WritingText={snapshot.WritingText}, PlayerInventory={snapshot.PlayerInventory}, " +
                $"DrawingPlayerChat={snapshot.DrawingPlayerChat}]");
        }
        else
        {
            SetWritingText(false);
            Main.blockInput = false;
            Main.playerInventory = false;
            Main.editSign = false;
            Main.editChest = false;
            Main.drawingPlayerChat = false;
            Main.inFancyUI = false;
            Main.gameMenu = false;
            Main.chatText = string.Empty;
            if (ReferenceEquals(Main.CurrentInputTextTakerOverride, this))
            {
                Main.CurrentInputTextTakerOverride = null;
            }
            log("TextInputDialog.Close: No snapshot found, reset all input state to defaults.");
        }

        _inputSnapshot = null;
        log("TextInputDialog.Close: Dialog closed.");
    }

    private bool IsJustPressed(KeyboardState keyboard, Keys key)
        => keyboard.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);

    private bool TryApplyRawKeyboardInput(KeyboardState keyboard, string oldText, int maxLength, out string newText)
    {
        newText = oldText;
        foreach (Keys key in keyboard.GetPressedKeys())
        {
            if (!IsJustPressed(keyboard, key))
            {
                continue;
            }

            if (key == Keys.Back)
            {
                if (newText.Length > 0)
                {
                    newText = newText[..^1];
                }

                continue;
            }

            if (TryGetPrintableCharacter(keyboard, key, out char character))
            {
                if (maxLength <= 0 || newText.Length < maxLength)
                {
                    newText += character;
                }
            }
        }

        return !string.Equals(newText, oldText, System.StringComparison.Ordinal);
    }

    private static bool TryGetPrintableCharacter(KeyboardState keyboard, Keys key, out char character)
    {
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        character = '\0';

        int keyValue = (int)key;

        if (keyValue >= (int)Keys.A && keyValue <= (int)Keys.Z)
        {
            char letter = (char)('a' + keyValue - (int)Keys.A);
            character = shift ? char.ToUpperInvariant(letter) : letter;
            return true;
        }

        if (keyValue >= (int)Keys.D0 && keyValue <= (int)Keys.D9)
        {
            string normal = "0123456789";
            string shifted = ")!@#$%^&*(";
            int index = keyValue - (int)Keys.D0;
            character = shift ? shifted[index] : normal[index];
            return true;
        }

        if (keyValue >= (int)Keys.NumPad0 && keyValue <= (int)Keys.NumPad9)
        {
            character = (char)('0' + keyValue - (int)Keys.NumPad0);
            return true;
        }

        character = key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPlus => shift ? '+' : '=',
            Keys.OemOpenBrackets => shift ? '{' : '[',
            Keys.OemCloseBrackets => shift ? '}' : ']',
            Keys.OemPipe => shift ? '|' : '\\',
            Keys.OemSemicolon => shift ? ':' : ';',
            Keys.OemQuotes => shift ? '"' : '\'',
            Keys.OemComma => shift ? '<' : ',',
            Keys.OemPeriod => shift ? '>' : '.',
            Keys.OemQuestion => shift ? '?' : '/',
            Keys.OemTilde => shift ? '~' : '`',
            Keys.Decimal => '.',
            Keys.Add => '+',
            Keys.Subtract => '-',
            Keys.Multiply => '*',
            Keys.Divide => '/',
            _ => '\0'
        };

        return character != '\0';
    }

    private static void SetWritingText(bool writingText)
    {
        PlayerInput.WritingText = writingText;
        Main.instance.HandleIME();
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
        public object? InputTextTakerOverride;
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
