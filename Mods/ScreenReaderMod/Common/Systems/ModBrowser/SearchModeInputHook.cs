#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.ModBrowser;

/// <summary>
/// Hooks UIInputTextField.DrawSelf to conditionally prevent keyboard input capture
/// when in navigation mode in the mod browser menus.
/// </summary>
public sealed class SearchModeInputHook : ModSystem
{
    private static Type? _uiInputTextFieldType;
    private static Hook? _drawSelfHook;

    // Cached reflection handles for UIInputTextField fields
    private static FieldInfo? _hintTextField;
    private static FieldInfo? _currentStringField;
    private static FieldInfo? _textBlinkerCountField;

    // Track previous search text for keystroke sound feedback
    private static string? _previousSearchText;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Get the UIInputTextField type
        _uiInputTextFieldType = Type.GetType("Terraria.ModLoader.UI.UIInputTextField, tModLoader");
        if (_uiInputTextFieldType is null)
        {
            Mod.Logger.Warn("[SearchModeInputHook] Could not find UIInputTextField type");
            return;
        }

        // Get field info for accessing private fields
        _hintTextField = _uiInputTextFieldType.GetField("_hintText", BindingFlags.NonPublic | BindingFlags.Instance);
        _currentStringField = _uiInputTextFieldType.GetField("_currentString", BindingFlags.NonPublic | BindingFlags.Instance);
        _textBlinkerCountField = _uiInputTextFieldType.GetField("_textBlinkerCount", BindingFlags.NonPublic | BindingFlags.Instance);

        if (_hintTextField is null || _currentStringField is null || _textBlinkerCountField is null)
        {
            Mod.Logger.Warn("[SearchModeInputHook] Could not find required UIInputTextField fields");
            return;
        }

        // Get DrawSelf method
        MethodInfo? drawSelfMethod = _uiInputTextFieldType.GetMethod(
            "DrawSelf",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(SpriteBatch) },
            null);

        if (drawSelfMethod is null)
        {
            Mod.Logger.Warn("[SearchModeInputHook] Could not find UIInputTextField.DrawSelf method");
            return;
        }

        try
        {
            _drawSelfHook = new Hook(drawSelfMethod, DrawSelf_Hook);
            Mod.Logger.Info("[SearchModeInputHook] Successfully hooked UIInputTextField.DrawSelf");
        }
        catch (Exception ex)
        {
            Mod.Logger.Error($"[SearchModeInputHook] Failed to hook DrawSelf: {ex}");
        }
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        _drawSelfHook?.Dispose();
        _drawSelfHook = null;

        SearchModeManager.Reset();
    }

    private delegate void DrawSelfDelegate(UIElement self, SpriteBatch spriteBatch);

    private static void DrawSelf_Hook(DrawSelfDelegate orig, UIElement self, SpriteBatch spriteBatch)
    {
        // If not in a relevant menu, use original behavior
        if (!SearchModeManager.IsRelevantMenu)
        {
            _previousSearchText = null;
            orig(self, spriteBatch);
            return;
        }

        // If search mode is active, use original behavior but track text changes for keystroke sound
        if (SearchModeManager.IsSearchModeActive)
        {
            orig(self, spriteBatch);

            // Check for text changes and play keystroke sound
            if (_currentStringField is not null)
            {
                string? currentText = _currentStringField.GetValue(self) as string;
                if (!string.Equals(currentText, _previousSearchText, StringComparison.Ordinal))
                {
                    // Only play sound if there was previous text (not on first frame)
                    if (_previousSearchText is not null)
                    {
                        SoundEngine.PlaySound(SoundID.MenuTick);
                    }
                    _previousSearchText = currentText;
                }
            }
            return;
        }

        // In navigation mode: draw the text field without capturing keyboard input
        _previousSearchText = null;
        DrawTextFieldWithoutInputCapture(self, spriteBatch);
    }

    /// <summary>
    /// Draws the text field visually without capturing keyboard input.
    /// This replicates the drawing portion of UIInputTextField.DrawSelf
    /// but skips PlayerInput.WritingText and Main.GetInputText calls.
    /// </summary>
    private static void DrawTextFieldWithoutInputCapture(UIElement self, SpriteBatch spriteBatch)
    {
        if (_hintTextField is null || _currentStringField is null || _textBlinkerCountField is null)
        {
            return;
        }

        try
        {
            string? hintText = _hintTextField.GetValue(self) as string ?? "";
            string? currentString = _currentStringField.GetValue(self) as string ?? "";
            int textBlinkerCount = (int)(_textBlinkerCountField.GetValue(self) ?? 0);

            // Increment blinker (matching original behavior for visual consistency)
            textBlinkerCount++;
            _textBlinkerCountField.SetValue(self, textBlinkerCount);

            // Determine display text (no blinking cursor in navigation mode to indicate unfocused)
            string displayText = currentString;
            // Don't add the blinking cursor "|" in navigation mode - this visually indicates unfocused state

            // Get dimensions for drawing
            CalculatedStyle dimensions = self.GetDimensions();
            Vector2 position = new(dimensions.X, dimensions.Y);

            // Draw the text
            if (string.IsNullOrEmpty(currentString))
            {
                Utils.DrawBorderString(spriteBatch, hintText, position, Color.Gray);
            }
            else
            {
                Utils.DrawBorderString(spriteBatch, displayText, position, Color.White);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[SearchModeInputHook] Error drawing text field: {ex.Message}");
        }
    }
}
