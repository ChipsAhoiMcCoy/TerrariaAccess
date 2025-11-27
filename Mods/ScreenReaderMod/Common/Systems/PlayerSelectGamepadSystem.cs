#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

public sealed class PlayerSelectGamepadSystem : ModSystem
{
    private const int BaseLinkId = 3400;
    private static readonly FieldInfo? PlayerListField = typeof(UICharacterSelect).GetField("_playerList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_UICharacterSelect.SetupGamepadPoints += EnsureEmptyListNavigation;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_UICharacterSelect.SetupGamepadPoints -= EnsureEmptyListNavigation;
    }

    private static void EnsureEmptyListNavigation(On_UICharacterSelect.orig_SetupGamepadPoints orig, UICharacterSelect self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        if (!PlayerInput.UsingGamepadUI)
        {
            return;
        }

        UIList? playerList = PlayerListField?.GetValue(self) as UIList;
        if (playerList is null)
        {
            return;
        }

        List<UIElement> items = playerList._items;
        if (items.Count == 0)
        {
            return;
        }

        bool hasCharacters = items.Any(static item => item is UICharacterListItem);
        if (hasCharacters)
        {
            return;
        }

        var links = new List<UILinkPoint>();
        int backId = 3000;
        int newId = 3001;

        if (UILinkPointNavigator.Points.TryGetValue(backId, out UILinkPoint? backLink))
        {
            links.Add(backLink);
        }

        if (UILinkPointNavigator.Points.TryGetValue(newId, out UILinkPoint? newLink))
        {
            links.Add(newLink);
        }

        int nextId = Math.Max(BaseLinkId, UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX + 1);
        foreach (UIElement item in items)
        {
            string? typeName = item.GetType().FullName;
            if (typeName is not null && typeName.Contains("UIScrollbar", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CalculatedStyle dimensions = item.GetDimensions();
            var position = new Vector2(dimensions.X + (dimensions.Width * 0.5f), dimensions.Y + (dimensions.Height * 0.5f));

            int id = nextId++;
            UILinkPoint linkPoint = EnsureLinkPoint(id);
            UILinkPointNavigator.SetPosition(id, position);
            links.Add(linkPoint);
        }

        if (links.Count == 0)
        {
            return;
        }

        List<UILinkPoint> ordered = links
            .DistinctBy(static link => link.ID)
            .OrderBy(static link => link.Position.Y)
            .ThenBy(static link => link.Position.X)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            UILinkPoint link = ordered[i];
            link.Up = i > 0 ? ordered[i - 1].ID : -1;
            link.Down = i < ordered.Count - 1 ? ordered[i + 1].ID : -1;
            link.Left = backId;
            link.Right = newId;
        }

        if (UILinkPointNavigator.Points.TryGetValue(backId, out UILinkPoint? backButton))
        {
            backButton.Up = -1;
            backButton.Down = ordered[0].ID;
            backButton.Right = newId;
        }

        if (UILinkPointNavigator.Points.TryGetValue(newId, out UILinkPoint? newButton))
        {
            newButton.Up = ordered[^1].ID;
            newButton.Down = -1;
            newButton.Left = backId;
        }

        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = Math.Max(UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX, ordered[^1].ID);

        int current = UILinkPointNavigator.CurrentPoint;
        if (!ordered.Any(link => link.ID == current))
        {
            int fallbackId = newId;
            if (!UILinkPointNavigator.Points.ContainsKey(fallbackId))
            {
                fallbackId = ordered[0].ID;
            }

            UILinkPointNavigator.ChangePoint(fallbackId);
        }
    }

    private static UILinkPoint EnsureLinkPoint(int id)
    {
        if (!UILinkPointNavigator.Points.TryGetValue(id, out UILinkPoint? linkPoint))
        {
            linkPoint = new UILinkPoint(id, true, -1, -1, -1, -1);
            UILinkPointNavigator.Points[id] = linkPoint;
        }

        return linkPoint;
    }
}
