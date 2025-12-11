#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using global::ScreenReaderMod;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Systems.BuildMode;
using ScreenReaderMod.Common.Systems.KeyboardParity;
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

    private enum BuildModeState
    {
        Inactive,
        AwaitingFirstCorner,
        AwaitingSecondCorner,
        Executing,
    }

    private BuildModeState _state = BuildModeState.Inactive;
    private Point? _firstCorner;
    private Point? _secondCorner;
    private Point _lastAnnouncedCursor;
    private int _cursorAnnounceCooldown;
    private SelectionAction _activeAction;
    private int _activeItemType;
    private int _tilesCleared;
    private int _wallsCleared;
    private int _tilesPlaced;
    private int _wallsPlaced;
    private bool _actionCompletedAnnounced;
    private int _actionCooldown;
    private int _autoToolRevertSlot = -1;
    private string? _lastCursorAnnouncement;
    private bool _wasUseHeld;
    private int _hurtGraceTicks;
    private SelectionIterator _selectionIterator;
    private readonly BuildModeRangeManager _rangeManager = new();

    private bool BuildModeActive => _state != BuildModeState.Inactive;
    private bool HasSelection => _state == BuildModeState.Executing && _firstCorner.HasValue && _secondCorner.HasValue;
    private bool AwaitingSecondCorner => _state == BuildModeState.AwaitingSecondCorner && _firstCorner.HasValue && !_secondCorner.HasValue;

    public override void ResetEffects()
    {
        if (!BuildModeActive)
        {
            RestorePlacementRangeIfNeeded();
            return;
        }

        EnsurePlacementRangeExpanded();
    }

    public override void PreUpdate()
    {
        if (!BuildModeActive)
        {
            RestorePlacementRangeIfNeeded();
            return;
        }

        EnsurePlacementRangeExpanded();
        GuardBuildModeInput();
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        bool togglePressed = BuildModeKeybinds.Toggle?.JustPressed ?? false;
        if (togglePressed)
        {
            ToggleBuildMode();
        }

        if (BuildModeActive)
        {
            EnsurePlacementRangeExpanded();
        }

        if (!BuildModeActive)
        {
            TrackMouseForCornerPlacement(triggersSet);
            return;
        }

        bool placePressed = CaptureCornerPlacementInput(triggersSet);
        if (!placePressed)
        {
            return;
        }

        if (!TryCaptureCursorTileInRange(out Point tile))
        {
            ScreenReaderService.Announce(BuildModeNarrationCatalog.CursorOutOfBounds());
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

        if (!BuildModeActive)
        {
            RestorePlacementRangeIfNeeded();
        }

        if (!BuildModeActive || !HasSelection)
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
        SelectionAction action = DetermineAction(held);
        if (action == SelectionAction.None)
        {
            ResetActiveAction();
            return;
        }

        if (useJustPressed && _activeAction != SelectionAction.None && _selectionIterator.Completed)
        {
            ResetActiveAction();
        }

        EnsureActiveAction(selection, action, ref held);
        SuppressVanillaUseWhileActing();
        InGameNarrationSystem.HotbarNarrator.SetExternalSuppression(action is SelectionAction.PlaceTile or SelectionAction.PlaceWall);

        bool acted = ProcessSelectionStep(selection, action, ref held);
        if (!acted && _selectionIterator.Completed && !_actionCompletedAnnounced)
        {
            AnnounceCompletion(action, selection, Player.HeldItem);
            _actionCompletedAnnounced = true;
        }
    }

    private void ToggleBuildMode()
    {
        if (BuildModeActive)
        {
            _state = BuildModeState.Inactive;
            RestorePlacementRangeIfNeeded();
            ResetSelection();
            ScreenReaderService.Announce(BuildModeNarrationCatalog.Disabled());
            return;
        }

        ResetSelection();
        _state = BuildModeState.AwaitingFirstCorner;
        ScreenReaderService.Announce(BuildModeNarrationCatalog.Enabled());
    }

    private void ResetState()
    {
        _state = BuildModeState.Inactive;
        RestorePlacementRangeIfNeeded();
        ResetSelection();
        _cursorAnnounceCooldown = 0;
        _lastAnnouncedCursor = Point.Zero;
        ResetActiveAction();
        _hurtGraceTicks = 0;
    }

    private void ResetActiveAction()
    {
        RevertAutoToolIfNeeded();
        _activeAction = SelectionAction.None;
        _activeItemType = 0;
        _selectionIterator.Clear();
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
        if (_state != BuildModeState.Inactive)
        {
            _state = BuildModeState.AwaitingFirstCorner;
        }
        ResetActiveAction();
    }

    public override void OnHurt(Player.HurtInfo info)
    {
        _hurtGraceTicks = HurtInputGraceTicks;
    }

    private void EnsureActiveAction(Rectangle selection, SelectionAction action, ref Item held)
    {
        if (!_selectionIterator.IsSameSelection(selection) || action != _activeAction || held.type != _activeItemType)
        {
            _selectionIterator.Reset(selection);
            _activeAction = action;
            _activeItemType = held.type;
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
        if (!_selectionIterator.TryGetNext(out int x, out int y))
        {
            return false;
        }

        bool advance = HandleSelectionPosition(action, ref held, x, y);
        if (!advance)
        {
            _selectionIterator.Rewind();
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
                    ScreenReaderService.Announce(BuildModeNarrationCatalog.ClearedBlocks(_tilesCleared + _wallsCleared, itemName));
                }
                else
                {
                    ScreenReaderService.Announce(BuildModeNarrationCatalog.NothingToClear());
                }

                break;

            case SelectionAction.PlaceTile:
                if (_tilesPlaced > 0)
                {
                    string blockName = TextSanitizer.Clean(held.Name);
                    ScreenReaderService.Announce(BuildModeNarrationCatalog.PlacedTiles(_tilesPlaced, blockName));
                }
                else
                {
                    ScreenReaderService.Announce(BuildModeNarrationCatalog.CannotPlaceTiles());
                }

                break;

            case SelectionAction.PlaceWall:
                if (_wallsPlaced > 0)
                {
                    string wallName = TextSanitizer.Clean(held.Name);
                    ScreenReaderService.Announce(BuildModeNarrationCatalog.PlacedWalls(_wallsPlaced, wallName));
                }
                else
                {
                    ScreenReaderService.Announce(BuildModeNarrationCatalog.CannotPlaceWalls());
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
        return BuildModeKeybinds.Place?.JustPressed ?? false;
    }

    private void TrackMouseForCornerPlacement(TriggersSet triggersSet)
    {
        // Intentionally left blank; only tracking via keybind states now.
        _ = triggersSet;
    }

    private void HandleCornerPlacement(Point tile)
    {
        if (!_firstCorner.HasValue)
        {
            _firstCorner = tile;
            _lastAnnouncedCursor = tile;
            _state = BuildModeState.AwaitingSecondCorner;
            ScreenReaderService.Announce(BuildModeNarrationCatalog.PointOneSet());
            return;
        }

        if (!_secondCorner.HasValue)
        {
            _secondCorner = tile;
            _state = BuildModeState.Executing;
            Rectangle bounds = GetSelection();
            ScreenReaderService.Announce(BuildModeNarrationCatalog.SelectionSet(bounds.Width, bounds.Height));
            return;
        }

        _firstCorner = tile;
        _secondCorner = null;
        _lastAnnouncedCursor = tile;
        _state = BuildModeState.AwaitingSecondCorner;
        ScreenReaderService.Announce(BuildModeNarrationCatalog.SelectionReset());
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

    private bool TryCaptureCursorTileInRange(out Point tile)
    {
        if (!TryCaptureCursorTile(out tile))
        {
            return false;
        }

        return true;
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
        if (!BuildModeActive || !_firstCorner.HasValue || _secondCorner.HasValue)
        {
            _cursorAnnounceCooldown = 0;
            return;
        }

        if (!TryCaptureCursorTileInRange(out Point tile))
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
        if (KeyboardCursorNudgeSystem.WasArrowHeldThisFrame())
        {
            return true;
        }

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

    private void EnsurePlacementRangeExpanded()
    {
        if (IsSmartCursorActive())
        {
            RestorePlacementRangeIfNeeded();
            return;
        }

        _rangeManager.ExpandPlacementRangeToViewport(Player);
    }

    private void RestorePlacementRangeIfNeeded()
    {
        _rangeManager.RestorePlacementRange(Player);
    }

    private static bool IsSmartCursorActive()
    {
        return Main.SmartCursorIsUsed || Main.SmartCursorWanted;
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

    private struct SelectionIterator
    {
        public Rectangle Selection { get; private set; }
        private int _index;
        private int _total;

        public bool Completed => HasSelection && _index >= _total;

        private bool HasSelection => _total > 0 && Selection != Rectangle.Empty;

        public void Reset(Rectangle selection)
        {
            Selection = selection;
            _total = selection.Width * selection.Height;
            _index = 0;
        }

        public bool TryGetNext(out int x, out int y)
        {
            if (!HasSelection || _index >= _total)
            {
                x = 0;
                y = 0;
                return false;
            }

            int offset = _index++;
            x = Selection.Left + offset % Selection.Width;
            y = Selection.Top + offset / Selection.Width;
            return true;
        }

        public void Rewind()
        {
            if (_index > 0)
            {
                _index--;
            }
        }

        public bool IsSameSelection(Rectangle selection)
        {
            return HasSelection && Selection == selection;
        }

        public void Clear()
        {
            Selection = Rectangle.Empty;
            _index = 0;
            _total = 0;
        }
    }
}
