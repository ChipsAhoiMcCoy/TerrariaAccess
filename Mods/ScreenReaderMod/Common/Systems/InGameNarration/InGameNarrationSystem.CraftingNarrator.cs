#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        private int _lastFocusIndex = -1;
        private int _lastRecipeIndex = -1;
        private string? _lastAnnouncement;
        private int _lastCount = -1;
        private string? _lastRequirementsMessage;
        private int _lastIngredientPoint = -1;
        private int _lastIngredientRecipeIndex = -1;
        private IngredientIdentity _lastIngredientIdentity = IngredientIdentity.Empty;
        private string? _lastIngredientMessage;
        private readonly HashSet<int> _missingRequirementRecipes = new();

        private static readonly FieldInfo[] CraftingShortcutFields = typeof(UILinkPointNavigator.Shortcuts)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(int) &&
                            field.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        private static readonly FieldInfo[] CraftingLinkIdFields = DiscoverCraftingLinkIdFields();

        private static readonly Lazy<int[]> CraftingContextValues = new(DiscoverCraftingContexts);
        private static readonly Lazy<Dictionary<int, int>> RecipeGroupLookup = new(DiscoverRecipeGroupLookup);
        private static readonly Func<Recipe, int, int>? AcceptedGroupResolver = CreateAcceptedGroupResolver();

        private static bool _loggedShortcutReflectionWarning;
        private static bool _loggedRecipeGroupReflectionWarning;
        private static bool _loggedRecipeFlagReflectionWarning;

        private readonly struct IngredientIdentity : IEquatable<IngredientIdentity>
        {
            public static IngredientIdentity Empty => default;

            public IngredientIdentity(int type, int prefix, int stack)
            {
                Type = type;
                Prefix = prefix;
                Stack = stack;
            }

            public int Type { get; }
            public int Prefix { get; }
            public int Stack { get; }

            public bool IsAir => Type <= 0 || Stack <= 0;

            public static IngredientIdentity From(Item item)
            {
                if (item is null || item.IsAir)
                {
                    return Empty;
                }

                return new IngredientIdentity(item.type, item.prefix, item.stack);
            }

            public bool Equals(IngredientIdentity other)
            {
                return Type == other.Type &&
                       Prefix == other.Prefix &&
                       Stack == other.Stack;
            }

            public override bool Equals(object? obj)
            {
                return obj is IngredientIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Type, Prefix, Stack);
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
            if (focusIndex < 0 || focusIndex >= Main.availableRecipe.Length)
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

        private static bool IsRecipeResultItem(Recipe recipe, Item item)
        {
            if (recipe is null || item is null || item.IsAir)
            {
                return false;
            }

            Item result = recipe.createItem;
            if (result is null || result.IsAir)
            {
                return false;
            }

            if (item.type != result.type)
            {
                return false;
            }

            IngredientIdentity candidate = IngredientIdentity.From(item);
            IngredientIdentity target = IngredientIdentity.From(result);
            return candidate.Equals(target);
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

            if (!TryGetFocusedRecipe(out Recipe recipe, out _, out _, out _))
            {
                return null;
            }

            if (!IsRecipeResultItem(recipe, item))
            {
                return null;
            }

            string? message = BuildRequirementMessage(recipe, out _);
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }

        public void Update(Player player)
        {
            if (!InventoryNarrator.IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            bool usingGamepadUi = PlayerInput.UsingGamepadUI;
            int currentPoint = usingGamepadUi ? UILinkPointNavigator.CurrentPoint : -1;

            if (!usingGamepadUi)
            {
                ResetIngredientFocus();
            }

            if (usingGamepadUi)
            {
                if (InventoryNarrator.TryGetContextForLinkPoint(currentPoint, out int context) &&
                    !IsCraftingContext(context))
                {
                    ResetFocus();
                    return;
                }

                if (!IsCraftingLinkPoint(currentPoint))
                {
                    ResetFocus();
                    return;
                }
            }

            if (!TryGetFocusedRecipe(out Recipe recipe, out int recipeIndex, out int focus, out int available))
            {
                ResetFocus();
                return;
            }

            Item result = recipe.createItem;

            string label = ComposeItemLabel(result);
            if (result.stack > 1)
            {
                label = $"{result.stack} {label}";
            }

            string? details = InventoryNarrator.BuildTooltipDetails(
                result,
                result.Name ?? string.Empty,
                allowMouseText: false,
                suppressControllerPrompts: true);

            int ingredientCount = CountIngredients(recipe);
            string? requirementMessage = BuildRequirementMessage(recipe, out bool hadRequirementData);
            if (!string.IsNullOrWhiteSpace(requirementMessage))
            {
                details = string.IsNullOrWhiteSpace(details)
                    ? requirementMessage
                    : $"{details}. {requirementMessage}";
                _missingRequirementRecipes.Remove(recipeIndex);
            }
            else if (hadRequirementData && _missingRequirementRecipes.Add(recipeIndex))
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[CraftingNarrator] Requirement narration missing for recipe {recipeIndex} ({label})");
            }

            string combined = InventoryNarrator.CombineItemAnnouncement(label, details);
            if (string.IsNullOrWhiteSpace(combined))
            {
                combined = label;
            }

            string itemMessage = $"{combined}. Recipe {focus + 1} of {available}";
            itemMessage = GlyphTagFormatter.Normalize(itemMessage);

            bool ingredientFocused = usingGamepadUi && TryAnnounceIngredientFocus(recipe, recipeIndex, ingredientCount, currentPoint);

            bool itemChanged =
                focus != _lastFocusIndex ||
                recipeIndex != _lastRecipeIndex ||
                available != _lastCount ||
                !string.Equals(itemMessage, _lastAnnouncement, StringComparison.Ordinal);

            _lastRequirementsMessage = requirementMessage;

            if (!itemChanged)
            {
                return;
            }

            _lastFocusIndex = focus;
            _lastRecipeIndex = recipeIndex;
            _lastAnnouncement = itemMessage;
            _lastCount = available;

            if (ingredientFocused)
            {
                return;
            }

            ScreenReaderService.Announce(itemMessage, force: true);
        }

        public static bool IsCraftingLinkPoint(int point)
        {
            if (point < 0)
            {
                return false;
            }

            if (MatchesCraftingField(point, CraftingShortcutFields))
            {
                return true;
            }

            if (MatchesCraftingField(point, CraftingLinkIdFields))
            {
                return true;
            }

            return false;
        }

        private static bool IsCraftingContext(int context)
        {
            int normalized = Math.Abs(context);
            foreach (int value in CraftingContextValues.Value)
            {
                if (normalized == value)
                {
                    return true;
                }
            }

            return normalized == Math.Abs(ItemSlot.Context.CraftingMaterial);
        }

        private void Reset()
        {
            ResetFocus();
            _lastAnnouncement = null;
            _lastRequirementsMessage = null;
            _missingRequirementRecipes.Clear();
        }

        private void ResetFocus()
        {
            _lastFocusIndex = -1;
            _lastRecipeIndex = -1;
            _lastCount = -1;
            _lastRequirementsMessage = null;
            ResetIngredientFocus();
        }

        private void ResetIngredientFocus()
        {
            _lastIngredientPoint = -1;
            _lastIngredientRecipeIndex = -1;
            _lastIngredientIdentity = IngredientIdentity.Empty;
            _lastIngredientMessage = null;
        }

        private static int[] DiscoverCraftingContexts()
        {
            try
            {
                FieldInfo[] fields = typeof(ItemSlot.Context)
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (fields.Length == 0)
                {
                    return new[] { Math.Abs(ItemSlot.Context.CraftingMaterial) };
                }

                return fields
                    .Where(field => field.FieldType == typeof(int) &&
                                    field.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
                    .Select(field =>
                    {
                        try
                        {
                            return Math.Abs((int)(field.GetValue(null) ?? -1));
                        }
                        catch
                        {
                            return -1;
                        }
                    })
                    .Where(value => value >= 0)
                    .Distinct()
                    .ToArray();
            }
            catch
            {
                return new[] { Math.Abs(ItemSlot.Context.CraftingMaterial) };
            }
        }

        private static FieldInfo[] DiscoverCraftingLinkIdFields()
        {
            Type? linkIdType = typeof(UILinkPointNavigator).Assembly.GetType("Terraria.UI.Gamepad.UILinkPointID");
            if (linkIdType is null)
            {
                return Array.Empty<FieldInfo>();
            }

            return linkIdType
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                .Where(field => field.FieldType == typeof(int) &&
                                (field.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase) ||
                                 field.Name.Contains("RECIPE", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        private static bool MatchesCraftingField(int point, FieldInfo[] fields)
        {
            foreach (FieldInfo field in fields)
            {
                try
                {
                    object? value = field.GetValue(null);
                    if (value is int intValue && intValue >= 0 && intValue == point)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (!_loggedShortcutReflectionWarning)
                {
                    _loggedShortcutReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to inspect crafting link field {field.Name}: {ex}");
                }
            }

            return false;
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

        private static int CountIngredients(Recipe recipe)
        {
            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is null || requiredItems.Count == 0)
            {
                return 0;
            }

            int count = 0;
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

                if (!ingredient.IsAir && ingredient.stack > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindIngredientIndex(Recipe recipe, Item candidate, IngredientIdentity identity)
        {
            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is null || requiredItems.Count == 0)
            {
                return -1;
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

                if (ReferenceEquals(ingredient, candidate))
                {
                    return i;
                }

                if (!ingredient.IsAir && IngredientIdentity.From(ingredient).Equals(identity))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryAnnounceIngredientFocus(Recipe recipe, int recipeIndex, int ingredientCount, int currentPoint)
        {
            if (currentPoint < 0)
            {
                ResetIngredientFocus();
                return false;
            }

            if (!InventoryNarrator.TryGetItemForLinkPoint(currentPoint, out Item? focusedItem, out int context))
            {
                ResetIngredientFocus();
                return false;
            }

            if (Math.Abs(context) != Math.Abs(ItemSlot.Context.CraftingMaterial))
            {
                ResetIngredientFocus();
                return false;
            }

            if (focusedItem is null)
            {
                ResetIngredientFocus();
                return false;
            }

            IngredientIdentity identity = IngredientIdentity.From(focusedItem);
            if (identity.IsAir)
            {
                ResetIngredientFocus();
                return false;
            }

            int ingredientIndex = FindIngredientIndex(recipe, focusedItem, identity);
            if (ingredientIndex < 0)
            {
                ResetIngredientFocus();
                return false;
            }

            string? label = DescribeRequirement(recipe, recipe.requiredItem[ingredientIndex], ingredientIndex);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = ComposeItemLabel(focusedItem);
            }

            string message = ingredientCount > 0
                ? $"Ingredient {ingredientIndex + 1} of {ingredientCount}, {label}"
                : $"Ingredient, {label}";

            message = GlyphTagFormatter.Normalize(message);

            if (_lastIngredientRecipeIndex == recipeIndex &&
                _lastIngredientPoint == currentPoint &&
                identity.Equals(_lastIngredientIdentity) &&
                string.Equals(message, _lastIngredientMessage, StringComparison.Ordinal))
            {
                return true;
            }

            _lastIngredientRecipeIndex = recipeIndex;
            _lastIngredientPoint = currentPoint;
            _lastIngredientIdentity = identity;
            _lastIngredientMessage = message;

            ScreenReaderService.Announce(message, force: true);
            return true;
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
