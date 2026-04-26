#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Recovers the vanilla Enter-to-chat path if a text input owner survives after
/// returning to ordinary gameplay.
/// </summary>
public sealed class ChatOpenRecoverySystem : ModSystem
{
    private bool _enterWasPressed;

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        KeyboardState keyboard = Keyboard.GetState();
        bool enterPressed = keyboard.IsKeyDown(Keys.Enter);
        bool enterJustPressed = enterPressed && !_enterWasPressed;
        _enterWasPressed = enterPressed;

        ClearStaleGameplayTextInputOwner();
        if (RecoverGameplayChatClose(enterJustPressed, keyboard))
        {
            return;
        }

        RecoverGameplayChatOpen(enterJustPressed, keyboard);
    }

    private static void ClearStaleGameplayTextInputOwner()
    {
        if (Main.CurrentInputTextTakerOverride is null && !PlayerInput.WritingText)
        {
            return;
        }

        if (!IsPlainGameplayContext())
        {
            return;
        }

        Main.CurrentInputTextTakerOverride = null;
        PlayerInput.WritingText = false;
        TerrariaAccess.Instance?.Logger.Info("[ChatOpenRecovery] Cleared stale text input owner during gameplay.");
    }

    private static bool RecoverGameplayChatClose(bool enterJustPressed, KeyboardState keyboard)
    {
        if (!enterJustPressed || !Main.drawingPlayerChat)
        {
            return false;
        }

        if (!CanSubmitOpenGameplayChat(keyboard))
        {
            return false;
        }

        SubmitOpenChatText();
        Main.chatText = string.Empty;
        Main.ClosePlayerChat();
        Main.chatRelease = false;
        SoundEngine.PlaySound(SoundID.MenuClose);
        TerrariaAccess.Instance?.Logger.Info("[ChatOpenRecovery] Closed gameplay chat from Enter fallback.");
        return true;
    }

    private static void RecoverGameplayChatOpen(bool enterJustPressed, KeyboardState keyboard)
    {
        if (!enterJustPressed || Main.chatRelease)
        {
            return;
        }

        if (!CanVanillaOpenGameplayChat(keyboard))
        {
            return;
        }

        PlayerInput.CurrentInputMode = InputMode.Keyboard;
        PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
        SoundEngine.PlaySound(SoundID.MenuOpen);
        Main.OpenPlayerChat();
        Main.chatText = string.Empty;
        Main.chatRelease = false;
        TerrariaAccess.Instance?.Logger.Info("[ChatOpenRecovery] Opened gameplay chat after stale chatRelease blocked Enter.");
    }

    private static bool CanVanillaOpenGameplayChat(KeyboardState keyboard)
    {
        if (!IsPlainGameplayContext())
        {
            return false;
        }

        if (Main.CurrentInputTextTakerOverride is not null)
        {
            return false;
        }

        if (!Main.hasFocus)
        {
            return false;
        }

        if (keyboard.IsKeyDown(Keys.LeftAlt) ||
            keyboard.IsKeyDown(Keys.RightAlt) ||
            keyboard.IsKeyDown(Keys.Escape))
        {
            return false;
        }

        return true;
    }

    private static bool CanSubmitOpenGameplayChat(KeyboardState keyboard)
    {
        if (Main.CurrentInputTextTakerOverride is not null)
        {
            return false;
        }

        if (!Main.hasFocus)
        {
            return false;
        }

        if (Main.gameMenu || Main.blockInput || Main.editSign || Main.editChest)
        {
            return false;
        }

        if (keyboard.IsKeyDown(Keys.LeftAlt) ||
            keyboard.IsKeyDown(Keys.RightAlt) ||
            keyboard.IsKeyDown(Keys.Escape))
        {
            return false;
        }

        return true;
    }

    private static void SubmitOpenChatText()
    {
        string chatText = Main.chatText ?? string.Empty;
        if (chatText == string.Empty)
        {
            return;
        }

        ChatMessage message = ChatManager.Commands.CreateOutgoingMessage(chatText);
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            ChatHelper.SendChatMessageFromClient(message);
            return;
        }

        if (Main.netMode == NetmodeID.SinglePlayer)
        {
            ChatManager.Commands.ProcessIncomingMessage(message, Main.myPlayer);
        }
    }

    private static bool IsPlainGameplayContext()
    {
        if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return false;
        }

        if (Main.gameMenu ||
            Main.playerInventory ||
            Main.inFancyUI ||
            Main.ingameOptionsWindow ||
            Main.blockInput)
        {
            return false;
        }

        if (Main.InGameUI?.IsVisible ?? false)
        {
            return false;
        }

        return true;
    }
}
