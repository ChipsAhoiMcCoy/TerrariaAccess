#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.Localization;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed partial class InventoryNarrator
    {
        internal static string? BuildTooltipDetails(Item item, string hoverName, bool allowMouseText = true, bool suppressControllerPrompts = false)
        {
            if (item is null || item.IsAir)
            {
                return null;
            }

            HashSet<string> nameCandidates = BuildItemNameCandidates(item, hoverName);
            List<string>? lines = null;
            if (allowMouseText)
            {
                string? raw = TryGetMouseText();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    lines = ExtractTooltipLines(raw, nameCandidates, suppressControllerPrompts);
                }
            }

            if (lines is null || lines.Count == 0)
            {
                lines = ExtractTooltipLinesFromItem(item, nameCandidates, suppressControllerPrompts);
            }

            if (lines.Count == 0)
            {
                return null;
            }

            string formatted = FormatTooltipLines(lines);
            return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
        }

        private static List<string> ExtractTooltipLines(string raw, HashSet<string> nameCandidates, bool suppressControllerPrompts)
        {
            string sanitized = GlyphTagFormatter.SanitizeTooltip(raw);
            List<string> lines = new();

            string[] segments = sanitized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                string trimmed = segment.Trim();
                if (trimmed.Length > 0 &&
                    !IsItemNameLine(trimmed, nameCandidates) &&
                    (!suppressControllerPrompts || !ShouldRemoveControllerPromptLine(trimmed)))
                {
                    lines.Add(trimmed);
                }
            }

            return lines;
        }

        private static List<string> ExtractTooltipLinesFromItem(Item item, HashSet<string> nameCandidates, bool suppressControllerPrompts)
        {
            List<string> lines = new();

            try
            {
                Item clone = item.Clone();
                const int MaxLines = 60;
                string[] toolTipLine = new string[MaxLines];
                bool[] preFixLine = new bool[MaxLines];
                bool[] badPreFixLine = new bool[MaxLines];
                string[] toolTipNames = new string[MaxLines];
                int yoyoLogo = -1;
                int researchLine = -1;
                float originalKnockBack = clone.knockBack;
                int numLines = 1;

                Main.MouseText_DrawItemTooltip_GetLinesInfo(
                    clone,
                    ref yoyoLogo,
                    ref researchLine,
                    originalKnockBack,
                    ref numLines,
                    toolTipLine,
                    preFixLine,
                    badPreFixLine,
                    toolTipNames,
                    out _);

                for (int i = 0; i < numLines && i < toolTipLine.Length; i++)
                {
                    string? line = toolTipLine[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string? entryName = toolTipNames[i];
                    if (!string.IsNullOrWhiteSpace(entryName) &&
                        string.Equals(entryName, "ItemName", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string trimmed = line.Trim();
                    if (IsItemNameLine(trimmed, nameCandidates))
                    {
                        continue;
                    }

                    if (suppressControllerPrompts && ShouldRemoveControllerPromptLine(trimmed))
                    {
                        continue;
                    }

                    lines.Add(trimmed);
                }
            }
            catch
            {
                // Swallow exceptions and return whatever we have.
            }

            return lines;
        }

        private static bool IsItemNameLine(string? line, HashSet<string> nameCandidates)
        {
            if (string.IsNullOrWhiteSpace(line) || nameCandidates is null || nameCandidates.Count == 0)
            {
                return false;
            }

            string normalizedLine = GlyphTagFormatter.Normalize(line).Trim();
            return nameCandidates.Contains(normalizedLine);
        }

        private static bool ShouldRemoveControllerPromptLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string normalized = GlyphTagFormatter.Normalize(line).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string lower = normalized.ToLowerInvariant();
            if (!lower.Contains("craft", StringComparison.Ordinal))
            {
                return false;
            }

            if (lower.Contains("right bumper", StringComparison.Ordinal) ||
                lower.Contains("left bumper", StringComparison.Ordinal) ||
                lower.Contains("right trigger", StringComparison.Ordinal) ||
                lower.Contains("left trigger", StringComparison.Ordinal) ||
                lower.Contains("button", StringComparison.Ordinal) ||
                lower.Contains("bumper", StringComparison.Ordinal) ||
                lower.Contains("trigger", StringComparison.Ordinal) ||
                lower.Contains("gamepad", StringComparison.Ordinal) ||
                lower.Contains("controller", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static HashSet<string> BuildItemNameCandidates(Item item, string hoverName)
        {
            HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidates, NarrationTextFormatter.ComposeItemName(item));
            AddCandidate(candidates, hoverName);
            AddCandidate(candidates, item.Name);
            AddCandidate(candidates, item.AffixName());
            AddCandidate(candidates, Lang.GetItemNameValue(item.type));
            return candidates;
        }

        private static void AddCandidate(HashSet<string> candidates, string? value)
        {
            if (candidates is null)
            {
                return;
            }

            string normalized = GlyphTagFormatter.NormalizeNameCandidate(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }

        private static string FormatTooltipLines(List<string> lines)
        {
            StringBuilder builder = new();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    if (line.EndsWith(":", StringComparison.Ordinal))
                    {
                        builder.Append(line);
                        if (i + 1 < lines.Count)
                        {
                            string next = lines[++i];
                            if (!string.IsNullOrWhiteSpace(next))
                            {
                                builder.Append(' ');
                                builder.Append(next);
                                if (!NarrationTextFormatter.HasTerminalPunctuation(next))
                                {
                                    builder.Append('.');
                                }
                            }
                        }

                    continue;
                }

                builder.Append(line);
                if (!NarrationTextFormatter.HasTerminalPunctuation(line))
                {
                    builder.Append('.');
                }
            }

            string result = builder.ToString();
            return GlyphTagFormatter.Normalize(result);
        }
    }
}
