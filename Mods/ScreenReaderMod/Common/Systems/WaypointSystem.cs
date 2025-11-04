#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems;

public sealed class WaypointSystem : ModSystem
{
    private const string WaypointListKey = "screenReaderWaypoints";
    private const string SelectedIndexKey = "screenReaderSelectedWaypoint";

    private const float ArrivalTileThreshold = 4f;
    private const int MinPingDelayFrames = 12;
    private const int MaxPingDelayFrames = 70;
    private const string MenuTickSoundPath = "Terraria/Sounds/Menu_Tick";

    private static ModKeybind? _nextWaypointKey;
    private static ModKeybind? _previousWaypointKey;
    private static ModKeybind? _createWaypointKey;

    private static readonly List<Waypoint> Waypoints = new();
    private static int _selectedIndex = -1;

    private static UserInterface? _namingInterface;
    private static WaypointNamingState? _namingState;
    private static GameTime? _lastUiGameTime;

    private static Asset<SoundEffect>? _menuTickSound;
    private static readonly List<SoundEffectInstance> ActiveSoundInstances = new();
    private static int _nextPingUpdateFrame = -1;
    private static bool _arrivalAnnounced;

    private struct Waypoint
    {
        public string Name;
        public Vector2 WorldPosition;

        public Waypoint(string name, Vector2 worldPosition)
        {
            Name = name;
            WorldPosition = worldPosition;
        }
    }

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        _nextWaypointKey = KeybindLoader.RegisterKeybind(Mod, "WaypointNext", Keys.OemCloseBrackets);
        _previousWaypointKey = KeybindLoader.RegisterKeybind(Mod, "WaypointPrevious", Keys.OemOpenBrackets);
        _createWaypointKey = KeybindLoader.RegisterKeybind(Mod, "WaypointCreate", Keys.OemPipe);

