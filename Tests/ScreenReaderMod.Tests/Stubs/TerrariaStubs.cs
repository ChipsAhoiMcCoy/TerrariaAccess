#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

// Minimal Terraria type stubs for testing purposes.
// These provide just enough API surface for the test project to compile
// source files that have Terraria dependencies.

namespace Terraria
{
    internal static class Main
    {
        public static AchievementSystem? Achievements { get; set; }
    }

    internal class AchievementSystem
    {
        private readonly Dictionary<string, Achievement> _achievements = new();

        public Achievement? GetAchievement(string name)
        {
            return _achievements.TryGetValue(name, out var achievement) ? achievement : null;
        }

        public void RegisterAchievement(string name, Achievement achievement)
        {
            _achievements[name] = achievement;
        }
    }

    internal class Achievement
    {
        public LocalizedText? FriendlyName { get; set; }
        public LocalizedText? Description { get; set; }
    }

    internal class LocalizedText
    {
        public string? Value { get; set; }

        public LocalizedText(string? value) => Value = value;
    }
}

namespace Terraria.UI.Chat
{
    internal class TextSnippet
    {
        public string Text { get; set; } = string.Empty;
    }

    internal static class ChatManager
    {
        public static List<TextSnippet>? ParseMessage(string message, Color color)
        {
            // Simple stub that returns a single text snippet
            // Real implementation parses [tags] but we don't need that for tests
            return new List<TextSnippet> { new() { Text = message } };
        }
    }
}

namespace ScreenReaderMod
{
    // Stub for the mod instance logger reference
    internal class ScreenReaderMod
    {
        public static ScreenReaderMod? Instance { get; set; }
        public ILogger? Logger { get; set; }
    }

    internal interface ILogger
    {
        void Info(string message);
    }
}
