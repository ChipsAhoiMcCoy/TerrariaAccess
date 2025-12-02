#nullable enable
using System;
using Microsoft.Xna.Framework;
using global::ScreenReaderMod;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Systems.BuildMode;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class BuildModePlayer : ModPlayer
{
    private bool _buildModeActive;
    private Point? _firstCorner;
    private Point? _secondCorner;
    private bool _lastUseHeld;
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

    private bool HasSelection => _firstCorner.HasValue && _secondCorner.HasValue;

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        if (BuildModeKeybinds.Toggle?.JustPressed ?? false)
        {
            ToggleBuildMode();
        }

        bool placePressed = BuildModeKeybinds.Place?.JustPressed ?? false;
        bool usingGamepad = PlayerInput.UsingGamepad;
        bool quickMountPressed = triggersSet.QuickMount;
        bool quickMountJustPressed = quickMountPressed && !_lastQuickMount;
        _lastQuickMount = quickMountPressed;

        if (_buildModeActive && quickMountPressed)
        {
            placePressed |= quickMountJustPressed;
            SuppressQuickMount(triggersSet);
        }

        // Allow a keyboard fallback: left click marks points when using mouse/keyboard.
        if (!placePressed && !usingGamepad)
        {
            bool mouseLeft = triggersSet.MouseLeft;
            placePressed = mouseLeft && !_lastMouseLeft;
            _lastMouseLeft = mouseLeft;
        }

        if (!_buildModeActive || !placePressed)
        {
            return;
        }

        if (!TryCaptureCursorTile(out Point tile))
        {
            ScreenReaderService.Announce("Build mode: cursor is out of world bounds.");
            return;
        }

        if (!_firstCorner.HasValue)
        {
            _firstCorner = tile;
            _lastAnnouncedCursor = tile;
            ScreenReaderService.Announce($"Build mode: point one set.");
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
        ScreenReaderService.Announce($"Build mode: selection reset. Point one set.");
    }

    public override void PostUpdate()
    {
        AnnounceCursorPositionIfNeeded();
        InGameNarrationSystem.HotbarNarrator.SetExternalSuppression(false);

        if (!_buildModeActive || !HasSelection)
        {
            _lastUseHeld = false;
            ResetActiveAction();
            return;
        }

        bool useHeld = IsUseHeld();
        _lastUseHeld = useHeld;

        if (!useHeld)
        {
            return;
        }

        if (Player.HeldItem is not { } held)
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

        EnsureActiveAction(selection, action, held);
        InGameNarrationSystem.HotbarNarrator.SetExternalSuppression(action is SelectionAction.PlaceTile or SelectionAction.PlaceWall);
        bool acted = ProcessSelectionStep(selection, action, held);
        if (!acted && _selectionIndex >= selection.Width * selection.Height && !_actionCompletedAnnounced)
        {
            AnnounceCompletion(action, selection, held);
            _actionCompletedAnnounced = true;
        }
    }

    private void ToggleBuildMode()
    {
        _buildModeActive = !_buildModeActive;
        if (!_buildModeActive)
        {
            ResetSelection();
            ScreenReaderService.Announce("Build mode disabled.");
            return;
        }

        ScreenReaderService.Announce("Build mode enabled. Press A to mark corners.");
    }

    private void ResetState()
    {
        _buildModeActive = false;
        ResetSelection();
        _lastUseHeld = false;
        _cursorAnnounceCooldown = 0;
        _lastAnnouncedCursor = Point.Zero;
        _lastMouseLeft = false;
        _lastQuickMount = false;
        ResetActiveAction();
    }

    private void ResetSelection()
    {
        _firstCorner = null;
        _secondCorner = null;
        ResetActiveAction();
    }

    private void SuppressQuickMount(TriggersSet triggersSet)
    {
        triggersSet.QuickMount = false;
        Player.controlMount = false;
    }

    private void ResetActiveAction()
    {
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
    }

    private void EnsureActiveAction(Rectangle selection, SelectionAction action, Item held)
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

    private bool ProcessSelectionStep(Rectangle selection, SelectionAction action, Item held)
    {
        int total = selection.Width * selection.Height;
        if (_selectionIndex >= total)
        {
            return false;
        }

        int offset = _selectionIndex;
        int x = selection.Left + offset % selection.Width;
        int y = selection.Top + offset / selection.Width;
        bool advance = HandleSelectionPosition(action, held, x, y);
        if (advance)
        {
            _selectionIndex++;
        }

        return true;
    }

    private bool HandleSelectionPosition(SelectionAction action, Item held, int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 1))
        {
            return true;
        }

        return action switch
        {
            SelectionAction.Clear => HitForClear(x, y, held),
            SelectionAction.PlaceTile => TryPlaceSingleTile(x, y, held),
            SelectionAction.PlaceWall => TryPlaceSingleWall(x, y, held),
            _ => true
        };
    }

    private bool HitForClear(int x, int y, Item held)
    {
        if (_actionCooldown > 0)
        {
            _actionCooldown--;
            return false;
        }

        Tile tile = Framing.GetTileSafely(x, y);
        bool advanced = true;
        int tilePower = Math.Max(held.pick, held.axe);
        int swingDelay = Math.Max(1, held.useTime);

        if (tile.HasTile && tilePower > 0)
        {
            bool hadTile = tile.HasTile;
            Player.PickTile(x, y, tilePower);
            if (hadTile && !Main.tile[x, y].HasTile)
            {
                _tilesCleared++;
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, x, y);
                }
            }
            else if (Main.tile[x, y].HasTile)
            {
                advanced = false; // keep working this tile until it breaks
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
                advanced = false; // keep working this wall until it breaks
            }
        }

        // If there is still something to clear and we have power, stay on this tile.
        if (!advanced && tilePower > 0)
        {
            _actionCooldown = swingDelay;
            return false;
        }

        _actionCooldown = swingDelay;
        return true;
    }

    private bool TryPlaceSingleTile(int x, int y, Item held)
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
            _actionCooldown = Math.Max(1, held.useTime);
            return true;
        }

        WorldGen.PlaceTile(x, y, held.createTile, mute: true, forced: true, plr: Player.whoAmI, style: held.placeStyle);
        Tile after = Main.tile[x, y];
        bool placedNow = after.HasTile && after.TileType == held.createTile && (!beforeHadTile || beforeType != held.createTile);
        if (!placedNow)
        {
            _actionCooldown = Math.Max(1, held.useTime);
            return true;
        }

        _tilesPlaced++;
        if (held.consumable)
        {
            held.stack = Math.Max(0, held.stack - 1);
        }

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendTileSquare(-1, x, y, 1);
        }

        SoundEngine.PlaySound(SoundID.Dig, new Vector2(x * 16, y * 16));
        _actionCooldown = Math.Max(1, held.useTime);
        return true;
    }

    private bool TryPlaceSingleWall(int x, int y, Item held)
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
        if (beforeWall == held.createWall)
        {
            _actionCooldown = Math.Max(1, held.useTime);
            return true;
        }

        WorldGen.PlaceWall(x, y, held.createWall, mute: true);
        int afterWall = Main.tile[x, y].WallType;

        if (afterWall == beforeWall || afterWall != held.createWall)
        {
            _actionCooldown = Math.Max(1, held.useTime);
            return true;
        }

        _wallsPlaced++;
        if (held.consumable)
        {
            held.stack = Math.Max(0, held.stack - 1);
        }

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendTileSquare(-1, x, y, 1);
        }

        SoundEngine.PlaySound(SoundID.Dig, new Vector2(x * 16, y * 16));
        _actionCooldown = Math.Max(1, held.useTime);
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
        // Covers mouse buttons and controller triggers mapped to use item/tile.
        return Player.controlUseItem || Player.controlUseTile || Main.mouseLeft || Main.mouseRight;
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
        _cursorAnnounceCooldown = 6;

        int deltaX = Math.Abs(tile.X - _firstCorner.Value.X);
        int deltaY = Math.Abs(tile.Y - _firstCorner.Value.Y);
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        if (deltaX > 0 && deltaY > 0)
        {
            ScreenReaderService.Announce($"{deltaX} X, {deltaY} Y.");
        }
        else if (deltaX > 0)
        {
            ScreenReaderService.Announce($"{deltaX} X.");
        }
        else
        {
            ScreenReaderService.Announce($"{deltaY} Y.");
        }
    }

    private enum SelectionAction
    {
        None,
        Clear,
        PlaceTile,
        PlaceWall
    }
}