        _namingInterface = new UserInterface();
    }

    public override void Unload()
    {
        Waypoints.Clear();
        _selectedIndex = -1;

        _namingInterface = null;
        _namingState = null;
        _lastUiGameTime = null;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;

        _nextWaypointKey = null;
        _previousWaypointKey = null;
        _createWaypointKey = null;

        DisposeSoundResources();
    }

    public override void OnWorldUnload()
    {
        Waypoints.Clear();
        _selectedIndex = -1;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        CloseNamingUi();
    }

    public override void LoadWorldData(TagCompound tag)
    {
        Waypoints.Clear();
        _selectedIndex = -1;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;

        if (tag.ContainsKey(WaypointListKey))
        {
            foreach (TagCompound entry in tag.GetList<TagCompound>(WaypointListKey))
            {
                string name = entry.GetString("name");
                float x = entry.GetFloat("x");
                float y = entry.GetFloat("y");
                Waypoints.Add(new Waypoint(name, new Vector2(x, y)));
            }
        }

        if (tag.ContainsKey(SelectedIndexKey))
        {
            _selectedIndex = Math.Clamp(tag.GetInt(SelectedIndexKey), -1, Waypoints.Count - 1);
        }

        if (_selectedIndex >= Waypoints.Count)
        {
            _selectedIndex = Waypoints.Count - 1;
        }
    }

    public override void SaveWorldData(TagCompound tag)
    {
        if (Waypoints.Count > 0)
        {
            List<TagCompound> serialized = new(Waypoints.Count);
            foreach (Waypoint waypoint in Waypoints)
            {
                serialized.Add(new TagCompound
                {
                    ["name"] = waypoint.Name,
                    ["x"] = waypoint.WorldPosition.X,
                    ["y"] = waypoint.WorldPosition.Y,
                });
            }

            tag[WaypointListKey] = serialized;
        }

        if (_selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            tag[SelectedIndexKey] = _selectedIndex;
        }
    }

    public override void PostUpdatePlayers()
    {
        if (Main.dedServ || Main.gameMenu || _namingState is not null)
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
        }
        else
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active || _selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
            }
            else if (Main.gamePaused)
            {
                // hold current schedule while paused
            }
            else
            {
                Waypoint waypoint = Waypoints[_selectedIndex];
                Vector2 targetPosition = waypoint.WorldPosition;
                float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;

                if (distanceTiles <= ArrivalTileThreshold)
                {
                    if (!_arrivalAnnounced)
                    {
                        ScreenReaderService.Announce($"Arrived at {waypoint.Name}");
                        _arrivalAnnounced = true;
                    }

                    _nextPingUpdateFrame = -1;
                }
                else
                {
                    if (_arrivalAnnounced)
                    {
                        _arrivalAnnounced = false;
                    }

                    if (_nextPingUpdateFrame < 0)
                    {
                        _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
                    }
                    else if (Main.GameUpdateCount >= (uint)_nextPingUpdateFrame)
                    {
                        EmitPing(player, targetPosition);
                        _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
                    }
                }
            }
        }

        CleanupFinishedSoundInstances();
    }

    public override void UpdateUI(GameTime gameTime)
    {
        _lastUiGameTime = gameTime;

        if (_namingInterface?.CurrentState is null)
        {
            return;
        }

        _namingInterface.Update(gameTime);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (_namingInterface?.CurrentState is null)
        {
            return;
        }

        int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text", StringComparison.Ordinal));
        if (mouseTextIndex < 0)
        {
            mouseTextIndex = layers.Count;
        }

        layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer("ScreenReaderMod: Waypoint Naming", DrawNamingUi, InterfaceScaleType.UI));
    }

    private static bool DrawNamingUi()
    {
        if (_namingInterface is null)
        {
            return true;
        }

        GameTime time = _lastUiGameTime ?? new GameTime();
        _namingInterface.Draw(Main.spriteBatch, time);
        return true;
    }

    private static void BeginNaming(Player player)
    {
        if (_namingInterface is null)
        {
            return;
        }

        Vector2 worldPosition = player.Center;
        string defaultName = BuildDefaultName(worldPosition);
        _nextPingUpdateFrame = -1;

        _namingState = new WaypointNamingState(defaultName, name =>
        {
            string resolvedName = string.IsNullOrWhiteSpace(name) ? defaultName : name.Trim();
            Waypoints.Add(new Waypoint(resolvedName, worldPosition));
            _selectedIndex = Waypoints.Count - 1;
            RescheduleWaypointPing(player);

            ScreenReaderService.Announce($"Created waypoint {resolvedName}");
            EmitPing(player, worldPosition);
            CloseNamingUi();
        }, () =>
        {
            ScreenReaderService.Announce("Waypoint creation cancelled");
            CloseNamingUi();
            if (_selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
            {
                RescheduleWaypointPing(player);
            }
        });

        _namingInterface.SetState(_namingState);
        Main.NewText("Waypoint naming: type a name, press Enter to save, or Escape to cancel.", Color.LightSkyBlue);
        ScreenReaderService.Announce("Type the waypoint name, then press Enter to save or Escape to cancel.");
    }

    private static void CloseNamingUi()
    {
        if (_namingInterface is null)
        {
            return;
        }

        _namingInterface.SetState(null);
        _namingState = null;
    }

    internal static void HandleKeybinds(Player player)
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        if (player is null || !player.active || player.whoAmI != Main.myPlayer)
        {
            return;
        }

        if (_namingState is not null)
        {
            return;
        }

        if (_createWaypointKey?.JustPressed ?? false)
        {
            BeginNaming(player);
            return;
        }

        if (_nextWaypointKey?.JustPressed ?? false)
        {
            CycleSelection(1, player);
            return;
        }

        if (_previousWaypointKey?.JustPressed ?? false)
        {
            CycleSelection(-1, player);
        }
    }

    private static string BuildDefaultName(Vector2 worldPosition)
    {
        int tileX = (int)Math.Round(worldPosition.X / 16f);
        int tileY = (int)Math.Round(worldPosition.Y / 16f);
        return $"Waypoint {tileX}, {tileY}";
    }

    private static void CycleSelection(int direction, Player player)
    {
        if (Waypoints.Count == 0)
        {
            ScreenReaderService.Announce("No waypoints available");
            return;
        }

        if (_selectedIndex < 0)
        {
            _selectedIndex = direction > 0 ? 0 : Waypoints.Count - 1;
        }
        else
        {
            _selectedIndex += direction;
            if (_selectedIndex >= Waypoints.Count)
            {
                _selectedIndex = 0;
            }
            else if (_selectedIndex < 0)
            {
                _selectedIndex = Waypoints.Count - 1;
            }
        }

        Waypoint waypoint = Waypoints[_selectedIndex];
        EmitPing(player, waypoint.WorldPosition);
        RescheduleWaypointPing(player);
        ScreenReaderService.Announce(ComposeWaypointAnnouncement(waypoint, player));
    }

    private static void RescheduleWaypointPing(Player player)
    {
        if (_selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            return;
        }

        _arrivalAnnounced = false;
        _nextPingUpdateFrame = ComputeNextPingFrame(player, Waypoints[_selectedIndex].WorldPosition);
    }

    private static string ComposeWaypointAnnouncement(Waypoint waypoint, Player player)
    {
        Vector2 offsetWorld = waypoint.WorldPosition - player.Center;
        int offsetX = (int)Math.Round(offsetWorld.X / 16f);
        int offsetY = (int)Math.Round(offsetWorld.Y / 16f);

        string horizontal = offsetX switch
        {
            > 0 => $"{offsetX} right",
            < 0 => $"{Math.Abs(offsetX)} left",
            _ => string.Empty,
        };

        string vertical = offsetY switch
        {
            > 0 => $"{offsetY} down",
            < 0 => $"{Math.Abs(offsetY)} up",
            _ => string.Empty,
        };

        string offsetDescription;
        if (!string.IsNullOrEmpty(horizontal) && !string.IsNullOrEmpty(vertical))
        {
            offsetDescription = $"{horizontal}, {vertical}";
        }
        else if (!string.IsNullOrEmpty(horizontal))
        {
            offsetDescription = horizontal;
        }
        else if (!string.IsNullOrEmpty(vertical))
        {
            offsetDescription = vertical;
        }
        else
        {
            offsetDescription = "here";
        }

        return $"Selected waypoint {waypoint.Name} ({offsetDescription})";
    }

    private static void EmitPing(Player player, Vector2 worldPosition)
    {
        if (Main.dedServ)
        {
            return;
        }

        CleanupFinishedSoundInstances();

        SoundEffect tone = EnsureWaypointSound();
        Vector2 offset = worldPosition - player.Center;

        float pan = MathHelper.Clamp(offset.X / 480f, -1f, 1f);
        float pitch = MathHelper.Clamp(-offset.Y / 320f, -0.6f, 0.6f);
        float baseVolume = MathHelper.Clamp(0.35f + Math.Abs(pitch) * 0.2f, 0f, 0.75f);

        SoundEffectInstance instance = tone.CreateInstance();
        instance.IsLooped = false;
        instance.Volume = baseVolume * Main.soundVolume;
        instance.Pitch = pitch;
        instance.Pan = pan;
        instance.Play();

        ActiveSoundInstances.Add(instance);
    }

    private static int ComputeNextPingFrame(Player player, Vector2 waypointPosition)
    {
        int delay = DeterminePingDelayFrames(player, waypointPosition);
        if (delay <= 0)
        {
            return -1;
        }

        return ComputeNextPingFrameFromDelay(delay);
    }

    private static int DeterminePingDelayFrames(Player player, Vector2 waypointPosition)
    {
        float distanceTiles = Vector2.Distance(player.Center, waypointPosition) / 16f;
        if (distanceTiles <= ArrivalTileThreshold)
        {
            return -1;
        }

        float frames = MathHelper.Clamp(distanceTiles * 2f, MinPingDelayFrames, MaxPingDelayFrames);
        return (int)MathF.Round(frames);
    }

    private static int ComputeNextPingFrameFromDelay(int delayFrames)
    {
        int safeDelay = Math.Max(1, delayFrames);
        ulong current = Main.GameUpdateCount;
        ulong target = current + (ulong)safeDelay;
        if (target > int.MaxValue)
        {
            target = (ulong)int.MaxValue;
        }

        return (int)target;
    }

    private static SoundEffect EnsureWaypointSound()
    {
        if (_menuTickSound is null || !_menuTickSound.IsLoaded)
        {
            _menuTickSound = Main.Assets.Request<SoundEffect>(MenuTickSoundPath, AssetRequestMode.ImmediateLoad);
        }

        return _menuTickSound.Value;
    }

    private static void CleanupFinishedSoundInstances()
    {
        for (int i = ActiveSoundInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = ActiveSoundInstances[i];
            if (instance.State == SoundState.Stopped)
            {
                instance.Dispose();
                ActiveSoundInstances.RemoveAt(i);
            }
        }
    }

    private static void DisposeSoundResources()
    {
        foreach (SoundEffectInstance instance in ActiveSoundInstances)
        {
            try
            {
                instance.Stop();
            }
            catch
            {
                // ignored
            }

            instance.Dispose();
        }

        ActiveSoundInstances.Clear();
        _menuTickSound = null;
    }

    private sealed class WaypointNamingState : UIState
    {
        private readonly Action<string> _submit;
        private readonly Action _cancel;
        private readonly string _defaultText;

        private WaypointTextInput? _textInput;
        private KeyboardState _previousKeyboardState;

        public WaypointNamingState(string defaultText, Action<string> submit, Action cancel)
        {
            _submit = submit;
            _cancel = cancel;
            _defaultText = defaultText;
        }

        public override void OnInitialize()
        {
            UIPanel panel = new()
            {
                Width = { Pixels = 420f },
                Height = { Pixels = 180f },
                HAlign = 0.5f,
                VAlign = 0.35f,
                BackgroundColor = new Color(24, 28, 50) * 0.92f,
                BorderColor = new Color(89, 116, 213),
                PaddingTop = 14f,
            };

            Append(panel);

            UIText header = new("Create Waypoint")
            {
                HAlign = 0.5f,
                Top = { Pixels = 6f },
            };
            panel.Append(header);

            UIText instructions = new("Type a name, Enter to save, Escape to cancel.")
            {
                HAlign = 0.5f,
                Top = { Pixels = 36f },
                TextOriginX = 0.5f,
            };
            panel.Append(instructions);

            _textInput = new WaypointTextInput(_defaultText)
            {
                Top = { Pixels = 70f },
                Left = { Pixels = 20f },
                Width = { Percent = 1f, Pixels = -40f },
                Height = { Pixels = 40f },
            };
            panel.Append(_textInput);

            UITextPanel<string> cancelButton = new("Cancel")
            {
                Top = { Pixels = 126f },
                Left = { Pixels = 20f },
                Width = { Pixels = 120f },
                Height = { Pixels = 34f },
            };
            cancelButton.OnLeftClick += (_, _) => Cancel();
            panel.Append(cancelButton);

            UITextPanel<string> submitButton = new("Save")
            {
                Top = { Pixels = 126f },
                Left = { Pixels = -140f, Percent = 1f },
                Width = { Pixels = 120f },
                Height = { Pixels = 34f },
            };
            submitButton.OnLeftClick += (_, _) => Submit();
            panel.Append(submitButton);
        }

        public override void OnActivate()
        {
            Main.blockInput = true;
            PlayerInput.WritingText = true;

            Player? localPlayer = Main.LocalPlayer;
            if (localPlayer is not null)
            {
                localPlayer.mouseInterface = true;
            }

            _textInput?.Focus();
            _previousKeyboardState = Keyboard.GetState();
        }

        public override void OnDeactivate()
        {
            PlayerInput.WritingText = false;
            Main.blockInput = false;
            _textInput?.Unfocus();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            KeyboardState current = Keyboard.GetState();
            if (_textInput is not null && _textInput.IsFocused)
            {
                if (WasKeyPressed(current, Keys.Enter))
                {
                    Submit();
                }
                else if (WasKeyPressed(current, Keys.Escape))
                {
                    Cancel();
                }
            }

            _previousKeyboardState = current;
        }

        private bool WasKeyPressed(KeyboardState current, Keys key)
        {
            return current.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        private void Submit()
        {
            string text = _textInput?.Text ?? string.Empty;
            _submit(text);
        }

        private void Cancel()
        {
            _cancel();
        }
    }

    private sealed class WaypointTextInput : UIPanel
    {
        private readonly UIText _text;
        private string _currentText;
        private bool _focused;

        public WaypointTextInput(string defaultText)
        {
            _currentText = defaultText;
            BackgroundColor = new Color(54, 64, 104) * 0.95f;
            BorderColor = new Color(146, 182, 255);
            PaddingLeft = 10f;
            PaddingRight = 10f;
            PaddingTop = 6f;
            PaddingBottom = 6f;

            _text = new UIText(defaultText ?? string.Empty)
            {
                TextOriginX = 0f,
                TextOriginY = 0f,
                Top = { Pixels = 0f },
                Left = { Pixels = 0f },
            };
            Append(_text);
        }

        public string Text => _currentText;

        public bool IsFocused => _focused;

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            Focus();
        }

        public void Focus()
        {
            _focused = true;
            Main.clrInput();
            PlayerInput.WritingText = true;
            Main.instance?.HandleIME();
        }

        public void Unfocus()
        {
            _focused = false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (ContainsPoint(Main.MouseScreen) && Main.LocalPlayer is not null)
            {
                Main.LocalPlayer.mouseInterface = true;
            }

            if (_focused)
            {
                Main.instance?.HandleIME();
                string next = Main.GetInputText(_currentText);
                if (!string.Equals(next, _currentText, StringComparison.Ordinal))
                {
                    _currentText = next;
                    _text.SetText(_currentText);
                }
            }
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            base.DrawSelf(spriteBatch);

            if (!_focused)
            {
                return;
            }

            float pulse = (float)Math.Sin(Main.GlobalTimeWrappedHourly * MathHelper.TwoPi) * 0.5f + 0.5f;
            Color caretColor = Color.Lerp(Color.White, new Color(177, 225, 255), pulse);

            CalculatedStyle inner = GetInnerDimensions();
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(_currentText ?? string.Empty);
            float caretX = inner.X + textSize.X;
            float caretY = inner.Y;
            var rectangle = new Rectangle((int)caretX + 2, (int)caretY, 2, FontAssets.MouseText.Value.LineSpacing);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, rectangle, caretColor);
        }
    }
}
