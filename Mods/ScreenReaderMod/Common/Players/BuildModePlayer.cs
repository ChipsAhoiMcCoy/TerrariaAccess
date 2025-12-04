#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using global::ScreenReaderMod;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Systems.BuildMode;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class BuildModePlayer : ModPlayer
{
    private const int CursorAnnouncementCooldownTicks = 6;
    private const int HurtInputGraceTicks = 15;

    private bool _buildModeActive;
    private Point? _firstCorner;
    private Point? _secondCorner;
    private Point _lastAnnouncedCursor;
    private int _cursorAnnounceCooldown;
    private bool _lastMouseLeft;
    private bool _lastQuickMount;
    private Rectangle _activeSelection;
    private SelectionAction _activeAction;
    private int _activeItemType;
    private int _selectionIndex;
    private int _tilesCleared;
    private int _wallsCleared;
    private int _tilesPlaced;
    private int _wallsPlaced;
    private bool _actionCompletedAnnounced;
    private int _actionCooldown;
    private int _autoToolRevertSlot = -1;
    private string? _lastCursorAnnouncement;
    private bool _rangeExpanded;
    private int _originalTileRangeX;
    private int _originalTileRangeY;
    private int _originalBlockRange;
    private bool _wasUseHeld;
    private int _hurtGraceTicks;

    private bool HasSelection => _firstCorner.HasValue && _secondCorner.HasValue;
    private bool AwaitingSecondCorner => _firstCorner.HasValue && !_secondCorner.HasValue;

    public override void ResetEffects()
    {
        if (!_buildModeActive)
        {
            RestorePlacementRangeIfNeeded();
            return;
        }

        ExpandPlacementRangeToViewport();
    }

    public override void PreUpdate()
    {
        if (!_buildModeActive)
        {
            RestorePlacementRangeIfNeeded();
            return;
        }

        GuardBuildModeInput();
        ExpandPlacementRangeToViewport();
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        if (BuildModeKeybinds.Toggle?.JustPressed ?? false)
        {
            ToggleBuildMode();
        }

        if (!_buildModeActive)
        {
            TrackMouseForCornerPlacement(triggersSet);
            return;
        }

        bool placePressed = CaptureCornerPlacementInput(triggersSet);
        if (!placePressed)
        {
            return;
        }

        if (!TryCaptureCursorTile(out Point tile))
        {
            ScreenReaderService.Announce("Build mode: cursor is out of world bounds.");
            return;
        }

        HandleCornerPlacement(tile);
    }

    public override void PostUpdate()
    {
        bool useHeld = IsUseHeld();
        bool useJustPressed = useHeld && !_wasUseHeld;
        _wasUseHeld = useHeld;

        AnnounceCursorPositionIfNeeded();
        InGameNarrationSystem.HotbarNarrator.SetExternalSuppression(false);

        if (!_buildModeActive)
        {
            RestorePlacementRangeIfNeeded();
        }

        if (!_buildModeActive || !HasSelection)
        {
            ResetActiveAction();
            return;
        }

        if (!useHeld)
        {
            _actionCooldown = 0;
            return;
        }

        if (_hurtGraceTicks > 0)
        {
            _hurtGraceTicks--;
            _actionCooldown = 0;
            return;
        }

        if (IsPlayerMoving())
        {
            _actionCooldown = 0;
            return;
        }

        if (Player.HeldItem is not { } held || held.IsAir)
        {
            ResetActiveAction();
            return;
        }

        Rectangle selection = GetSelection();
        int totalTiles = selection.Width * selection.Height;
        SelectionAction action = DetermineAction(held);
        if (action == SelectionAction.None)
        {
            ResetActiveAction();
            return;
        }

        if (useJustPressed && _activeSelection == selection && _activeAction != SelectionAction.None && _selectionIndex >= totalTiles)
        {
            ResetActiveAction();
        }

        EnsureActiveAction(selection, action, ref held);
        SuppressVanillaUseWhileActing();
        InGameNarrationSystem.HotbarNarrator.SetExternalSuppression(action is SelectionAction.PlaceTile or SelectionAction.PlaceWall);

        bool acted = ProcessSelectionStep(selection, action, ref held);
        if (!acted && _selectionIndex >= totalTiles && !_actionCompletedAnnounced)
        {
            AnnounceCompletion(action, selection, Player.HeldItem);
            _actionCompletedAnnounced = true;
        }
    }

    private void ExpandPlacementRangeToViewport()
    {
        float zoomX = Math.Abs(Main.GameViewMatrix.Zoom.X) < 0.001f ? 1f : Main.GameViewMatrix.Zoom.X;
        float zoomY = Math.Abs(Main.GameViewMatrix.Zoom.Y) < 0.001f ? zoomX : Main.GameViewMatrix.Zoom.Y;
        float zoom = Math.Max(0.001f, Math.Min(zoomX, zoomY));

        float viewWidth = Main.screenWidth / zoom;
        float viewHeight = Main.screenHeight / zoom;

        Vector2 topLeft = Main.screenPosition;
        Vector2 bottomRight = topLeft + new Vector2(viewWidth, viewHeight);

        float leftTiles = MathF.Abs(Player.Center.X - topLeft.X) / 16f;
        float rightTiles = MathF.Abs(bottomRight.X - Player.Center.X) / 16f;
        float upTiles = MathF.Abs(Player.Center.Y - topLeft.Y) / 16f;
        float downTiles = MathF.Abs(bottomRight.Y - Player.Center.Y) / 16f;

        int horizontalRange = (int)Math.Ceiling(Math.Max(leftTiles, rightTiles)) + 2;
        int verticalRange = (int)Math.Ceiling(Math.Max(upTiles, downTiles)) + 2;

        if (!_rangeExpanded)
        {
            _rangeExpanded = true;
            _originalTileRangeX = Player.tileRangeX;
            _originalTileRangeY = Player.tileRangeY;
            _originalBlockRange = Player.blockRange;
        }

        Player.tileRangeX = Math.Max(Player.tileRangeX, horizontalRange);
        Player.tileRangeY = Math.Max(Player.tileRangeY, verticalRange);
        Player.blockRange = Math.Max(Player.blockRange, Math.Max(horizontalRange, verticalRange));
    }

    private void ToggleBuildMode()
    {
        _buildModeActive = !_buildModeActive;
        if (!_buildModeActive)
        {
            RestorePlacementRangeIfNeeded();
            ResetSelection();
            ScreenReaderService.Announce("Build mode disabled.");
            return;
        }

        ScreenReaderService.Announce("Build mode enabled. Press A to mark corners.");
    }

    private void ResetState()
    {
        _buildModeActive = false;
        RestorePlacementRangeIfNeeded();
        ResetSelection();
        _cursorAnnounceCooldown = 0;
        _lastAnnouncedCursor = Point.Zero;
        _lastMouseLeft = false;
        _lastQuickMount = false;
        ResetActiveAction();
        _hurtGraceTicks = 0;
    }

    private void ResetActiveAction()
    {
        RevertAutoToolIfNeeded();
        _activeSelection = Rectangle.Empty;
        _activeAction = SelectionAction.None;
        _activeItemType = 0;
        _selectionIndex = 0;
        _tilesCleared = 0;
        _wallsCleared = 0;
        _tilesPlaced = 0;
        _wallsPlaced = 0;
        _actionCompletedAnnounced = false;
        _actionCooldown = 0;
        _autoToolRevertSlot = -1;
        _wasUseHeld = false;
        _hurtGraceTicks = 0;
    }

    private void ResetSelection()
    {
        _firstCorner = null;
        _secondCorner = null;
        _lastCursorAnnouncement = null;
        ResetActiveAction();
    }

    public override void OnHurt(Player.HurtInfo info)
    {
        _hurtGraceTicks = HurtInputGraceTicks;
    }

    private void SuppressQuickMount(TriggersSet triggersSet)
    {
        triggersSet.QuickMount = false;
        Player.controlMount = false;
    }

    private void EnsureActiveAction(Rectangle selection, SelectionAction action, ref Item held)
    {
        if (selection != _activeSelection || action != _activeAction || held.type != _activeItemType)
        {
            _activeSelection = selection;
            _activeAction = action;
            _activeItemType = held.type;
            _selectionIndex = 0;
            _tilesCleared = 0;
            _wallsCleared = 0;
            _tilesPlaced = 0;
            _wallsPlaced = 0;
            _actionCompletedAnnounced = false;
            _actionCooldown = 0;
            RevertAutoToolIfNeeded();
        }
        else
        {
            held = Player.HeldItem;
        }
    }

    private SelectionAction DetermineAction(Item held)
    {
        if (held.pick > 0 || held.axe > 0 || held.hammer > 0)
        {
            return SelectionAction.Clear;
        }

        if (held.createTile >= TileID.Dirt)
        {
            return SelectionAction.PlaceTile;
        }

        if (held.createWall > WallID.None)
        {
            return SelectionAction.PlaceWall;
        }

        return SelectionAction.None;
    }

    private bool ProcessSelectionStep(Rectangle selection, SelectionAction action, ref Item held)
    {
        int total = selection.Width * selection.Height;
        if (_selectionIndex >= total)
        {
            return false;
        }

        int offset = _selectionIndex;
        int x = selection.Left + offset % selection.Width;
        int y = selection.Top + offset / selection.Width;
        bool advance = HandleSelectionPosition(action, ref held, x, y);
        if (advance)
        {
            _selectionIndex++;
        }

        return true;
    }

    private bool HandleSelectionPosition(SelectionAction action, ref Item held, int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 1))
        {
            return true;
        }

        return action switch
        {
            SelectionAction.Clear => HitForClear(x, y, ref held),
            SelectionAction.PlaceTile => TryPlaceSingleTile(x, y, ref held),
            SelectionAction.PlaceWall => TryPlaceSingleWall(x, y, ref held),
            _ => true
        };
    }

    private bool HitForClear(int x, int y, ref Item held)
    {
        if (_actionCooldown > 0)
        {
            _actionCooldown--;
            return false;
        }

        Tile tile = Framing.GetTileSafely(x, y);
        if (!tile.HasTile && tile.WallType == 0)
        {
            return true;
        }

        if (RequiresAxe(tile) && held.axe <= 0 && TryAutoSwapToAxe(tile, ref held))
        {
            tile = Framing.GetTileSafely(x, y);
        }

        bool advanced = true;
        int tilePower = Math.Max(held.pick, held.axe);
        int swingDelay = GetAdjustedMiningDelay(held);
        bool wasTree = RequiresAxe(tile);
        bool hadTile = tile.HasTile;

        if (hadTile && tilePower > 0)
        {
            Player.PickTile(x, y, tilePower);
            if (hadTile && !Main.tile[x, y].HasTile)
            {
                _tilesCleared++;
                if (wasTree)
                {
                    RevertAutoToolIfNeeded();
                }

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, x, y);
                }
            }
            else if (Main.tile[x, y].HasTile)
            {
                advanced = false;
            }
        }

        if (held.hammer > 0 && tile.WallType > 0)
        {
            int beforeWall = tile.WallType;
            Player.PickWall(x, y, held.hammer);
            if (beforeWall != 0 && Main.tile[x, y].WallType == 0)
            {
                _wallsCleared++;
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 2, x, y);
                }
            }
            else if (Main.tile[x, y].WallType != 0)
            {
                advanced = false;
            }
        }

        _actionCooldown = swingDelay;
        return advanced;
    }

    private bool TryPlaceSingleTile(int x, int y, ref Item held)
    {
        if (_actionCooldown > 0)
        {
            _actionCooldown--;
            return false;
        }

        if (held.consumable && held.stack <= 0)
        {
            return true;
        }

        Tile before = Framing.GetTileSafely(x, y);
        bool beforeHadTile = before.HasTile;
        int beforeType = before.TileType;
        if (beforeHadTile && beforeType == held.createTile)
        {
            _actionCooldown = 0;
            return true;
        }

        bool placed = WorldGen.PlaceTile(x, y, held.createTile, mute: false, forced: true, plr: Player.whoAmI, style: held.placeStyle);
        if (placed)
        {
            WorldGen.SquareTileFrame(x, y, resetFrame: true);
        }

        Tile after = Main.tile[x, y];
        bool placedNow = after.HasTile && after.TileType == held.createTile && (!beforeHadTile || beforeType != held.createTile);
        if (!placedNow)
        {
            _actionCooldown = GetAdjustedPlacementDelay(held, isWall: false);
            return placed;
        }

        _tilesPlaced++;
        ConsumeHeldItem(ref held);

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendTileSquare(-1, x, y, 1);
        }

        _actionCooldown = GetAdjustedPlacementDelay(held, isWall: false);
        return true;
    }

    private bool TryPlaceSingleWall(int x, int y, ref Item held)
    {
        if (_actionCooldown > 0)
        {
            _actionCooldown--;
            return false;
        }

        if (held.consumable && held.stack <= 0)
        {
            return true;
        }

        int beforeWall = Main.tile[x, y].WallType;
        if (beforeWall == held.createWall || !WallPlacementAllowedAt(x, y))
        {
            _actionCooldown = 0;
            return true;
        }

        WorldGen.PlaceWall(x, y, held.createWall, mute: false);
        int afterWall = Main.tile[x, y].WallType;
        bool placed = afterWall != beforeWall && afterWall == held.createWall;

        if (!placed)
        {
            _actionCooldown = GetAdjustedPlacementDelay(held, isWall: true);
            return placed;
        }

        _wallsPlaced++;
        ConsumeHeldItem(ref held);

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendTileSquare(-1, x, y, 1);
        }

        _actionCooldown = GetAdjustedPlacementDelay(held, isWall: true);
        return true;
    }

    private void AnnounceCompletion(SelectionAction action, Rectangle selection, Item held)
    {
        string itemName = TextSanitizer.Clean(held.AffixName());
        switch (action)
        {
            case SelectionAction.Clear:
                if (_tilesCleared > 0 || _wallsCleared > 0)
                {
                    ScreenReaderService.Announce($"Build mode: cleared {_tilesCleared + _wallsCleared} blocks with {itemName}.");
                }
                else
                {
                    ScreenReaderService.Announce("Build mode: nothing to clear in the selected area.");
                }

                break;

            case SelectionAction.PlaceTile:
                if (_tilesPlaced > 0)
                {
                    string blockName = TextSanitizer.Clean(held.Name);
                    ScreenReaderService.Announce($"Build mode: placed {_tilesPlaced} tiles of {blockName}.");
                }
                else
                {
                    ScreenReaderService.Announce("Build mode: could not place tiles in the selected area.");
                }

                break;

            case SelectionAction.PlaceWall:
                if (_wallsPlaced > 0)
                {
                    string wallName = TextSanitizer.Clean(held.Name);
                    ScreenReaderService.Announce($"Build mode: placed {_wallsPlaced} walls of {wallName}.");
                }
                else
                {
                    ScreenReaderService.Announce("Build mode: could not place walls in the selected area.");
                }

                break;
        }
    }

    private bool IsUseHeld()
    {
        return Player.controlUseItem || Player.controlUseTile || Main.mouseLeft || Main.mouseRight;
    }

    private bool CaptureCornerPlacementInput(TriggersSet triggersSet)
    {
        bool placePressed = BuildModeKeybinds.Place?.JustPressed ?? false;
        bool usingGamepad = PlayerInput.UsingGamepad;
        bool quickMountPressed = triggersSet.QuickMount;
        bool quickMountJustPressed = quickMountPressed && !_lastQuickMount;
        _lastQuickMount = quickMountPressed;

        if (quickMountPressed)
        {
            placePressed |= quickMountJustPressed;
            SuppressQuickMount(triggersSet);
        }

        if (!placePressed && !usingGamepad)
        {
            bool mouseLeft = triggersSet.MouseLeft;
            placePressed = mouseLeft && !_lastMouseLeft;
            _lastMouseLeft = mouseLeft;
        }
        else
        {
            _lastMouseLeft = triggersSet.MouseLeft;
        }

        return placePressed;
    }

    private void TrackMouseForCornerPlacement(TriggersSet triggersSet)
    {
        if (!PlayerInput.UsingGamepad)
        {
            _lastMouseLeft = triggersSet.MouseLeft;
        }

        _lastQuickMount = triggersSet.QuickMount;
    }

    private void HandleCornerPlacement(Point tile)
    {
        if (!_firstCorner.HasValue)
        {
            _firstCorner = tile;
            _lastAnnouncedCursor = tile;
            ScreenReaderService.Announce("Build mode: point one set.");
            return;
        }

        if (!_secondCorner.HasValue)
        {
            _secondCorner = tile;
            Rectangle bounds = GetSelection();
            ScreenReaderService.Announce($"Build mode: points set. Selection is {bounds.Width} by {bounds.Height} tiles.");
            return;
        }

        _firstCorner = tile;
        _secondCorner = null;
        _lastAnnouncedCursor = tile;
        ScreenReaderService.Announce("Build mode: selection reset. Point one set.");
    }

    private static bool TryCaptureCursorTile(out Point tile)
    {
        Vector2 cursorWorld = Main.MouseWorld;
        int tileX = (int)(cursorWorld.X / 16f);
        int tileY = (int)(cursorWorld.Y / 16f);

        if (WorldGen.InWorld(tileX, tileY, 1))
        {
            tile = new Point(tileX, tileY);
            return true;
        }

        tile = Point.Zero;
        return false;
    }

    private Rectangle GetSelection()
    {
        if (!HasSelection)
        {
            return Rectangle.Empty;
        }

        Point first = _firstCorner!.Value;
        Point second = _secondCorner!.Value;
        int minX = Math.Min(first.X, second.X);
        int minY = Math.Min(first.Y, second.Y);
        int maxX = Math.Max(first.X, second.X);
        int maxY = Math.Max(first.Y, second.Y);

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private void AnnounceCursorPositionIfNeeded()
    {
        if (!_buildModeActive || !_firstCorner.HasValue || _secondCorner.HasValue)
        {
            _cursorAnnounceCooldown = 0;
            return;
        }

        if (!TryCaptureCursorTile(out Point tile))
        {
            _cursorAnnounceCooldown = 0;
            return;
        }

        if (_cursorAnnounceCooldown > 0)
        {
            _cursorAnnounceCooldown--;
            return;
        }

        if (tile == _lastAnnouncedCursor)
        {
            return;
        }

        _lastAnnouncedCursor = tile;
        _cursorAnnounceCooldown = CursorAnnouncementCooldownTicks;

        string announcement = BuildDirectionalCursorAnnouncement(_firstCorner.Value, tile);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            _lastCursorAnnouncement = null;
            return;
        }

        bool suppressRepeats = PlayerInput.UsingGamepad && !IsGamepadDpadPressed();
        if (suppressRepeats && string.Equals(_lastCursorAnnouncement, announcement, StringComparison.Ordinal))
        {
            return;
        }

        _lastCursorAnnouncement = announcement;
        ScreenReaderService.Announce(announcement);
    }

    private string BuildDirectionalCursorAnnouncement(Point origin, Point current)
    {
        int deltaX = current.X - origin.X;
        int deltaY = current.Y - origin.Y;

        List<string> parts = new();

        if (deltaY != 0)
        {
            string direction = deltaY > 0 ? "down" : "up";
            parts.Add($"{Math.Abs(deltaY)} {direction}");
        }

        if (deltaX != 0)
        {
            string direction = deltaX > 0 ? "right" : "left";
            parts.Add($"{Math.Abs(deltaX)} {direction}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static bool RequiresAxe(Tile tile)
    {
        return tile.HasTile && tile.TileType >= 0 && tile.TileType < Main.tileAxe.Length && Main.tileAxe[tile.TileType];
    }

    private int GetAdjustedMiningDelay(Item held)
    {
        int baseUse = Math.Max(1, held.useTime);
        float pickSpeed = Math.Max(0.3f, Player.pickSpeed);
        return Math.Max(1, (int)MathF.Ceiling(baseUse * pickSpeed));
    }

    private int GetAdjustedPlacementDelay(Item held, bool isWall)
    {
        int baseUse = Math.Max(1, held.useTime);
        float speedMult = isWall ? Player.wallSpeed : Player.tileSpeed;
        if (speedMult <= 0f)
        {
            return baseUse;
        }

        return Math.Max(1, (int)MathF.Ceiling(baseUse / speedMult));
    }

    private bool TryAutoSwapToAxe(Tile tile, ref Item held)
    {
        if (!RequiresAxe(tile) || held.axe > 0)
        {
            return false;
        }

        int bestAxeIndex = FindBestAxeIndex();
        if (bestAxeIndex < 0 || bestAxeIndex == Player.selectedItem)
        {
            return false;
        }

        if (_autoToolRevertSlot == -1)
        {
            _autoToolRevertSlot = Player.selectedItem;
        }

        Player.selectedItem = bestAxeIndex;
        held = Player.HeldItem;
        _activeItemType = held.type;
        _actionCooldown = 0;
        return true;
    }

    private void ConsumeHeldItem(ref Item held)
    {
        if (!held.consumable)
        {
            return;
        }

        if (Player.ConsumeItem(held.type))
        {
            held = Player.HeldItem;
            _activeItemType = held.type;
        }
    }

    private int FindBestAxeIndex()
    {
        int bestIndex = -1;
        int bestPower = -1;

        for (int i = 0; i < Player.inventory.Length; i++)
        {
            Item candidate = Player.inventory[i];
            if (candidate is null || candidate.IsAir)
            {
                continue;
            }

            int power = candidate.axe;
            if (power <= 0)
            {
                continue;
            }

            if (power > bestPower || (power == bestPower && bestIndex == -1))
            {
                bestPower = power;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void RevertAutoToolIfNeeded()
    {
        if (_autoToolRevertSlot < 0)
        {
            return;
        }

        int targetSlot = _autoToolRevertSlot;
        _autoToolRevertSlot = -1;

        if (targetSlot < 0 || targetSlot >= Player.inventory.Length)
        {
            return;
        }

        Item target = Player.inventory[targetSlot];
        if (target is null || target.IsAir)
        {
            return;
        }

        Player.selectedItem = targetSlot;
        _activeItemType = Player.HeldItem.type;
    }

    private static bool WallPlacementAllowedAt(int x, int y)
    {
        Tile tile = Framing.GetTileSafely(x, y);
        if (tile.HasTile && TileID.Sets.BasicChest[tile.TileType])
        {
            return false;
        }

        return true;
    }

    private void SuppressVanillaUseWhileActing()
    {
        Player.controlUseItem = false;
        Player.controlUseTile = false;
        Player.releaseUseItem = true;
        Player.releaseUseTile = true;
    }

    private void GuardBuildModeInput()
    {
        if (!HasSelection)
        {
            return;
        }

        if (!IsUseHeld())
        {
            return;
        }

        SuppressVanillaUseWhileActing();
    }

    private static bool IsGamepadDpadPressed()
    {
        try
        {
            GamePadState state = GamePad.GetState(PlayerIndex.One);
            if (!state.IsConnected)
            {
                return false;
            }

            return state.DPad.Up == ButtonState.Pressed ||
                state.DPad.Down == ButtonState.Pressed ||
                state.DPad.Left == ButtonState.Pressed ||
                state.DPad.Right == ButtonState.Pressed;
        }
        catch
        {
            return false;
        }
    }

    private void RestorePlacementRangeIfNeeded()
    {
        if (!_rangeExpanded)
        {
            return;
        }

        Player.tileRangeX = _originalTileRangeX;
        Player.tileRangeY = _originalTileRangeY;
        Player.blockRange = _originalBlockRange;
        _rangeExpanded = false;
    }

    private bool IsPlayerMoving()
    {
        if (Player.controlLeft || Player.controlRight || Player.controlUp || Player.controlDown)
        {
            return true;
        }

        return Math.Abs(Player.velocity.X) > 0.05f || Math.Abs(Player.velocity.Y) > 0.05f;
    }

    private enum SelectionAction
    {
        None,
        Clear,
        PlaceTile,
        PlaceWall
    }
}
