#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

public sealed class WorkshopMenuGamepadSystem : ModSystem
{
    private const int BaseLinkId = 3000;
    private const float DefaultSpacing = 200f;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_UIWorkshopHub.SetupGamepadPoints += EnhanceWorkshopLinks;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_UIWorkshopHub.SetupGamepadPoints -= EnhanceWorkshopLinks;
    }

    private static void EnhanceWorkshopLinks(On_UIWorkshopHub.orig_SetupGamepadPoints orig, UIWorkshopHub self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        try
        {
            ConfigureLinks(self);
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[WorkshopGamepad] Failed to configure link points: {ex}");
        }
    }

    private static void ConfigureLinks(UIWorkshopHub hub)
    {
        List<SnapPoint> snapPoints = hub.GetSnapPoints();
        if (snapPoints.Count == 0)
        {
            return;
        }

        SnapPoint? backPoint = FindSnapPoint(snapPoints, "Back");
        SnapPoint? logsPoint = FindSnapPoint(snapPoints, "Logs");

        List<SnapPoint> buttonPoints = snapPoints
            .Where(p => string.Equals(p.Name, "Button", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Position.Y)
            .ThenBy(p => p.Position.X)
            .ToList();

        int nextId = BaseLinkId;
        var bindings = new List<PointBinding>(2 + buttonPoints.Count);

        int backId = -1;
        if (backPoint is not null)
        {
            backId = nextId++;
            bindings.Add(new PointBinding(backId, backPoint));
        }

        int logsId = -1;
        if (logsPoint is not null)
        {
            logsId = nextId++;
            bindings.Add(new PointBinding(logsId, logsPoint));
        }

        foreach (SnapPoint button in buttonPoints)
        {
            bindings.Add(new PointBinding(nextId++, button));
        }

        if (bindings.Count == 0)
        {
            return;
        }

        float rowTolerance = DetermineSpacing(bindings.Select(b => b.Point.Position.Y));
        float columnTolerance = DetermineSpacing(bindings.Select(b => b.Point.Position.X));

        foreach (PointBinding binding in bindings)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Point.Position);
            linkPoint.Unlink();
        }

        foreach (PointBinding binding in bindings)
        {
            PointBinding? left = FindHorizontalNeighbor(binding, bindings, rowTolerance, lookRight: false);
            if (left is not null)
            {
                ConnectHorizontal(left.Value, binding);
            }

            PointBinding? right = FindHorizontalNeighbor(binding, bindings, rowTolerance, lookRight: true);
            if (right is not null)
            {
                ConnectHorizontal(binding, right.Value);
            }

            PointBinding? up = FindVerticalNeighbor(binding, bindings, columnTolerance, lookDown: false);
            if (up is not null)
            {
                ConnectVertical(up.Value, binding);
            }

            PointBinding? down = FindVerticalNeighbor(binding, bindings, columnTolerance, lookDown: true);
            if (down is not null)
            {
                ConnectVertical(binding, down.Value);
            }
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = bindings[^1].Id;

        if (PlayerInput.UsingGamepadUI && !bindings.Any(b => b.Id == UILinkPointNavigator.CurrentPoint))
        {
            UILinkPointNavigator.ChangePoint(bindings[0].Id);
        }
    }

    private static SnapPoint? FindSnapPoint(IEnumerable<SnapPoint> snapPoints, string name)
    {
        return snapPoints.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static float DetermineSpacing(IEnumerable<float> values)
    {
        float minSpacing = float.MaxValue;
        float? previous = null;

        foreach (float value in values.OrderBy(v => v))
        {
            if (previous.HasValue)
            {
                float diff = value - previous.Value;
                if (diff > 1f && diff < minSpacing)
                {
                    minSpacing = diff;
                }
            }

            previous = value;
        }

        if (minSpacing == float.MaxValue)
        {
            return DefaultSpacing / 2f;
        }

        return MathF.Max(10f, minSpacing / 2f);
    }

    private static PointBinding? FindHorizontalNeighbor(PointBinding origin, IEnumerable<PointBinding> bindings, float rowTolerance, bool lookRight)
    {
        float directionMultiplier = lookRight ? 1f : -1f;

        PointBinding? candidate = null;
        float bestDistance = float.MaxValue;

        foreach (PointBinding binding in bindings)
        {
            if (binding.Id == origin.Id)
            {
                continue;
            }

            float deltaY = Math.Abs(binding.Point.Position.Y - origin.Point.Position.Y);
            if (deltaY > rowTolerance)
            {
                continue;
            }

            float deltaX = binding.Point.Position.X - origin.Point.Position.X;
            if (deltaX == 0f || Math.Sign(deltaX) != Math.Sign(directionMultiplier))
            {
                continue;
            }

            float distance = Math.Abs(deltaX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                candidate = binding;
            }
        }

        return candidate;
    }

    private static PointBinding? FindVerticalNeighbor(PointBinding origin, IEnumerable<PointBinding> bindings, float columnTolerance, bool lookDown)
    {
        float directionMultiplier = lookDown ? 1f : -1f;

        PointBinding? candidate = null;
        float bestDistance = float.MaxValue;

        foreach (PointBinding binding in bindings)
        {
            if (binding.Id == origin.Id)
            {
                continue;
            }

            float deltaX = Math.Abs(binding.Point.Position.X - origin.Point.Position.X);
            if (deltaX > columnTolerance)
            {
                continue;
            }

            float deltaY = binding.Point.Position.Y - origin.Point.Position.Y;
            if (deltaY == 0f || Math.Sign(deltaY) != Math.Sign(directionMultiplier))
            {
                continue;
            }

            float distance = Math.Abs(deltaY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                candidate = binding;
            }
        }

        return candidate;
    }

    private static void ConnectHorizontal(PointBinding left, PointBinding right)
    {
        UILinkPoint leftPoint = EnsureLinkPoint(left.Id);
        UILinkPoint rightPoint = EnsureLinkPoint(right.Id);

        leftPoint.Right = right.Id;
        rightPoint.Left = left.Id;
    }

    private static void ConnectVertical(PointBinding up, PointBinding down)
    {
        UILinkPoint upPoint = EnsureLinkPoint(up.Id);
        UILinkPoint downPoint = EnsureLinkPoint(down.Id);

        upPoint.Down = down.Id;
        downPoint.Up = up.Id;
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

    private readonly record struct PointBinding(int Id, SnapPoint Point);
}
