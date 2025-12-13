#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Workshop Hub menu.
/// </summary>
public sealed class WorkshopHubAccessibilitySystem : ModSystem
{
    private const int BaseLinkId = 3000;
    private const float DefaultSpacing = 200f;

    private static int _lastAnnouncedPointId = -1;
    private static int _lastSeenPointId = -1;
    private static UIWorkshopHub? _lastHub;
    private static int _initialFocusFramesRemaining;

    /// <summary>
    /// Returns true if the Workshop Hub is currently active and handling gamepad input.
    /// Used by MenuNarration to suppress hover announcements that would conflict.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (_lastHub is null || !PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            // Verify we're still in the Workshop Hub UI state
            if (Main.MenuUI?.CurrentState is not UIWorkshopHub)
            {
                _lastHub = null;
                return false;
            }

            return true;
        }
    }

    // Cached field references for button elements
    private static readonly FieldInfo? ButtonModsField = typeof(UIWorkshopHub).GetField("_buttonMods", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonModSourcesField = typeof(UIWorkshopHub).GetField("_buttonModSources", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonModBrowserField = typeof(UIWorkshopHub).GetField("_buttonModBrowser", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonModPackField = typeof(UIWorkshopHub).GetField("_buttonModPack", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonUseResourcePacksField = typeof(UIWorkshopHub).GetField("_buttonUseResourcePacks", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonPublishResourcePacksField = typeof(UIWorkshopHub).GetField("_buttonPublishResourcePacks", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonImportWorldsField = typeof(UIWorkshopHub).GetField("_buttonImportWorlds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonPublishWorldsField = typeof(UIWorkshopHub).GetField("_buttonPublishWorlds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonBackField = typeof(UIWorkshopHub).GetField("_buttonBack", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ButtonLogsField = typeof(UIWorkshopHub).GetField("_buttonLogs", BindingFlags.NonPublic | BindingFlags.Instance);

    // Map of button elements to their label/description localization keys
    private static readonly List<WorkshopButtonInfo> ButtonInfos = new()
    {
        new(ButtonModsField, "tModLoader.MenuManageMods", "tModLoader.MenuManageModsDescription"),
        new(ButtonModSourcesField, "tModLoader.MenuDevelopMods", "tModLoader.MenuDevelopModsDescription"),
        new(ButtonModBrowserField, "tModLoader.MenuDownloadMods", "tModLoader.MenuDownloadModsDescription"),
        new(ButtonModPackField, "tModLoader.ModsModPacks", "tModLoader.MenuModPackDescription"),
        new(ButtonUseResourcePacksField, "Workshop.HubResourcePacks", "Workshop.HubDescriptionUseResourcePacks"),
        new(ButtonPublishResourcePacksField, "Workshop.HubPublishResourcePacks", "Workshop.HubDescriptionPublishResourcePacks"),
        new(ButtonImportWorldsField, "Workshop.HubWorlds", "Workshop.HubDescriptionImportWorlds"),
        new(ButtonPublishWorldsField, "Workshop.HubPublishWorlds", "Workshop.HubDescriptionPublishWorlds"),
    };

    // Track binding for announcement lookup
    private static readonly Dictionary<int, PointBinding> BindingById = new();

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
        BindingById.Clear();
        _lastAnnouncedPointId = -1;
        _lastSeenPointId = -1;
        _lastHub = null;
        _initialFocusFramesRemaining = 0;
    }

    private static void EnhanceWorkshopLinks(On_UIWorkshopHub.orig_SetupGamepadPoints orig, UIWorkshopHub self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        try
        {
            ConfigureLinks(self);
            AnnounceCurrentFocus(self);
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[WorkshopHub] Failed to configure link points: {ex}");
        }
    }

    private static void ConfigureLinks(UIWorkshopHub hub)
    {
        List<SnapPoint> snapPoints = hub.GetSnapPoints();
        if (snapPoints.Count == 0)
        {
            return;
        }

        // Detect when entering a new Workshop Hub and reset announcement state
        if (!ReferenceEquals(hub, _lastHub))
        {
            _lastHub = hub;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;
            // Force focus for several frames to ensure it sticks after UI initialization completes
            _initialFocusFramesRemaining = 5;
            ScreenReaderMod.Instance?.Logger.Info("[WorkshopHub] Entered Workshop Hub");
        }

        BindingById.Clear();

        SnapPoint? backPoint = FindSnapPoint(snapPoints, "Back");
        SnapPoint? logsPoint = FindSnapPoint(snapPoints, "Logs");

        List<SnapPoint> buttonPoints = snapPoints
            .Where(p => string.Equals(p.Name, "Button", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Position.Y)
            .ThenBy(p => p.Position.X)
            .ToList();

        int nextId = BaseLinkId;
        var bindings = new List<PointBinding>(2 + buttonPoints.Count);

        // Build button element map for the current hub instance
        var elementToInfo = new Dictionary<UIElement, WorkshopButtonInfo>();
        foreach (WorkshopButtonInfo info in ButtonInfos)
        {
            if (info.Field?.GetValue(hub) is UIElement element)
            {
                elementToInfo[element] = info;
            }
        }

        // Add button bindings with label info
        foreach (SnapPoint button in buttonPoints)
        {
            UIElement? buttonElement = FindButtonElementForSnapPoint(hub, button, elementToInfo.Keys);
            string label = string.Empty;
            string description = string.Empty;

            if (buttonElement is not null && elementToInfo.TryGetValue(buttonElement, out WorkshopButtonInfo info))
            {
                label = Language.GetTextValue(info.LabelKey);
                description = Language.GetTextValue(info.DescriptionKey);
            }

            var binding = new PointBinding(nextId++, button, label, description, buttonElement);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Add back button
        if (backPoint is not null)
        {
            string backLabel = Language.GetTextValue("UI.Back");
            var backBinding = new PointBinding(nextId++, backPoint, backLabel, string.Empty, null);
            bindings.Add(backBinding);
            BindingById[backBinding.Id] = backBinding;
        }

        // Add logs button
        if (logsPoint is not null)
        {
            string logsLabel = Language.GetTextValue("Workshop.ReportLogsButton");
            var logsBinding = new PointBinding(nextId++, logsPoint, logsLabel, string.Empty, null);
            bindings.Add(logsBinding);
            BindingById[logsBinding.Id] = logsBinding;
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

        // Force focus to "Manage Mods" for several frames to ensure it sticks
        if (PlayerInput.UsingGamepadUI && _initialFocusFramesRemaining > 0)
        {
            // Default to "Manage Mods" button if available, otherwise fall back to first button
            string manageModsLabel = Language.GetTextValue("tModLoader.MenuManageMods");
            PointBinding? manageModsBinding = bindings.FirstOrDefault(b =>
                string.Equals(b.Label, manageModsLabel, StringComparison.OrdinalIgnoreCase));

            int defaultPointId = manageModsBinding?.Id ?? bindings[0].Id;
            UILinkPointNavigator.ChangePoint(defaultPointId);
            _initialFocusFramesRemaining--;
        }
    }

    private static UIElement? FindButtonElementForSnapPoint(UIWorkshopHub hub, SnapPoint snapPoint, IEnumerable<UIElement> knownButtons)
    {
        // SnapPoints have a Position; compare to button element positions
        Vector2 snapPos = snapPoint.Position;
        float tolerance = 50f;

        foreach (UIElement button in knownButtons)
        {
            CalculatedStyle dims = button.GetDimensions();
            Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);

            if (Vector2.DistanceSquared(snapPos, center) < tolerance * tolerance)
            {
                return button;
            }
        }

        return null;
    }

    private static void AnnounceCurrentFocus(UIWorkshopHub hub)
    {
        if (!PlayerInput.UsingGamepadUI)
        {
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        if (currentPoint < BaseLinkId)
        {
            return;
        }

        // Only announce when the point has been stable for at least one frame
        // This prevents announcing during navigation initialization when the point may shift
        bool isStable = currentPoint == _lastSeenPointId;
        bool alreadyAnnounced = currentPoint == _lastAnnouncedPointId;

        // Update what we saw this frame for next frame's stability check
        _lastSeenPointId = currentPoint;

        // If not stable yet or already announced, skip
        if (!isStable || alreadyAnnounced)
        {
            return;
        }

        if (!BindingById.TryGetValue(currentPoint, out PointBinding binding))
        {
            return;
        }

        string announcement = BuildAnnouncement(binding);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastAnnouncedPointId = currentPoint;

        // Play menu tick sound for navigation (fancy buttons don't play sound on hover like Back/Logs do)
        SoundEngine.PlaySound(SoundID.MenuTick);

        ScreenReaderMod.Instance?.Logger.Info($"[WorkshopHub] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    private static string BuildAnnouncement(PointBinding binding)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        // For main buttons, include the description
        string description = TextSanitizer.Clean(binding.Description);
        if (!string.IsNullOrWhiteSpace(description))
        {
            return $"{label}. {description}";
        }

        return label;
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

    private readonly record struct PointBinding(int Id, SnapPoint Point, string Label, string Description, UIElement? Element);

    private readonly record struct WorkshopButtonInfo(FieldInfo? Field, string LabelKey, string DescriptionKey);
}
