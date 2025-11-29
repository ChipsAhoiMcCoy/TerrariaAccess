#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class CraftingNarrator
    {
        private RecipeAnnouncement? _lastAnnouncement;
        private static RecipeFocus? _hoveredFocusOverride;
        private static uint _hoveredFocusFrame;
        private static int _recipeLookupVersion = -1;
        private static Dictionary<Item, int>? _recipeResultLookup;
        private readonly HashSet<int> _missingRequirementRecipes = new();

        private static readonly Lazy<Dictionary<int, int>> RecipeGroupLookup = new(DiscoverRecipeGroupLookup);
        private static readonly Func<Recipe, int, int>? AcceptedGroupResolver = CreateAcceptedGroupResolver();

        private static bool _loggedRecipeGroupReflectionWarning;
        private static bool _loggedRecipeFlagReflectionWarning;

        private readonly struct RecipeFocus
        {
            public RecipeFocus(Recipe recipe, int recipeIndex, int focusIndex, int availableCount)
            {
                Recipe = recipe;
                RecipeIndex = recipeIndex;
                FocusIndex = focusIndex;
                AvailableCount = availableCount;
            }

            public Recipe Recipe { get; }
            public int RecipeIndex { get; }
            public int FocusIndex { get; }
            public int AvailableCount { get; }
            public Item Result => Recipe.createItem;
        }

        private readonly struct RecipeIdentity : IEquatable<RecipeIdentity>
        {
            public RecipeIdentity(int type, int prefix, int stack)
            {
                Type = type;
                Prefix = prefix;
                Stack = stack;
            }

            public int Type { get; }
            public int Prefix { get; }
            public int Stack { get; }

            public static RecipeIdentity From(Item item)
            {
                if (item is null || item.IsAir)
                {
                    return default;
                }

                return new RecipeIdentity(item.type, item.prefix, item.stack);
            }

            public bool Equals(RecipeIdentity other)
            {
                return Type == other.Type &&
                       Prefix == other.Prefix &&
                       Stack == other.Stack;
            }

            public override bool Equals(object? obj)
            {
                return obj is RecipeIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Type, Prefix, Stack);
            }
        }

        private readonly record struct RecipeAnnouncement(
            RecipeIdentity Identity,
            int RecipeIndex,
            int FocusIndex,
            int AvailableCount,
            string Message);

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            public static ReferenceEqualityComparer<T> Instance { get; } = new();

            public bool Equals(T? x, T? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly string[] NeedWaterMembers = { "needWater", "_needWater", "NeedWater" };
        private static readonly string[] NeedHoneyMembers = { "needHoney", "_needHoney", "NeedHoney" };
        private static readonly string[] NeedLavaMembers = { "needLava", "_needLava", "NeedLava" };
        private static readonly string[] NeedSnowBiomeMembers = { "needSnowBiome", "_needSnowBiome", "NeedSnowBiome" };
        private static readonly string[] NeedGraveyardBiomeMembers = { "needGraveyardBiome", "_needGraveyardBiome", "NeedGraveyardBiome" };
        private static readonly string[] AnyIronBarMembers = { "anyIronBar", "_anyIronBar", "AnyIronBar" };
        private static readonly string[] AnyWoodMembers = { "anyWood", "_anyWood", "AnyWood" };
        private static readonly string[] AnySandMembers = { "anySand", "_anySand", "AnySand" };
        private static readonly string[] AnyFragmentMembers = { "anyFragment", "_anyFragment", "AnyFragment" };
        private static readonly string[] AnyPressurePlateMembers = { "anyPressurePlate", "_anyPressurePlate", "AnyPressurePlate" };

        private static bool TryGetFocusedRecipe(out Recipe recipe, out int recipeIndex, out int focusIndex, out int availableCount)
        {
            recipe = null!;
            recipeIndex = -1;
            focusIndex = -1;
            availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
            if (availableCount <= 0)
            {
                return false;
            }

            focusIndex = Utils.Clamp(Main.focusRecipe, 0, availableCount - 1);
            return TryGetRecipeEntry(focusIndex, availableCount, out recipe, out recipeIndex);
        }

        private static bool TryGetRecipeEntry(int focusIndex, int availableCount, out Recipe recipe, out int recipeIndex)
        {
            recipe = null!;
            recipeIndex = -1;

            if (focusIndex < 0 || focusIndex >= availableCount || focusIndex >= Main.availableRecipe.Length)
            {
                return false;
            }

            recipeIndex = Main.availableRecipe[focusIndex];
            if (recipeIndex < 0 || recipeIndex >= Main.recipe.Length)
            {
                return false;
            }

            Recipe candidate = Main.recipe[recipeIndex];
            if (candidate is null || candidate.createItem is null || candidate.createItem.IsAir)
            {
                return false;
            }

            recipe = candidate;
            return true;
        }

        private static bool TryResolveRecipeFocus(Item item, out RecipeFocus focus)
        {
            focus = default;
            RecipeIdentity identity = RecipeIdentity.From(item);
            if (identity.Type <= 0)
            {
                return false;
            }

            return TryFindRecipeFocus(identity, out focus);
        }

        private static bool TryFindRecipeFocus(RecipeIdentity identity, out RecipeFocus focus)
        {
            focus = default;
            if (identity.Type <= 0)
            {
                return false;
            }

            int availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
            if (availableCount <= 0)
            {
                return false;
            }

            for (int i = 0; i < availableCount; i++)
            {
                if (!TryGetRecipeEntry(i, availableCount, out Recipe recipe, out int recipeIndex))
                {
                    continue;
                }

                RecipeIdentity candidateIdentity = RecipeIdentity.From(recipe.createItem);
                if (!candidateIdentity.Equals(identity))
                {
                    continue;
                }

                focus = new RecipeFocus(recipe, recipeIndex, i, availableCount);
                return true;
            }

            return false;
        }

        private static bool TryResolveRecipeFocusFromReference(Item item, out RecipeFocus focus)
        {
            focus = default;
            if (!TryGetRecipeIndexForResultItem(item, out int recipeIndex))
            {
                return false;
            }

            return TryCreateFocusFromRecipeIndex(recipeIndex, out focus);
        }

        internal static bool TryCaptureHoveredRecipe(Item item)
        {
            if (!Main.playerInventory)
            {
                return false;
            }

            if (!TryResolveRecipeFocusFromReference(item, out RecipeFocus focus) &&
                !TryResolveRecipeFocus(item, out focus))
            {
                return false;
            }

            RegisterHoveredFocus(focus);
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
            return true;
        }

        internal static bool TryFocusRecipeAtAvailableIndex(int availableIndex)
        {
            if (!TryCreateFocusFromAvailableIndex(availableIndex, out RecipeFocus focus))
            {
                return false;
            }

            RegisterHoveredFocus(focus);
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
            return true;
        }

        internal static string? TryGetRequirementTooltipDetails(Item item, bool locationIsEmpty)
        {
            if (item is null || item.IsAir)
            {
                return null;
            }

            if (!locationIsEmpty)
            {
                return null;
            }

            if (!Main.playerInventory)
            {
                return null;
            }

            if (!Main.mouseItem.IsAir)
            {
                return null;
            }

            if (!TryResolveRecipeFocus(item, out RecipeFocus focus))
            {
                return null;
            }

            string? message = BuildRequirementMessage(focus.Recipe, out _);
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }

        internal static void TryCaptureRecipeHover(Item item, int context)
        {
            if (item is null)
            {
                return;
            }

            UiNarrationArea area = ItemSlotContextFacts.ResolveArea(context);
            if ((area & (UiNarrationArea.Crafting | UiNarrationArea.Guide)) == 0)
            {
                return;
            }

            if (!TryResolveRecipeFocusFromReference(item, out RecipeFocus focus) &&
                !TryResolveRecipeFocus(item, out focus))
            {
                return;
            }

            RegisterHoveredFocus(focus);
            UiAreaNarrationContext.RecordArea(area);
        }

        public void Update(Player player)
        {
            if (Main.ingameOptionsWindow)
            {
                Reset();
                return;
            }

            if (!InventoryNarrator.IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            if (!UiAreaNarrationContext.IsActiveArea(UiNarrationArea.Crafting | UiNarrationArea.Guide))
            {
                ResetFocus();
                return;
            }

            if (PlayerInput.UsingGamepadUI &&
                InventoryNarrator.TryGetContextForLinkPoint(UILinkPointNavigator.CurrentPoint, out int context) &&
                !ItemSlotContextFacts.IsCraftingContext(context))
            {
                ResetFocus();
                return;
            }

            if (!TryCaptureFocus(out RecipeFocus focus))
            {
                ResetFocus();
                return;
            }

            if (!TryBuildAnnouncement(focus, out RecipeAnnouncement announcement))
            {
                return;
            }

            if (_lastAnnouncement.HasValue && _lastAnnouncement.Value.Equals(announcement))
            {
                return;
            }

            _lastAnnouncement = announcement;
            ScreenReaderService.Announce(announcement.Message, force: true);
        }

        private static bool TryCaptureFocus(out RecipeFocus focus)
        {
            if (TryGetActiveHoveredFocus(out focus))
            {
                return true;
            }

            if (!TryGetFocusedRecipe(out Recipe recipe, out int recipeIndex, out int focusIndex, out int available))
            {
                focus = default;
                return false;
            }

            focus = new RecipeFocus(recipe, recipeIndex, focusIndex, available);
            return true;
        }

        private static bool TryCreateFocusFromAvailableIndex(int availableIndex, out RecipeFocus focus)
        {
            focus = default;
            int availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
            if (!TryGetRecipeEntry(availableIndex, availableCount, out Recipe recipe, out int recipeIndex))
            {
                return false;
            }

            focus = new RecipeFocus(recipe, recipeIndex, availableIndex, availableCount);
            return true;
        }

        private static bool TryCreateFocusFromRecipeIndex(int recipeIndex, out RecipeFocus focus)
        {
            focus = default;
            if (recipeIndex < 0)
            {
                return false;
            }

            Recipe[]? recipes = Main.recipe;
            if (recipes is null || recipeIndex >= recipes.Length)
            {
                return false;
            }

            Recipe recipe = recipes[recipeIndex];
            if (recipe is null || recipe.createItem is null || recipe.createItem.IsAir)
            {
                return false;
            }

            int available = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
            if (available <= 0)
            {
                return false;
            }

            for (int i = 0; i < available; i++)
            {
                if (Main.availableRecipe[i] != recipeIndex)
                {
                    continue;
                }

                focus = new RecipeFocus(recipe, recipeIndex, i, available);
                return true;
            }

            return false;
        }

        private static bool TryGetActiveHoveredFocus(out RecipeFocus focus)
        {
            focus = default;
            if (!_hoveredFocusOverride.HasValue)
            {
                return false;
            }

            uint current = Main.GameUpdateCount;
            uint frame = _hoveredFocusFrame;
            uint age = current >= frame ? current - frame : uint.MaxValue - frame + current + 1;
            if (age > 10)
            {
                _hoveredFocusOverride = null;
                _hoveredFocusFrame = 0;
                return false;
            }

            focus = _hoveredFocusOverride.Value;
            if (Main.focusRecipe == focus.FocusIndex)
            {
                _hoveredFocusOverride = null;
                _hoveredFocusFrame = 0;
            }

            return true;
        }

        private static void RegisterHoveredFocus(in RecipeFocus focus)
        {
            if (_hoveredFocusOverride.HasValue &&
                _hoveredFocusOverride.Value.RecipeIndex == focus.RecipeIndex &&
                _hoveredFocusOverride.Value.FocusIndex == focus.FocusIndex)
            {
                _hoveredFocusFrame = Main.GameUpdateCount;
                return;
            }

            _hoveredFocusOverride = focus;
            _hoveredFocusFrame = Main.GameUpdateCount;
        }

        private bool TryBuildAnnouncement(in RecipeFocus focus, out RecipeAnnouncement announcement)
        {
            announcement = default;

            Item result = focus.Result;
            if (result is null || result.IsAir)
            {
                return false;
            }

            string label = ComposeItemLabel(result);

            string? details = InventoryNarrator.BuildTooltipDetails(
                result,
                result.Name ?? string.Empty,
                allowMouseText: false,
                suppressControllerPrompts: true);

            string? requirementMessage = BuildRequirementMessage(focus.Recipe, out bool hadRequirementData);
            if (!string.IsNullOrWhiteSpace(requirementMessage))
            {
                details = string.IsNullOrWhiteSpace(details)
                    ? requirementMessage
                    : $"{details}. {requirementMessage}";
                _missingRequirementRecipes.Remove(focus.RecipeIndex);
            }
            else if (hadRequirementData)
            {
                LogMissingRequirementNarration(focus.RecipeIndex, label);
            }

            string combined = InventoryNarrator.CombineItemAnnouncement(label, details);
            if (string.IsNullOrWhiteSpace(combined))
            {
                combined = label;
            }

            string message = $"{combined}. Recipe {focus.FocusIndex + 1} of {focus.AvailableCount}";
            message = GlyphTagFormatter.Normalize(message);

            announcement = new RecipeAnnouncement(
                RecipeIdentity.From(result),
                focus.RecipeIndex,
                focus.FocusIndex,
                focus.AvailableCount,
                message);

            return true;
        }

        private void LogMissingRequirementNarration(int recipeIndex, string label)
        {
            if (_missingRequirementRecipes.Add(recipeIndex))
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[CraftingNarrator] Requirement narration missing for recipe {recipeIndex} ({label})");
            }
        }

        private static bool TryGetRecipeIndexForResultItem(Item item, out int recipeIndex)
        {
            recipeIndex = -1;
            if (item is null)
            {
                return false;
            }

            EnsureRecipeLookups();
            return _recipeResultLookup is not null && _recipeResultLookup.TryGetValue(item, out recipeIndex);
        }

        private static void EnsureRecipeLookups()
        {
            int version = Recipe.numRecipes;
            Recipe[]? recipes = Main.recipe;
            if (recipes is null)
            {
                _recipeLookupVersion = -1;
                _recipeResultLookup = null;
                return;
            }

            if (_recipeLookupVersion == version &&
                _recipeResultLookup is not null)
            {
                return;
            }

            Dictionary<Item, int> resultLookup = new(ReferenceEqualityComparer<Item>.Instance);

            int totalRecipes = Math.Min(version, recipes.Length);
            for (int i = 0; i < totalRecipes; i++)
            {
                Recipe recipe = recipes[i];
                if (recipe is null)
                {
                    continue;
                }

                Item result = recipe.createItem;
                if (result is not null && !result.IsAir)
                {
                    resultLookup[result] = i;
                }
            }

            _recipeResultLookup = resultLookup;
            _recipeLookupVersion = version;
        }

        private void Reset()
        {
            ResetFocus();
            _missingRequirementRecipes.Clear();
        }

        private void ResetFocus()
        {
            _lastAnnouncement = null;
            _hoveredFocusOverride = null;
            _hoveredFocusFrame = 0;
        }

        private static string? BuildRequirementMessage(Recipe recipe, out bool hadRequirements)
        {
            hadRequirements = false;
            if (recipe is null)
            {
                return null;
            }

            List<string> ingredientParts = BuildIngredientRequirementParts(recipe);
            List<string> stationParts = BuildStationRequirementParts(recipe);

            hadRequirements = ingredientParts.Count > 0 || stationParts.Count > 0;
            if (!hadRequirements)
            {
                return null;
            }

            List<string> segments = new();
            if (ingredientParts.Count > 0)
            {
                segments.Add($"Requires {string.Join(", ", ingredientParts)}");
            }

            if (stationParts.Count > 0)
            {
                string prefix = TextSanitizer.Clean(Lang.inter[22].Value);
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    prefix = "Required objects:";
                }

                segments.Add($"{prefix} {string.Join(", ", stationParts)}");
            }

            string message = string.Join(". ", segments);
            return GlyphTagFormatter.Normalize(message);
        }

        private static List<string> BuildIngredientRequirementParts(Recipe recipe)
        {
            var parts = new List<string>();
            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is null || requiredItems.Count == 0)
            {
                return parts;
            }

            for (int i = 0; i < requiredItems.Count; i++)
            {
                Item ingredient = requiredItems[i];
                if (ingredient is null)
                {
                    continue;
                }

                if (ingredient.type == 0)
                {
                    break;
                }

                if (ingredient.IsAir || ingredient.stack <= 0)
                {
                    continue;
                }

                string? description = DescribeRequirement(recipe, ingredient, i);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    parts.Add(description);
                }
            }

            return parts;
        }

        private static List<string> BuildStationRequirementParts(Recipe recipe)
        {
            var results = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddUnique(string? value)
            {
                string cleaned = TextSanitizer.Clean(value);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    return;
                }

                string normalized = GlyphTagFormatter.Normalize(cleaned);
                if (unique.Add(normalized))
                {
                    results.Add(normalized);
                }
            }

            IList<int>? requiredTiles = recipe.requiredTile;
            if (requiredTiles is not null)
            {
                for (int i = 0; i < requiredTiles.Count; i++)
                {
                    int tileId = requiredTiles[i];
                    if (tileId == -1)
                    {
                        break;
                    }

                    AddUnique(ResolveRequiredTileLabel(tileId));
                }
            }

            if (TryGetRecipeBool(recipe, NeedWaterMembers, "needWater", out bool needWater) && needWater)
            {
                AddUnique(Lang.inter[53].Value);
            }

            if (TryGetRecipeBool(recipe, NeedHoneyMembers, "needHoney", out bool needHoney) && needHoney)
            {
                AddUnique(Lang.inter[58].Value);
            }

            if (TryGetRecipeBool(recipe, NeedLavaMembers, "needLava", out bool needLava) && needLava)
            {
                AddUnique(Lang.inter[56].Value);
            }

            if (TryGetRecipeBool(recipe, NeedSnowBiomeMembers, "needSnowBiome", out bool needSnow) && needSnow)
            {
                AddUnique(Lang.inter[123].Value);
            }

            if (TryGetRecipeBool(recipe, NeedGraveyardBiomeMembers, "needGraveyardBiome", out bool needGraveyard) && needGraveyard)
            {
                AddUnique(Lang.inter[124].Value);
            }

            if (recipe.Conditions is not null)
            {
                foreach (Condition condition in recipe.Conditions)
                {
                    AddUnique(condition?.Description?.Value);
                }
            }

            return results;
        }

        private static string? ResolveRequiredTileLabel(int tileId)
        {
            if (tileId < 0)
            {
                return null;
            }

            try
            {
                int style = Recipe.GetRequiredTileStyle(tileId);
                int lookup = MapHelper.TileToLookup(tileId, style);
                string? mapObjectName = Lang.GetMapObjectName(lookup);
                if (!string.IsNullOrWhiteSpace(mapObjectName))
                {
                    return TextSanitizer.Clean(mapObjectName);
                }
            }
            catch
            {
                // Ignore lookup failures and fall back to other names.
            }

            string? tileName = TileID.Search.GetName(tileId);
            if (!string.IsNullOrWhiteSpace(tileName))
            {
                return TextSanitizer.Clean(tileName);
            }

            return $"Tile {tileId}";
        }

        private static bool TryGetRecipeBool(Recipe recipe, string[] memberNames, string debugName, out bool value)
        {
            value = false;
            if (recipe is null)
            {
                return false;
            }

            Type type = recipe.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (string memberName in memberNames)
            {
                FieldInfo? field = type.GetField(memberName, flags);
                if (field is not null)
                {
                    try
                    {
                        object? result = field.GetValue(recipe);
                        if (result is bool boolValue)
                        {
                            value = boolValue;
                            return true;
                        }
                    }
                    catch (Exception ex) when (!_loggedRecipeFlagReflectionWarning)
                    {
                        _loggedRecipeFlagReflectionWarning = true;
                        ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to read recipe field {memberName}: {ex}");
                    }

                    return false;
                }

                PropertyInfo? property = type.GetProperty(memberName, flags);
                if (property is not null && property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(bool))
                {
                    try
                    {
                        object? result = property.GetValue(recipe);
                        if (result is bool boolValue)
                        {
                            value = boolValue;
                            return true;
                        }
                    }
                    catch (Exception ex) when (!_loggedRecipeFlagReflectionWarning)
                    {
                        _loggedRecipeFlagReflectionWarning = true;
                        ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to read recipe property {memberName}: {ex}");
                    }

                    return false;
                }
            }

            if (!_loggedRecipeFlagReflectionWarning)
            {
                _loggedRecipeFlagReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Debug($"[CraftingNarrator] Unable to locate recipe flag members for {debugName}");
            }

            return false;
        }

        private static string? DescribeRequirement(Recipe recipe, Item ingredient, int index)
        {
            if (recipe is null ||
                ingredient is null ||
                ingredient.type == 0 ||
                ingredient.stack <= 0 ||
                ingredient.IsAir)
            {
                return null;
            }

            string? label = ResolveRequirementLabel(recipe, ingredient, index);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Item {ingredient.type}";
            }

            string sanitized = GlyphTagFormatter.Normalize(label);
            int stack = Math.Max(1, ingredient.stack);
            return stack > 1 ? $"{stack} {sanitized}" : sanitized;
        }

        private static string? ResolveRequirementLabel(Recipe recipe, Item ingredient, int index)
        {
            if (recipe is null || ingredient is null)
            {
                return null;
            }

            if (TryResolveProcessGroupLabel(recipe, ingredient, out string? groupLabel))
            {
                return groupLabel;
            }

            string? anyLabel = ResolveAnyRequirementLabel(recipe, ingredient);
            if (!string.IsNullOrWhiteSpace(anyLabel))
            {
                return anyLabel;
            }

            int groupId = GetAcceptedGroupId(recipe, index);
            if (groupId >= 0)
            {
                try
                {
                    Dictionary<int, RecipeGroup>? groups = RecipeGroup.recipeGroups;
                    if (groups is not null && groups.TryGetValue(groupId, out RecipeGroup? group) && group is not null)
                    {
                        string? value = group.GetText();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return TextSanitizer.Clean(value);
                        }
                    }
                }
                catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
                {
                    _loggedRecipeGroupReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to resolve recipe group {groupId}: {ex}");
                }
            }

            string name = TextSanitizer.Clean(ingredient.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = TextSanitizer.Clean(Lang.GetItemNameValue(ingredient.type));
            }

            return name;
        }

        private static bool TryResolveProcessGroupLabel(Recipe recipe, Item ingredient, out string? label)
        {
            label = null;
            try
            {
                if (recipe.ProcessGroupsForText(ingredient.type, out string? text) && !string.IsNullOrWhiteSpace(text))
                {
                    label = TextSanitizer.Clean(text);
                    return true;
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to process recipe group text: {ex}");
            }

            return false;
        }

        private static string? ResolveAnyRequirementLabel(Recipe recipe, Item ingredient)
        {
            string prefix = TextSanitizer.Clean(Lang.misc[37].Value);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "Any";
            }

            static string CombineAnyLabel(string prefix, string? suffix)
            {
                string cleanedSuffix = TextSanitizer.Clean(suffix);
                if (string.IsNullOrWhiteSpace(cleanedSuffix))
                {
                    return prefix;
                }

                return $"{prefix} {cleanedSuffix}";
            }

            if (TryGetRecipeBool(recipe, AnyIronBarMembers, "anyIronBar", out bool anyIronBar) && anyIronBar && ingredient.type == ItemID.IronBar)
            {
                return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.IronBar));
            }

            if (TryGetRecipeBool(recipe, AnyWoodMembers, "anyWood", out bool anyWood) && anyWood && ingredient.type == ItemID.Wood)
            {
                return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.Wood));
            }

            if (TryGetRecipeBool(recipe, AnySandMembers, "anySand", out bool anySand) && anySand && ingredient.type == ItemID.SandBlock)
            {
                return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.SandBlock));
            }

            if (TryGetRecipeBool(recipe, AnyFragmentMembers, "anyFragment", out bool anyFragment) && anyFragment && ingredient.type == ItemID.FragmentSolar)
            {
                return CombineAnyLabel(prefix, Lang.misc[51].Value);
            }

            const int PressurePlateItemId = 542;
            if (TryGetRecipeBool(recipe, AnyPressurePlateMembers, "anyPressurePlate", out bool anyPressurePlate) && anyPressurePlate && ingredient.type == PressurePlateItemId)
            {
                return CombineAnyLabel(prefix, Lang.misc[38].Value);
            }

            return null;
        }

        private static int GetAcceptedGroupId(Recipe recipe, int index)
        {
            if (recipe is null || index < 0)
            {
                return -1;
            }

            if (AcceptedGroupResolver is not null)
            {
                try
                {
                    int value = AcceptedGroupResolver(recipe, index);
                    if (value >= 0)
                    {
                        return value;
                    }
                }
                catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
                {
                    _loggedRecipeGroupReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to inspect recipe accepted groups: {ex}");
                }
            }

            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is not null &&
                index >= 0 &&
                index < requiredItems.Count &&
                RecipeGroupLookup.Value.TryGetValue(requiredItems[index].type, out int fallbackGroup) &&
                fallbackGroup >= 0)
            {
                return fallbackGroup;
            }

            return -1;
        }

        private static Dictionary<int, int> DiscoverRecipeGroupLookup()
        {
            var result = new Dictionary<int, int>();

            try
            {
                Type groupType = typeof(RecipeGroup);
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                string[] candidateNames =
                {
                    "recipeGroupIDs",
                    "recipeGroupLookup",
                    "recipeGroupLookupTable",
                    "_recipeGroupIDs",
                    "_recipeGroupLookup"
                };

                foreach (string fieldName in candidateNames)
                {
                    FieldInfo? field = groupType.GetField(fieldName, flags);
                    if (field is null)
                    {
                        continue;
                    }

                    object? value = field.GetValue(null);
                    if (value is IDictionary dictionary)
                    {
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            if (entry.Key is int key && entry.Value is int id)
                            {
                                result[key] = id;
                            }
                        }
                    }
                    else if (value is IEnumerable<KeyValuePair<int, int>> pairs)
                    {
                        foreach (KeyValuePair<int, int> pair in pairs)
                        {
                            result[pair.Key] = pair.Value;
                        }
                    }

                    if (result.Count > 0)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to discover recipe group lookup: {ex}");
            }

            return result;
        }

        private static Func<Recipe, int, int>? CreateAcceptedGroupResolver()
        {
            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                string[] candidateFields = { "acceptedGroup", "_acceptedGroup", "AcceptedGroup" };

                foreach (string fieldName in candidateFields)
                {
                    FieldInfo? field = typeof(Recipe).GetField(fieldName, flags);
                    if (field is null)
                    {
                        continue;
                    }

                    if (field.FieldType == typeof(int[]))
                    {
                        return (recipe, index) =>
                        {
                            if (recipe is null || index < 0)
                            {
                                return -1;
                            }

                            if (field.GetValue(recipe) is int[] groups && index < groups.Length)
                            {
                                return groups[index];
                            }

                            return -1;
                        };
                    }

                    if (typeof(IList<int>).IsAssignableFrom(field.FieldType))
                    {
                        return (recipe, index) =>
                        {
                            if (recipe is null || index < 0)
                            {
                                return -1;
                            }

                            if (field.GetValue(recipe) is IList<int> list && index < list.Count)
                            {
                                return list[index];
                            }

                            return -1;
                        };
                    }
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to bind accepted group resolver: {ex}");
            }

            return null;
        }
    }
}
