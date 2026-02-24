#nullable enable
using System;
using TerrariaAccess.Common.Services;
using Terraria;

namespace TerrariaAccess.Common.Systems.Inventory;

/// <summary>
/// Handles narration for the Guide's crafting menu.
/// Announces items placed in the guide slot and available recipe counts.
/// </summary>
internal sealed class GuideMenuNarrator
{
    private RecipeIdentity _lastGuideIdentity;
    private int _lastGuideRecipeCount;

    /// <summary>
    /// Updates guide menu narration. Should be called each frame when inventory is open.
    /// </summary>
    public void Update()
    {
        if (!Main.InGuideCraftMenu)
        {
            Reset();
            return;
        }

        UiAreaNarrationContext.RecordArea(UiNarrationArea.Guide);
        TryAnnounceGuideItem();
    }

    /// <summary>
    /// Resets all state.
    /// </summary>
    public void Reset()
    {
        _lastGuideIdentity = default;
        _lastGuideRecipeCount = 0;
    }

    private void TryAnnounceGuideItem()
    {
        Item guideItem = Main.guideItem;
        RecipeIdentity identity = RecipeIdentity.From(guideItem);
        int recipeCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);

        bool identityChanged = !_lastGuideIdentity.Equals(identity);
        bool recipeCountChanged = recipeCount != _lastGuideRecipeCount;
        if (!identityChanged && !recipeCountChanged)
        {
            return;
        }

        _lastGuideIdentity = identity;
        _lastGuideRecipeCount = recipeCount;

        if (identity.Type <= 0)
        {
            return;
        }

        string label = ItemDescriber.ComposeLabel(guideItem, includeCountWhenSingular: true);
        string message = $"Guide: {label}";
        if (recipeCount > 0)
        {
            string suffix = recipeCount == 1 ? " recipe available" : " recipes available";
            message = $"{message}. {recipeCount}{suffix}";
        }

        ScreenReaderService.Announce(message, force: true);
    }
}
