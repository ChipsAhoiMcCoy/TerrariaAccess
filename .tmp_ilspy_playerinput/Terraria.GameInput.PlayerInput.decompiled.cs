using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria.Audio;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.Chat;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Social;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Terraria.GameInput;

public class PlayerInput
{
	public class MiscSettingsTEMP
	{
		public static bool HotbarRadialShouldBeUsed = true;
	}

	public static class SettingsForUI
	{
		public static CursorMode CurrentCursorMode { get; private set; }

		public static bool ShowGamepadHints
		{
			get
			{
				if (!UsingGamepad)
				{
					return SteamDeckIsUsed;
				}
				return true;
			}
		}

		public static bool AllowSecondaryGamepadAim
		{
			get
			{
				if (CurrentCursorMode != CursorMode.Gamepad)
				{
					return !SteamDeckIsUsed;
				}
				return true;
			}
		}

		public static bool PushEquipmentAreaUp
		{
			get
			{
				if (UsingGamepad)
				{
					return !SteamDeckIsUsed;
				}
				return false;
			}
		}

		public static bool ShowGamepadCursor
		{
			get
			{
				if (ControllerHousingCursorActive)
				{
					return true;
				}
				if (!SteamDeckIsUsed)
				{
					return UsingGamepad;
				}
				return CurrentCursorMode == CursorMode.Gamepad;
			}
		}

		public static bool HighlightThingsForMouse
		{
			get
			{
				if (UsingGamepadUI)
				{
					return CurrentCursorMode == CursorMode.Mouse;
				}
				return true;
			}
		}

		public static int FramesSinceLastTimeInMouseMode { get; private set; }

		public static bool PreventHighlightsForGamepad => FramesSinceLastTimeInMouseMode == 0;

		public static void SetCursorMode(CursorMode cursorMode)
		{
			CurrentCursorMode = cursorMode;
			if (CurrentCursorMode == CursorMode.Mouse)
			{
				FramesSinceLastTimeInMouseMode = 0;
			}
		}

		public static void UpdateCounters()
		{
			if (CurrentCursorMode != CursorMode.Mouse)
			{
				FramesSinceLastTimeInMouseMode++;
			}
		}

		public static void TryRevertingToMouseMode()
		{
			if (FramesSinceLastTimeInMouseMode <= 0)
			{
				SetCursorMode(CursorMode.Mouse);
				CurrentInputMode = InputMode.Mouse;
				Triggers.Current.UsedMovementKey = false;
				NavigatorUnCachePosition();
			}
		}
	}

	private struct FastUseItemMemory
	{
		private int _slot;

		private int _itemType;

		private bool _shouldFastUse;

		private bool _isMouseItem;

		private Player _player;

		public bool TryStartForItemSlot(Player player, int itemSlot)
		{
			if (player.UsingOrReusingItem)
			{
				return false;
			}
			if (itemSlot < 0 || itemSlot >= 50)
			{
				Clear();
				return false;
			}
			_player = player;
			_slot = itemSlot;
			_itemType = _player.inventory[itemSlot].type;
			_shouldFastUse = true;
			_isMouseItem = false;
			ItemSlot.PickupItemIntoMouse(player.inventory, 0, itemSlot, player);
			return true;
		}

		public bool TryStartForMouse(Player player)
		{
			if (player.UsingOrReusingItem)
			{
				return false;
			}
			_player = player;
			_slot = -1;
			_itemType = Main.mouseItem.type;
			_shouldFastUse = true;
			_isMouseItem = true;
			return true;
		}

		public void Clear()
		{
			_shouldFastUse = false;
		}

		public bool CanFastUse()
		{
			if (!_shouldFastUse)
			{
				return false;
			}
			if (_isMouseItem)
			{
				return Main.mouseItem.type == _itemType;
			}
			return _player.inventory[_slot].type == _itemType;
		}

		public void EndFastUse()
		{
			if (_shouldFastUse)
			{
				if (!_isMouseItem && _player.inventory[_slot].IsAir)
				{
					Utils.Swap(ref Main.mouseItem, ref _player.inventory[_slot]);
				}
				Clear();
			}
		}
	}

	public static Vector2 RawMouseScale = Vector2.One;

	public static TriggersPack Triggers = new TriggersPack();

	public static List<string> KnownTriggers = new List<string>
	{
		"MouseLeft", "MouseRight", "Up", "Down", "Left", "Right", "Jump", "Throw", "Inventory", "Grapple",
		"SmartSelect", "SmartCursor", "QuickMount", "QuickHeal", "QuickMana", "QuickBuff", "MapZoomIn", "MapZoomOut", "MapAlphaUp", "MapAlphaDown",
		"MapFull", "MapStyle", "Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4", "Hotbar5", "Hotbar6", "Hotbar7", "Hotbar8",
		"Hotbar9", "Hotbar10", "HotbarMinus", "HotbarPlus", "DpadRadial1", "DpadRadial2", "DpadRadial3", "DpadRadial4", "RadialHotbar", "RadialQuickbar",
		"DpadSnap1", "DpadSnap2", "DpadSnap3", "DpadSnap4", "MenuUp", "MenuDown", "MenuLeft", "MenuRight", "LockOn", "ViewZoomIn",
		"ViewZoomOut", "Loadout1", "Loadout2", "Loadout3", "PreviousLoadout", "NextLoadout", "ToggleCameraMode", "ToggleCreativeMenu", "ArmorSetAbility", "Dash"
	};

	private static bool _canReleaseRebindingLock = true;

	private static int _memoOfLastPoint = -1;

	public static int NavigatorRebindingLock;

	public static string BlockedKey = "";

	private static string _listeningTrigger;

	private static InputMode _listeningInputMode;

	public static Dictionary<string, PlayerInputProfile> Profiles = new Dictionary<string, PlayerInputProfile>();

	public static Dictionary<string, PlayerInputProfile> OriginalProfiles = new Dictionary<string, PlayerInputProfile>();

	private static string _selectedProfile;

	private static PlayerInputProfile _currentProfile;

	private static InputMode _inputMode = InputMode.Keyboard;

	private static Buttons[] ButtonsGamepad = (Buttons[])Enum.GetValues(typeof(Buttons));

	public static bool GrappleAndInteractAreShared;

	public static SmartSelectGamepadPointer smartSelectPointer = new SmartSelectGamepadPointer();

	public static bool UseSteamDeckIfPossible;

	private static string _invalidatorCheck = "";

	private static bool _lastActivityState;

	public static MouseState MouseInfo;

	public static MouseState MouseInfoOld;

	public static int MouseX;

	public static int MouseY;

	public static bool LockGamepadTileUseButton = false;

	public static List<string> MouseKeys = new List<string>();

	public static int PreUIX;

	public static int PreUIY;

	public static int PreLockOnX;

	public static int PreLockOnY;

	public static int ScrollWheelValue;

	public static int ScrollWheelValueOld;

	public static int ScrollWheelDelta;

	public static int ScrollWheelDeltaForUI;

	public static bool GamepadAllowScrolling;

	public static int GamepadScrollValue;

	public static Vector2 GamepadThumbstickLeft = Vector2.Zero;

	public static Vector2 GamepadThumbstickRight = Vector2.Zero;

	private static FastUseItemMemory _fastUseMemory;

	private static bool _InBuildingMode;

	private static int _UIPointForBuildingMode = -1;

	public static bool WritingText;

	private static int _originalMouseX;

	private static int _originalMouseY;

	private static int _originalLastMouseX;

	private static int _originalLastMouseY;

	private static int _originalScreenWidth;

	private static int _originalScreenHeight;

	private static List<string> _buttonsLocked = new List<string>();

	public static Vector2 HousingMouseOffset;

	public static bool PreventCursorModeSwappingToGamepad = false;

	public static bool PreventFirstMousePositionGrab = false;

	private static int[] DpadSnapCooldown = new int[4];

	public static bool AllowExecutionOfGamepadInstructions = true;

	private static readonly List<string> _keysToLocalize = new List<string>();

	public static string ListeningTrigger => _listeningTrigger;

	public static bool CurrentlyRebinding => _listeningTrigger != null;

	public static bool InvisibleGamepadInMenus
	{
		get
		{
			if (ControllerHousingCursorActive)
			{
				return false;
			}
			if ((!Main.gameMenu && !Main.ingameOptionsWindow && !Main.playerInventory && Main.player[Main.myPlayer].talkNPC == -1 && Main.player[Main.myPlayer].sign == -1 && Main.InGameUI.CurrentState == null) || _InBuildingMode || !Main.InvisibleCursorForGamepad)
			{
				if (CursorIsBusy)
				{
					return !_InBuildingMode;
				}
				return false;
			}
			return true;
		}
	}

	public static PlayerInputProfile CurrentProfile => _currentProfile;

	public static KeyConfiguration ProfileGamepadUI => CurrentProfile.InputModes[InputMode.XBoxGamepadUI];

	public static InputMode CurrentInputMode
	{
		get
		{
			return _inputMode;
		}
		set
		{
			bool usingGamepad = UsingGamepad;
			_inputMode = value;
			if (UsingGamepad != usingGamepad && PlayerInput.OnBindingChange != null)
			{
				PlayerInput.OnBindingChange();
			}
		}
	}

	public static bool UsingGamepad
	{
		get
		{
			if (CurrentInputMode != InputMode.XBoxGamepad)
			{
				return CurrentInputMode == InputMode.XBoxGamepadUI;
			}
			return true;
		}
	}

	public static bool UsingGamepadUI => CurrentInputMode == InputMode.XBoxGamepadUI;

	public static bool IgnoreMouseInterface
	{
		get
		{
			if (Main.LocalPlayer.UsingOrReusingItem && !UsingGamepad && !Main.gamePaused)
			{
				return true;
			}
			bool flag = UsingGamepad && !UILinkPointNavigator.Available;
			if (flag && SteamDeckIsUsed && SettingsForUI.CurrentCursorMode == CursorMode.Mouse && !Main.mouseRight)
			{
				return false;
			}
			return flag;
		}
	}

	public static bool SteamDeckIsUsed => UseSteamDeckIfPossible;

	public static bool ShouldFastUseItem => _fastUseMemory.CanFastUse();

	public static bool InBuildingMode => _InBuildingMode;

	public static int RealScreenWidth => _originalScreenWidth;

	public static int RealScreenHeight => _originalScreenHeight;

	public static bool CursorIsBusy
	{
		get
		{
			if (!(ItemSlot.CircularRadialOpacity > 0f))
			{
				return ItemSlot.QuicksRadialOpacity > 0f;
			}
			return true;
		}
	}

	public static Vector2 OriginalScreenSize => new Vector2(_originalScreenWidth, _originalScreenHeight);

	public static bool ControllerHousingCursorActive
	{
		get
		{
			if (CurrentInputMode == InputMode.XBoxGamepadUI && UILinkPointNavigator.CurrentPoint >= 600)
			{
				return UILinkPointNavigator.CurrentPoint < 650;
			}
			return false;
		}
	}

	public static Vector2 HousingWorldPosition
	{
		get
		{
			return Main.LocalPlayer.Center + HousingMouseOffset;
		}
		set
		{
			HousingMouseOffset = value - Main.LocalPlayer.Center;
		}
	}

	public static Vector2 HousingScreenPosition => HousingWorldPosition.ToScreenPosition();

	public static event Action OnBindingChange;

	public static event Action OnActionableInput;

	public static void ListenFor(string triggerName, InputMode inputmode)
	{
		_listeningTrigger = triggerName;
		_listeningInputMode = inputmode;
	}

	private static bool InvalidateKeyboardSwap()
	{
		if (_invalidatorCheck.Length == 0)
		{
			return false;
		}
		string text = "";
		List<Keys> pressedKeys = GetPressedKeys();
		for (int i = 0; i < pressedKeys.Count; i++)
		{
			text = string.Concat(text, pressedKeys[i], ", ");
		}
		if (text == _invalidatorCheck)
		{
			return true;
		}
		_invalidatorCheck = "";
		return false;
	}

	public static void ResetInputsOnActiveStateChange()
	{
		bool allowInputProcessing = FocusHelper.AllowInputProcessing;
		if (_lastActivityState != allowInputProcessing)
		{
			MouseInfo = default(MouseState);
			MouseInfoOld = default(MouseState);
			Main.keyState = Keyboard.GetState();
			Main.inputText = Keyboard.GetState();
			Main.oldInputText = Keyboard.GetState();
			Main.keyCount = 0;
			Triggers.Reset();
			Triggers.Reset();
			string text = "";
			List<Keys> pressedKeys = GetPressedKeys();
			for (int i = 0; i < pressedKeys.Count; i++)
			{
				text = string.Concat(text, pressedKeys[i], ", ");
			}
			_invalidatorCheck = text;
		}
		_lastActivityState = allowInputProcessing;
	}

	public static List<Keys> GetPressedKeys()
	{
		List<Keys> list = Main.keyState.GetPressedKeys().ToList();
		for (int num = list.Count - 1; num >= 0; num--)
		{
			if (list[num] == Keys.None || list[num] == Keys.Kanji)
			{
				list.RemoveAt(num);
			}
		}
		return list;
	}

	public static void TryEnteringFastUseModeForInventorySlot(int inventorySlot)
	{
		_fastUseMemory.TryStartForItemSlot(Main.LocalPlayer, inventorySlot);
	}

	public static void TryEnteringFastUseModeForMouseItem()
	{
		_fastUseMemory.TryStartForMouse(Main.LocalPlayer);
	}

	public static void TryEndingFastUse()
	{
		_fastUseMemory.EndFastUse();
	}

	public static void EnterBuildingMode()
	{
		if (Main.LocalPlayer.UsingOrReusingItem)
		{
			return;
		}
		SoundEngine.PlaySound(10);
		_InBuildingMode = true;
		_UIPointForBuildingMode = UILinkPointNavigator.CurrentPoint;
		if (Main.mouseItem.IsAir)
		{
			int uIPointForBuildingMode = _UIPointForBuildingMode;
			if (uIPointForBuildingMode < 50 && uIPointForBuildingMode >= 0 && Main.player[Main.myPlayer].inventory[uIPointForBuildingMode].stack > 0)
			{
				Utils.Swap(ref Main.mouseItem, ref Main.player[Main.myPlayer].inventory[uIPointForBuildingMode]);
			}
		}
	}

	public static void ExitBuildingMode(bool quiet = false)
	{
		if (!quiet)
		{
			SoundEngine.PlaySound(11);
		}
		_InBuildingMode = false;
		UILinkPointNavigator.ChangePoint(_UIPointForBuildingMode);
		if (Main.mouseItem.stack > 0 && Main.player[Main.myPlayer].itemAnimation == 0)
		{
			int uIPointForBuildingMode = _UIPointForBuildingMode;
			if (uIPointForBuildingMode < 50 && uIPointForBuildingMode >= 0 && Main.player[Main.myPlayer].inventory[uIPointForBuildingMode].stack <= 0)
			{
				Utils.Swap(ref Main.mouseItem, ref Main.player[Main.myPlayer].inventory[uIPointForBuildingMode]);
			}
		}
		_UIPointForBuildingMode = -1;
	}

	public static void VerifyBuildingMode()
	{
		if (_InBuildingMode)
		{
			Player obj = Main.player[Main.myPlayer];
			bool flag = false;
			if (Main.mouseItem.stack <= 0)
			{
				flag = true;
			}
			if (obj.dead)
			{
				flag = true;
			}
			if (flag)
			{
				ExitBuildingMode();
			}
		}
	}

	public static void SetSelectedProfile(string name)
	{
		if (Profiles.ContainsKey(name))
		{
			_selectedProfile = name;
			_currentProfile = Profiles[_selectedProfile];
		}
	}

	public static void Initialize()
	{
		Main.InputProfiles.OnProcessText += PrettyPrintProfiles;
		Player.Hooks.OnEnterWorld += Hook_OnEnterWorld;
		PlayerInputProfile playerInputProfile = new PlayerInputProfile("Redigit's Pick");
		playerInputProfile.Initialize(PresetProfiles.Redigit);
		Profiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Yoraiz0r's Pick");
		playerInputProfile.Initialize(PresetProfiles.Yoraiz0r);
		Profiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Console (Playstation)");
		playerInputProfile.Initialize(PresetProfiles.ConsolePS);
		Profiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Console (Xbox)");
		playerInputProfile.Initialize(PresetProfiles.ConsoleXBox);
		Profiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Custom");
		playerInputProfile.Initialize(PresetProfiles.Redigit);
		Profiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Redigit's Pick");
		playerInputProfile.Initialize(PresetProfiles.Redigit);
		OriginalProfiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Yoraiz0r's Pick");
		playerInputProfile.Initialize(PresetProfiles.Yoraiz0r);
		OriginalProfiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Console (Playstation)");
		playerInputProfile.Initialize(PresetProfiles.ConsolePS);
		OriginalProfiles.Add(playerInputProfile.Name, playerInputProfile);
		playerInputProfile = new PlayerInputProfile("Console (Xbox)");
		playerInputProfile.Initialize(PresetProfiles.ConsoleXBox);
		OriginalProfiles.Add(playerInputProfile.Name, playerInputProfile);
		SetSelectedProfile("Custom");
		Triggers.Initialize();
	}

	public static void Hook_OnEnterWorld(Player player)
	{
		if (player.whoAmI == Main.myPlayer)
		{
			Main.SmartCursorWanted_GamePad = true;
		}
	}

	public static bool Save()
	{
		Main.InputProfiles.Clear();
		Main.InputProfiles.Put("Selected Profile", _selectedProfile);
		foreach (KeyValuePair<string, PlayerInputProfile> profile in Profiles)
		{
			Main.InputProfiles.Put(profile.Value.Name, profile.Value.Save());
		}
		return Main.InputProfiles.Save();
	}

	public static void Load()
	{
		Main.InputProfiles.Load();
		Dictionary<string, PlayerInputProfile> dictionary = new Dictionary<string, PlayerInputProfile>();
		string currentValue = null;
		Main.InputProfiles.Get("Selected Profile", ref currentValue);
		List<string> allKeys = Main.InputProfiles.GetAllKeys();
		for (int i = 0; i < allKeys.Count; i++)
		{
			string text = allKeys[i];
			if (text == "Selected Profile" || string.IsNullOrEmpty(text))
			{
				continue;
			}
			Dictionary<string, object> currentValue2 = new Dictionary<string, object>();
			Main.InputProfiles.Get(text, ref currentValue2);
			if (currentValue2.Count > 0)
			{
				PlayerInputProfile playerInputProfile = new PlayerInputProfile(text);
				playerInputProfile.Initialize(PresetProfiles.None);
				if (playerInputProfile.Load(currentValue2))
				{
					dictionary.Add(text, playerInputProfile);
				}
			}
		}
		if (dictionary.Count > 0)
		{
			Profiles = dictionary;
			if (!string.IsNullOrEmpty(currentValue) && Profiles.ContainsKey(currentValue))
			{
				SetSelectedProfile(currentValue);
			}
			else
			{
				SetSelectedProfile(Profiles.Keys.First());
			}
		}
	}

	public static void ManageVersion_1_3()
	{
		PlayerInputProfile playerInputProfile = Profiles["Custom"];
		string[,] array = new string[20, 2]
		{
			{ "KeyUp", "Up" },
			{ "KeyDown", "Down" },
			{ "KeyLeft", "Left" },
			{ "KeyRight", "Right" },
			{ "KeyJump", "Jump" },
			{ "KeyThrowItem", "Throw" },
			{ "KeyInventory", "Inventory" },
			{ "KeyQuickHeal", "QuickHeal" },
			{ "KeyQuickMana", "QuickMana" },
			{ "KeyQuickBuff", "QuickBuff" },
			{ "KeyUseHook", "Grapple" },
			{ "KeyAutoSelect", "SmartSelect" },
			{ "KeySmartCursor", "SmartCursor" },
			{ "KeyMount", "QuickMount" },
			{ "KeyMapStyle", "MapStyle" },
			{ "KeyFullscreenMap", "MapFull" },
			{ "KeyMapZoomIn", "MapZoomIn" },
			{ "KeyMapZoomOut", "MapZoomOut" },
			{ "KeyMapAlphaUp", "MapAlphaUp" },
			{ "KeyMapAlphaDown", "MapAlphaDown" }
		};
		for (int i = 0; i < array.GetLength(0); i++)
		{
			string currentValue = null;
			Main.Configuration.Get(array[i, 0], ref currentValue);
			if (currentValue != null)
			{
				playerInputProfile.InputModes[InputMode.Keyboard].KeyStatus[array[i, 1]] = new List<string> { currentValue };
				playerInputProfile.InputModes[InputMode.KeyboardUI].KeyStatus[array[i, 1]] = new List<string> { currentValue };
			}
		}
	}

	public static void LockGamepadButtons(string TriggerName)
	{
		List<string> value = null;
		KeyConfiguration value2 = null;
		if (CurrentProfile.InputModes.TryGetValue(CurrentInputMode, out value2) && value2.KeyStatus.TryGetValue(TriggerName, out value))
		{
			_buttonsLocked.AddRange(value);
		}
	}

	public static bool IsGamepadButtonLockedFromUse(string keyName)
	{
		return _buttonsLocked.Contains(keyName);
	}

	public static void UpdateInput()
	{
		SettingsForUI.UpdateCounters();
		Triggers.Reset();
		ScrollWheelValueOld = ScrollWheelValue;
		ScrollWheelValue = 0;
		GamepadThumbstickLeft = Vector2.Zero;
		GamepadThumbstickRight = Vector2.Zero;
		GrappleAndInteractAreShared = (UsingGamepad || SteamDeckIsUsed) && CurrentProfile.InputModes[InputMode.XBoxGamepad].DoGrappleAndInteractShareTheSameKey;
		if (InBuildingMode && !UsingGamepad)
		{
			ExitBuildingMode();
		}
		if (_canReleaseRebindingLock && NavigatorRebindingLock > 0)
		{
			NavigatorRebindingLock--;
			Triggers.Current.UsedMovementKey = false;
			if (NavigatorRebindingLock == 0 && _memoOfLastPoint != -1)
			{
				UIManageControls.ForceMoveTo = _memoOfLastPoint;
				_memoOfLastPoint = -1;
			}
		}
		_canReleaseRebindingLock = true;
		VerifyBuildingMode();
		MouseInput();
		int num = (int)(0u | (KeyboardInput() ? 1u : 0u)) | (GamePadInput() ? 1 : 0);
		Triggers.Update();
		PostInput();
		ScrollWheelDelta = ScrollWheelValue - ScrollWheelValueOld;
		ScrollWheelDeltaForUI = ScrollWheelDelta;
		WritingText = false;
		UpdateMainMouse();
		Main.mouseLeft = Triggers.Current.MouseLeft;
		Main.mouseRight = Triggers.Current.MouseRight;
		CacheZoomableValues();
		if (num != 0 && PlayerInput.OnActionableInput != null)
		{
			PlayerInput.OnActionableInput();
		}
	}

	public static void UpdateMainMouse()
	{
		Main.lastMouseX = Main.mouseX;
		Main.lastMouseY = Main.mouseY;
		Main.mouseX = MouseX;
		Main.mouseY = MouseY;
	}

	public static void CacheZoomableValues()
	{
		CacheOriginalInput();
		CacheOriginalScreenDimensions();
	}

	public static void CacheMousePositionForZoom()
	{
		float num = 1f;
		_originalMouseX = (int)((float)Main.mouseX * num);
		_originalMouseY = (int)((float)Main.mouseY * num);
	}

	private static void CacheOriginalInput()
	{
		_originalMouseX = Main.mouseX;
		_originalMouseY = Main.mouseY;
		_originalLastMouseX = Main.lastMouseX;
		_originalLastMouseY = Main.lastMouseY;
	}

	public static void CacheOriginalScreenDimensions()
	{
		_originalScreenWidth = Main.screenWidth;
		_originalScreenHeight = Main.screenHeight;
	}

	public static void UpdateHousingCursor()
	{
		if (ControllerHousingCursorActive)
		{
			Vector2 gamepadThumbstickRight = GamepadThumbstickRight;
			Vector2 vector = gamepadThumbstickRight;
			if (vector != Vector2.Zero)
			{
				vector.Normalize();
			}
			_ = Main.LocalPlayer.Center;
			float m = Main.GameViewMatrix.ZoomMatrix.M11;
			if (gamepadThumbstickRight != Vector2.Zero)
			{
				Vector2 vector2 = new Vector2(10f);
				Vector2 vector3 = gamepadThumbstickRight * vector2 * m;
				HousingMouseOffset += vector3;
			}
			Vector2 vector4 = new Vector2(20f, 20f);
			HousingWorldPosition = Vector2.Clamp(max: (new Vector2(Main.screenWidth, Main.screenHeight) - vector4).ScreenToWorldPosition(), value1: HousingWorldPosition, min: vector4.ScreenToWorldPosition());
		}
		else
		{
			HousingMouseOffset = Vector2.Zero;
		}
	}

	private static bool GamePadInput()
	{
		bool flag = false;
		ScrollWheelValue += GamepadScrollValue;
		GamePadState gamePadState = default(GamePadState);
		bool flag2 = false;
		for (int i = 0; i < 4; i++)
		{
			GamePadState state = GamePad.GetState((PlayerIndex)i);
			if (state.IsConnected)
			{
				flag2 = true;
				gamePadState = state;
				break;
			}
		}
		if (Main.SettingBlockGamepadsEntirely)
		{
			return false;
		}
		if (!flag2)
		{
			return false;
		}
		if (!FocusHelper.AllowInputProcessingForGamepad)
		{
			return false;
		}
		Player player = Main.player[Main.myPlayer];
		bool flag3 = UILinkPointNavigator.Available && !InBuildingMode;
		InputMode inputMode = InputMode.XBoxGamepad;
		if (Main.gameMenu || flag3 || player.talkNPC != -1 || player.sign != -1 || IngameFancyUI.CanCover())
		{
			inputMode = InputMode.XBoxGamepadUI;
		}
		if (!Main.gameMenu && InBuildingMode)
		{
			inputMode = InputMode.XBoxGamepad;
		}
		if (CurrentInputMode == InputMode.XBoxGamepad && inputMode == InputMode.XBoxGamepadUI)
		{
			flag = true;
		}
		if (CurrentInputMode == InputMode.XBoxGamepadUI && inputMode == InputMode.XBoxGamepad)
		{
			flag = true;
		}
		if (flag)
		{
			CurrentInputMode = inputMode;
		}
		KeyConfiguration keyConfiguration = CurrentProfile.InputModes[inputMode];
		int num = 2145386496;
		for (int j = 0; j < ButtonsGamepad.Length; j++)
		{
			if ((int)((uint)num & (uint)ButtonsGamepad[j]) > 0)
			{
				continue;
			}
			string text = ButtonsGamepad[j].ToString();
			bool flag4 = _buttonsLocked.Contains(text);
			if (gamePadState.IsButtonDown(ButtonsGamepad[j]))
			{
				if (!flag4)
				{
					if (CheckRebindingProcessGamepad(text))
					{
						return false;
					}
					keyConfiguration.Processkey(Triggers.Current, text, inputMode);
					flag = true;
				}
			}
			else
			{
				_buttonsLocked.Remove(text);
			}
		}
		GamepadThumbstickLeft = gamePadState.ThumbSticks.Left * new Vector2(1f, -1f) * new Vector2(CurrentProfile.LeftThumbstickInvertX.ToDirectionInt() * -1, CurrentProfile.LeftThumbstickInvertY.ToDirectionInt() * -1);
		GamepadThumbstickRight = gamePadState.ThumbSticks.Right * new Vector2(1f, -1f) * new Vector2(CurrentProfile.RightThumbstickInvertX.ToDirectionInt() * -1, CurrentProfile.RightThumbstickInvertY.ToDirectionInt() * -1);
		Vector2 gamepadThumbstickRight = GamepadThumbstickRight;
		Vector2 gamepadThumbstickLeft = GamepadThumbstickLeft;
		Vector2 vector = gamepadThumbstickRight;
		if (vector != Vector2.Zero)
		{
			vector.Normalize();
		}
		Vector2 vector2 = gamepadThumbstickLeft;
		if (vector2 != Vector2.Zero)
		{
			vector2.Normalize();
		}
		float num2 = 0.6f;
		float triggersDeadzone = CurrentProfile.TriggersDeadzone;
		if (inputMode == InputMode.XBoxGamepadUI)
		{
			num2 = 0.4f;
			if (GamepadAllowScrolling)
			{
				GamepadScrollValue -= (int)(gamepadThumbstickRight.Y * 16f);
			}
			GamepadAllowScrolling = false;
		}
		if (Vector2.Dot(-Vector2.UnitX, vector2) >= num2 && gamepadThumbstickLeft.X < 0f - CurrentProfile.LeftThumbstickDeadzoneX)
		{
			if (CheckRebindingProcessGamepad(Buttons.LeftThumbstickLeft.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.LeftThumbstickLeft.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(Vector2.UnitX, vector2) >= num2 && gamepadThumbstickLeft.X > CurrentProfile.LeftThumbstickDeadzoneX)
		{
			if (CheckRebindingProcessGamepad(Buttons.LeftThumbstickRight.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.LeftThumbstickRight.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(-Vector2.UnitY, vector2) >= num2 && gamepadThumbstickLeft.Y < 0f - CurrentProfile.LeftThumbstickDeadzoneY)
		{
			if (CheckRebindingProcessGamepad(Buttons.LeftThumbstickUp.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.LeftThumbstickUp.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(Vector2.UnitY, vector2) >= num2 && gamepadThumbstickLeft.Y > CurrentProfile.LeftThumbstickDeadzoneY)
		{
			if (CheckRebindingProcessGamepad(Buttons.LeftThumbstickDown.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.LeftThumbstickDown.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(-Vector2.UnitX, vector) >= num2 && gamepadThumbstickRight.X < 0f - CurrentProfile.RightThumbstickDeadzoneX)
		{
			if (CheckRebindingProcessGamepad(Buttons.RightThumbstickLeft.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.RightThumbstickLeft.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(Vector2.UnitX, vector) >= num2 && gamepadThumbstickRight.X > CurrentProfile.RightThumbstickDeadzoneX)
		{
			if (CheckRebindingProcessGamepad(Buttons.RightThumbstickRight.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.RightThumbstickRight.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(-Vector2.UnitY, vector) >= num2 && gamepadThumbstickRight.Y < 0f - CurrentProfile.RightThumbstickDeadzoneY)
		{
			if (CheckRebindingProcessGamepad(Buttons.RightThumbstickUp.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.RightThumbstickUp.ToString(), inputMode);
			flag = true;
		}
		if (Vector2.Dot(Vector2.UnitY, vector) >= num2 && gamepadThumbstickRight.Y > CurrentProfile.RightThumbstickDeadzoneY)
		{
			if (CheckRebindingProcessGamepad(Buttons.RightThumbstickDown.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.RightThumbstickDown.ToString(), inputMode);
			flag = true;
		}
		if (gamePadState.Triggers.Left > triggersDeadzone)
		{
			if (CheckRebindingProcessGamepad(Buttons.LeftTrigger.ToString()))
			{
				return false;
			}
			keyConfiguration.Processkey(Triggers.Current, Buttons.LeftTrigger.ToString(), inputMode);
			flag = true;
		}
		if (gamePadState.Triggers.Right > triggersDeadzone)
		{
			string newKey = Buttons.RightTrigger.ToString();
			if (CheckRebindingProcessGamepad(newKey))
			{
				return false;
			}
			if (inputMode == InputMode.XBoxGamepadUI && SteamDeckIsUsed && SettingsForUI.CurrentCursorMode == CursorMode.Mouse)
			{
				Triggers.Current.MouseLeft = true;
			}
			else
			{
				keyConfiguration.Processkey(Triggers.Current, newKey, inputMode);
				flag = true;
			}
		}
		bool flag5 = ItemID.Sets.GamepadWholeScreenUseRange[player.inventory[player.selectedItem].type] || player.scope;
		Item item = player.inventory[player.selectedItem];
		bool flag6 = false;
		bool flag7 = !Main.gameMenu && !flag3 && Main.SmartCursorWanted;
		bool num3 = player.rulerGrid && player.builderAccStatus[1] == 0;
		bool flag8 = player.rulerLine && player.builderAccStatus[0] == 0;
		bool flag9 = num3 || flag8;
		if (!flag7 && flag9)
		{
			flag5 = true;
		}
		int num4 = item.tileBoost + ItemID.Sets.GamepadExtraRange[item.type];
		if (player.yoyoString && ItemID.Sets.Yoyo[item.type])
		{
			num4 += 5;
		}
		else if (item.createTile < 0 && item.createWall <= 0 && item.shoot > 0)
		{
			num4 += 10;
		}
		else if (player.controlTorch)
		{
			num4++;
		}
		if (item.createWall > 0 || item.createTile > 0 || item.tileWand > 0)
		{
			num4 += player.blockRange;
		}
		if (flag5)
		{
			num4 += 30;
		}
		if (player.mount.Active && player.mount.Type == 8)
		{
			num4 = 10;
		}
		if (!CursorIsBusy)
		{
			bool flag10 = Main.mapFullscreen || (!Main.gameMenu && !flag3);
			int num5 = Main.screenWidth / 2;
			int num6 = Main.screenHeight / 2;
			if (!Main.mapFullscreen && flag10 && !flag5)
			{
				Point point = Main.ReverseGravitySupport(player.Center - Main.screenPosition).ToPoint();
				num5 = point.X;
				num6 = point.Y;
			}
			if (player.velocity == Vector2.Zero && gamepadThumbstickLeft == Vector2.Zero && gamepadThumbstickRight == Vector2.Zero && flag7)
			{
				num5 += player.direction * 10;
			}
			float m = Main.GameViewMatrix.ZoomMatrix.M11;
			smartSelectPointer.UpdateSize(new Vector2(Player.tileRangeX * 16 + num4 * 16, Player.tileRangeY * 16 + num4 * 16) * m);
			if (flag5)
			{
				smartSelectPointer.UpdateSize(new Vector2(Math.Max(Main.screenWidth, Main.screenHeight) / 2));
			}
			smartSelectPointer.UpdateCenter(new Vector2(num5, num6));
			if (gamepadThumbstickRight != Vector2.Zero && flag10)
			{
				Vector2 vector3 = new Vector2(8f);
				if (!Main.gameMenu && Main.mapFullscreen)
				{
					vector3 = new Vector2(16f);
				}
				if (flag7)
				{
					vector3 = new Vector2((Player.tileRangeX + num4) * 16, (Player.tileRangeY + num4) * 16);
					if (flag5)
					{
						vector3 = new Vector2(Math.Max(Main.screenWidth, Main.screenHeight) / 2);
					}
				}
				else if (!Main.mapFullscreen)
				{
					if (player.inventory[player.selectedItem].mech)
					{
						vector3 += Vector2.Zero;
					}
					else
					{
						vector3 += new Vector2(num4) / 4f;
					}
				}
				float m2 = Main.GameViewMatrix.ZoomMatrix.M11;
				Vector2 vector4 = gamepadThumbstickRight * vector3 * m2;
				int num7 = MouseX - num5;
				int num8 = MouseY - num6;
				if (flag7)
				{
					num7 = 0;
					num8 = 0;
				}
				num7 += (int)vector4.X;
				num8 += (int)vector4.Y;
				MouseX = num7 + num5;
				MouseY = num8 + num6;
				flag = true;
				flag6 = true;
				SettingsForUI.SetCursorMode(CursorMode.Gamepad);
			}
			bool allowSecondaryGamepadAim = SettingsForUI.AllowSecondaryGamepadAim;
			if (gamepadThumbstickLeft != Vector2.Zero && flag10)
			{
				float num9 = 8f;
				if (!Main.gameMenu && Main.mapFullscreen)
				{
					num9 = 3f;
				}
				if (Main.mapFullscreen)
				{
					Vector2 vector5 = gamepadThumbstickLeft * num9;
					Main.mapFullscreenPos += vector5 * num9 * (1f / Main.mapFullscreenScale);
					flag = true;
				}
				else if (!flag6 && Main.SmartCursorWanted && allowSecondaryGamepadAim)
				{
					float m3 = Main.GameViewMatrix.ZoomMatrix.M11;
					Vector2 vector6 = gamepadThumbstickLeft * new Vector2((Player.tileRangeX + num4) * 16, (Player.tileRangeY + num4) * 16) * m3;
					if (flag5)
					{
						vector6 = new Vector2(Math.Max(Main.screenWidth, Main.screenHeight) / 2) * gamepadThumbstickLeft;
					}
					int num10 = (int)vector6.X;
					int num11 = (int)vector6.Y;
					MouseX = num10 + num5;
					MouseY = num11 + num6;
					flag6 = true;
				}
				flag = true;
			}
			if (CurrentInputMode == InputMode.XBoxGamepad)
			{
				HandleDpadSnap();
				if (SettingsForUI.AllowSecondaryGamepadAim)
				{
					int num12 = MouseX - num5;
					int num13 = MouseY - num6;
					if (!Main.gameMenu && !flag3)
					{
						if (flag5 && !Main.mapFullscreen)
						{
							float num14 = 1f;
							int num15 = Main.screenWidth / 2;
							int num16 = Main.screenHeight / 2;
							num12 = (int)Utils.Clamp(num12, (float)(-num15) * num14, (float)num15 * num14);
							num13 = (int)Utils.Clamp(num13, (float)(-num16) * num14, (float)num16 * num14);
						}
						else
						{
							float num17 = 0f;
							if (player.HeldItem.createTile >= 0 || player.HeldItem.createWall > 0 || player.HeldItem.tileWand >= 0)
							{
								num17 = 0.5f;
							}
							float m4 = Main.GameViewMatrix.ZoomMatrix.M11;
							float num18 = (0f - ((float)(Player.tileRangeY + num4) - num17)) * 16f * m4;
							float max = ((float)(Player.tileRangeY + num4) - num17) * 16f * m4;
							num18 -= (float)(player.height / 16 / 2 * 16);
							num12 = (int)Utils.Clamp(num12, (0f - ((float)(Player.tileRangeX + num4) - num17)) * 16f * m4, ((float)(Player.tileRangeX + num4) - num17) * 16f * m4);
							num13 = (int)Utils.Clamp(num13, num18, max);
						}
						if (flag7 && (!flag || flag5))
						{
							float num19 = 0.81f;
							if (flag5)
							{
								num19 = 0.95f;
							}
							num12 = (int)((float)num12 * num19);
							num13 = (int)((float)num13 * num19);
						}
					}
					else
					{
						num12 = Utils.Clamp(num12, -num5 + 10, num5 - 10);
						num13 = Utils.Clamp(num13, -num6 + 10, num6 - 10);
					}
					MouseX = num12 + num5;
					MouseY = num13 + num6;
				}
			}
		}
		if (flag)
		{
			CurrentInputMode = inputMode;
		}
		if (CurrentInputMode != InputMode.XBoxGamepadUI && flag)
		{
			PreventCursorModeSwappingToGamepad = true;
		}
		if (!flag)
		{
			PreventCursorModeSwappingToGamepad = false;
		}
		if (CurrentInputMode == InputMode.XBoxGamepadUI && flag && !PreventCursorModeSwappingToGamepad)
		{
			SettingsForUI.SetCursorMode(CursorMode.Gamepad);
		}
		return flag;
	}

	private static void MouseInput()
	{
		bool flag = false;
		MouseInfoOld = MouseInfo;
		MouseInfo = Mouse.GetState();
		ScrollWheelValue += MouseInfo.ScrollWheelValue;
		if (MouseInfo.X != MouseInfoOld.X || MouseInfo.Y != MouseInfoOld.Y || MouseInfo.ScrollWheelValue != MouseInfoOld.ScrollWheelValue)
		{
			MouseX = (int)((float)MouseInfo.X * RawMouseScale.X);
			MouseY = (int)((float)MouseInfo.Y * RawMouseScale.Y);
			if (!PreventFirstMousePositionGrab)
			{
				flag = true;
				SettingsForUI.SetCursorMode(CursorMode.Mouse);
			}
			PreventFirstMousePositionGrab = false;
		}
		MouseKeys.Clear();
		if (FocusHelper.AllowInputProcessing)
		{
			if (MouseInfo.LeftButton == ButtonState.Pressed)
			{
				MouseKeys.Add("Mouse1");
				flag = true;
			}
			if (MouseInfo.RightButton == ButtonState.Pressed)
			{
				MouseKeys.Add("Mouse2");
				flag = true;
			}
			if (MouseInfo.MiddleButton == ButtonState.Pressed)
			{
				MouseKeys.Add("Mouse3");
				flag = true;
			}
			if (MouseInfo.XButton1 == ButtonState.Pressed)
			{
				MouseKeys.Add("Mouse4");
				flag = true;
			}
			if (MouseInfo.XButton2 == ButtonState.Pressed)
			{
				MouseKeys.Add("Mouse5");
				flag = true;
			}
		}
		if (flag)
		{
			CurrentInputMode = InputMode.Mouse;
			Triggers.Current.UsedMovementKey = false;
		}
	}

	private static bool KeyboardInput()
	{
		bool flag = false;
		bool flag2 = false;
		List<Keys> pressedKeys = GetPressedKeys();
		DebugKeys(pressedKeys);
		if (pressedKeys.Count == 0 && MouseKeys.Count == 0)
		{
			Main.blockKey = Keys.None.ToString();
			return false;
		}
		for (int i = 0; i < pressedKeys.Count; i++)
		{
			if (pressedKeys[i] == Keys.LeftShift || pressedKeys[i] == Keys.RightShift)
			{
				flag = true;
			}
			else if (pressedKeys[i] == Keys.LeftAlt || pressedKeys[i] == Keys.RightAlt)
			{
				flag2 = true;
			}
			Main.ChromaPainter.PressKey(pressedKeys[i]);
		}
		if (Main.blockKey != Keys.None.ToString())
		{
			bool flag3 = false;
			for (int j = 0; j < pressedKeys.Count; j++)
			{
				if (pressedKeys[j].ToString() == Main.blockKey)
				{
					pressedKeys[j] = Keys.None;
					flag3 = true;
				}
			}
			if (!flag3)
			{
				Main.blockKey = Keys.None.ToString();
			}
		}
		KeyConfiguration keyConfiguration = CurrentProfile.InputModes[InputMode.Keyboard];
		if (Main.gameMenu && !WritingText)
		{
			keyConfiguration = CurrentProfile.InputModes[InputMode.KeyboardUI];
		}
		List<string> list = new List<string>(pressedKeys.Count);
		for (int k = 0; k < pressedKeys.Count; k++)
		{
			list.Add(pressedKeys[k].ToString());
		}
		if (WritingText)
		{
			list.Clear();
		}
		int count = list.Count;
		list.AddRange(MouseKeys);
		bool flag4 = false;
		for (int l = 0; l < list.Count; l++)
		{
			if (l < count && pressedKeys[l] == Keys.None)
			{
				continue;
			}
			string newKey = list[l];
			if (!(list[l] == Keys.Tab.ToString()) || !((flag && SocialAPI.Mode == SocialMode.Steam) || flag2))
			{
				if (CheckRebindingProcessKeyboard(newKey))
				{
					return false;
				}
				_ = Main.oldKeyState;
				if (l >= count || !Main.oldKeyState.IsKeyDown(pressedKeys[l]))
				{
					keyConfiguration.Processkey(Triggers.Current, newKey, InputMode.Keyboard);
				}
				else
				{
					keyConfiguration.CopyKeyState(Triggers.Old, Triggers.Current, newKey);
				}
				if (l >= count || pressedKeys[l] != Keys.None)
				{
					flag4 = true;
				}
			}
		}
		if (flag4)
		{
			CurrentInputMode = InputMode.Keyboard;
		}
		return flag4;
	}

	private static void DebugKeys(List<Keys> keys)
	{
	}

	private static void FixDerpedRebinds()
	{
		List<string> list = new List<string> { "MouseLeft", "MouseRight", "Inventory" };
		foreach (InputMode value in Enum.GetValues(typeof(InputMode)))
		{
			if (value == InputMode.Mouse)
			{
				continue;
			}
			FixKeysConflict(value, list);
			foreach (string item in list)
			{
				if (CurrentProfile.InputModes[value].KeyStatus[item].Count < 1)
				{
					ResetKeyBinding(value, item);
				}
			}
		}
	}

	private static void FixKeysConflict(InputMode inputMode, List<string> triggers)
	{
		for (int i = 0; i < triggers.Count; i++)
		{
			for (int j = i + 1; j < triggers.Count; j++)
			{
				List<string> list = CurrentProfile.InputModes[inputMode].KeyStatus[triggers[i]];
				List<string> list2 = CurrentProfile.InputModes[inputMode].KeyStatus[triggers[j]];
				foreach (string item in list.Intersect(list2).ToList())
				{
					list.Remove(item);
					list2.Remove(item);
				}
			}
		}
	}

	private static void ResetKeyBinding(InputMode inputMode, string trigger)
	{
		string key = "Redigit's Pick";
		if (OriginalProfiles.ContainsKey(_selectedProfile))
		{
			key = _selectedProfile;
		}
		CurrentProfile.InputModes[inputMode].KeyStatus[trigger].Clear();
		CurrentProfile.InputModes[inputMode].KeyStatus[trigger].AddRange(OriginalProfiles[key].InputModes[inputMode].KeyStatus[trigger]);
	}

	private static bool CheckRebindingProcessGamepad(string newKey)
	{
		_canReleaseRebindingLock = false;
		if (!CurrentlyRebinding)
		{
			return NavigatorRebindingLock > 0;
		}
		if (CurrentlyRebinding && _listeningInputMode == InputMode.XBoxGamepad)
		{
			NavigatorRebindingLock = 3;
			_memoOfLastPoint = UILinkPointNavigator.CurrentPoint;
			SoundEngine.PlaySound(12);
			if (CurrentProfile.InputModes[InputMode.XBoxGamepad].KeyStatus[ListeningTrigger].Contains(newKey))
			{
				CurrentProfile.InputModes[InputMode.XBoxGamepad].KeyStatus[ListeningTrigger].Remove(newKey);
			}
			else
			{
				CurrentProfile.InputModes[InputMode.XBoxGamepad].KeyStatus[ListeningTrigger] = new List<string> { newKey };
			}
			ListenFor(null, InputMode.XBoxGamepad);
		}
		if (CurrentlyRebinding && _listeningInputMode == InputMode.XBoxGamepadUI)
		{
			NavigatorRebindingLock = 3;
			_memoOfLastPoint = UILinkPointNavigator.CurrentPoint;
			SoundEngine.PlaySound(12);
			if (CurrentProfile.InputModes[InputMode.XBoxGamepadUI].KeyStatus[ListeningTrigger].Contains(newKey))
			{
				CurrentProfile.InputModes[InputMode.XBoxGamepadUI].KeyStatus[ListeningTrigger].Remove(newKey);
			}
			else
			{
				CurrentProfile.InputModes[InputMode.XBoxGamepadUI].KeyStatus[ListeningTrigger] = new List<string> { newKey };
			}
			ListenFor(null, InputMode.XBoxGamepadUI);
		}
		FixDerpedRebinds();
		if (PlayerInput.OnBindingChange != null)
		{
			PlayerInput.OnBindingChange();
		}
		return NavigatorRebindingLock > 0;
	}

	private static bool CheckRebindingProcessKeyboard(string newKey)
	{
		_canReleaseRebindingLock = false;
		if (!CurrentlyRebinding)
		{
			return NavigatorRebindingLock > 0;
		}
		if (CurrentlyRebinding && _listeningInputMode == InputMode.Keyboard)
		{
			NavigatorRebindingLock = 3;
			_memoOfLastPoint = UILinkPointNavigator.CurrentPoint;
			SoundEngine.PlaySound(12);
			if (CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus[ListeningTrigger].Contains(newKey))
			{
				CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus[ListeningTrigger].Remove(newKey);
			}
			else
			{
				CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus[ListeningTrigger] = new List<string> { newKey };
			}
			ListenFor(null, InputMode.Keyboard);
			Main.blockKey = newKey;
			Main.blockInput = false;
			Main.ChromaPainter.CollectBoundKeys();
		}
		if (CurrentlyRebinding && _listeningInputMode == InputMode.KeyboardUI)
		{
			NavigatorRebindingLock = 3;
			_memoOfLastPoint = UILinkPointNavigator.CurrentPoint;
			SoundEngine.PlaySound(12);
			if (CurrentProfile.InputModes[InputMode.KeyboardUI].KeyStatus[ListeningTrigger].Contains(newKey))
			{
				CurrentProfile.InputModes[InputMode.KeyboardUI].KeyStatus[ListeningTrigger].Remove(newKey);
			}
			else
			{
				CurrentProfile.InputModes[InputMode.KeyboardUI].KeyStatus[ListeningTrigger] = new List<string> { newKey };
			}
			ListenFor(null, InputMode.KeyboardUI);
			Main.blockKey = newKey;
			Main.blockInput = false;
			Main.ChromaPainter.CollectBoundKeys();
		}
		FixDerpedRebinds();
		if (PlayerInput.OnBindingChange != null)
		{
			PlayerInput.OnBindingChange();
		}
		return NavigatorRebindingLock > 0;
	}

	private static void PostInput()
	{
		Main.GamepadCursorAlpha = MathHelper.Clamp(Main.GamepadCursorAlpha + ((Main.SmartCursorIsUsed && !UILinkPointNavigator.Available && GamepadThumbstickLeft == Vector2.Zero && GamepadThumbstickRight == Vector2.Zero) ? (-0.05f) : 0.05f), 0f, 1f);
		if (CurrentProfile.HotbarAllowsRadial)
		{
			int num = Triggers.Current.HotbarPlus.ToInt() - Triggers.Current.HotbarMinus.ToInt();
			if (MiscSettingsTEMP.HotbarRadialShouldBeUsed)
			{
				switch (num)
				{
				case 1:
					Triggers.Current.RadialHotbar = true;
					Triggers.JustReleased.RadialHotbar = false;
					break;
				case -1:
					Triggers.Current.RadialQuickbar = true;
					Triggers.JustReleased.RadialQuickbar = false;
					break;
				}
			}
		}
		MiscSettingsTEMP.HotbarRadialShouldBeUsed = false;
	}

	private static void HandleDpadSnap()
	{
		Vector2 zero = Vector2.Zero;
		Player player = Main.player[Main.myPlayer];
		for (int i = 0; i < 4; i++)
		{
			bool flag = false;
			Vector2 vector = Vector2.Zero;
			if (Main.gameMenu || (UILinkPointNavigator.Available && !InBuildingMode))
			{
				return;
			}
			switch (i)
			{
			case 0:
				flag = Triggers.Current.DpadMouseSnap1;
				vector = -Vector2.UnitY;
				break;
			case 1:
				flag = Triggers.Current.DpadMouseSnap2;
				vector = Vector2.UnitX;
				break;
			case 2:
				flag = Triggers.Current.DpadMouseSnap3;
				vector = Vector2.UnitY;
				break;
			case 3:
				flag = Triggers.Current.DpadMouseSnap4;
				vector = -Vector2.UnitX;
				break;
			}
			if (DpadSnapCooldown[i] > 0)
			{
				DpadSnapCooldown[i]--;
			}
			if (flag)
			{
				if (DpadSnapCooldown[i] == 0)
				{
					int num = 6;
					if (ItemSlot.IsABuildingItem(player.inventory[player.selectedItem]))
					{
						num = player.inventory[player.selectedItem].useTime;
					}
					DpadSnapCooldown[i] = num;
					zero += vector;
				}
			}
			else
			{
				DpadSnapCooldown[i] = 0;
			}
		}
		if (zero != Vector2.Zero)
		{
			Main.SmartCursorWanted_GamePad = false;
			Matrix matrix = Main.GameViewMatrix.ZoomMatrix * Main.GameViewMatrix.EffectMatrix;
			Matrix matrix2 = Matrix.Invert(matrix);
			Vector2 vector2 = Vector2.Transform(Main.MouseScreen, matrix2);
			zero.Y *= Main.LocalPlayer.gravDir;
			Vector2 vector3 = Vector2.Transform((vector2 + zero * new Vector2(16f) + Main.screenPosition).ToTileCoordinates().ToWorldCoordinates() - Main.screenPosition, matrix);
			MouseX = (int)vector3.X;
			MouseY = (int)vector3.Y;
			SettingsForUI.SetCursorMode(CursorMode.Gamepad);
		}
	}

	private static bool ShouldShowInstructionsForGamepad()
	{
		if (!UsingGamepad)
		{
			return SteamDeckIsUsed;
		}
		return true;
	}

	public static string ComposeInstructionsForGamepad()
	{
		string empty = string.Empty;
		InputMode inputMode = InputMode.XBoxGamepad;
		if (Main.gameMenu || UILinkPointNavigator.Available)
		{
			inputMode = InputMode.XBoxGamepadUI;
		}
		if (InBuildingMode && !Main.gameMenu)
		{
			inputMode = InputMode.XBoxGamepad;
		}
		KeyConfiguration keyConfiguration = CurrentProfile.InputModes[inputMode];
		Player localPlayer = Main.LocalPlayer;
		if (Main.mapFullscreen && !Main.gameMenu)
		{
			empty += "          ";
			empty += BuildCommand(Lang.misc[56].Value, ProfileGamepadUI.KeyStatus["Inventory"]);
			empty += BuildCommand(Lang.inter[118].Value, ProfileGamepadUI.KeyStatus["HotbarPlus"]);
			empty += BuildCommand(Lang.inter[119].Value, ProfileGamepadUI.KeyStatus["HotbarMinus"]);
			if (Main.netMode == 1 && Main.player[Main.myPlayer].HasItem(2997))
			{
				empty += BuildCommand(Lang.inter[120].Value, ProfileGamepadUI.KeyStatus["MouseRight"]);
			}
		}
		else if (inputMode == InputMode.XBoxGamepadUI && !InBuildingMode)
		{
			empty = UILinkPointNavigator.GetInstructions();
		}
		else if ((localPlayer.dead && localPlayer.CanDeathSpectate && localPlayer.AnyoneToSpectate()) || localPlayer.spectating >= 0)
		{
			string textValue = Language.GetTextValue((localPlayer.spectating >= 0) ? "Game.GamepadSpectateChangeTarget" : "Game.GamepadSpectate");
			empty += BuildCommand(textValue, keyConfiguration.KeyStatus["Left"], keyConfiguration.KeyStatus["Right"]);
			if (localPlayer.spectating >= 0)
			{
				empty += BuildCommand(Language.GetTextValue("Game.GamepadSpectateCancel"), keyConfiguration.KeyStatus["Jump"]);
			}
			if (localPlayer.CanWormholeToSpectating())
			{
				empty += BuildCommand(Language.GetTextValue("Game.GamepadSpectateWormhole"), keyConfiguration.KeyStatus["QuickBuff"]);
			}
		}
		else
		{
			if (localPlayer.dead)
			{
				return empty;
			}
			empty += BuildCommand(Lang.misc[58].Value, keyConfiguration.KeyStatus["Jump"]);
			empty += BuildCommand(Lang.misc[59].Value, keyConfiguration.KeyStatus["HotbarMinus"], keyConfiguration.KeyStatus["HotbarPlus"]);
			if (InBuildingMode)
			{
				empty += BuildCommand(Lang.menu[6].Value, keyConfiguration.KeyStatus["Inventory"], keyConfiguration.KeyStatus["MouseRight"]);
			}
			if (WiresUI.Open)
			{
				empty += BuildCommand(Lang.misc[53].Value, keyConfiguration.KeyStatus["MouseLeft"]);
				empty += BuildCommand(Lang.misc[56].Value, keyConfiguration.KeyStatus["MouseRight"]);
			}
			else
			{
				Item item = Main.player[Main.myPlayer].inventory[Main.player[Main.myPlayer].selectedItem];
				empty = ((item.damage > 0 && item.ammo == 0) ? (empty + BuildCommand(Lang.misc[60].Value, keyConfiguration.KeyStatus["MouseLeft"])) : ((item.createTile < 0 && item.createWall <= 0) ? (empty + BuildCommand(Lang.misc[63].Value, keyConfiguration.KeyStatus["MouseLeft"])) : (empty + BuildCommand(Lang.misc[61].Value, keyConfiguration.KeyStatus["MouseLeft"]))));
				bool flag = true;
				bool flag2 = Main.SmartInteractProj != -1 || Main.HasInteractableObjectThatIsNotATile;
				bool flag3 = !Main.SmartInteractShowingGenuine && Main.SmartInteractShowingFake;
				if (Main.SmartInteractShowingGenuine || Main.SmartInteractShowingFake || flag2)
				{
					if (Main.SmartInteractNPC != -1)
					{
						if (flag3)
						{
							flag = false;
						}
						empty += BuildCommand(Lang.misc[80].Value, keyConfiguration.KeyStatus["MouseRight"]);
					}
					else if (flag2)
					{
						if (flag3)
						{
							flag = false;
						}
						empty += BuildCommand(Lang.misc[79].Value, keyConfiguration.KeyStatus["MouseRight"]);
					}
					else if (Main.SmartInteractX != -1 && Main.SmartInteractY != -1)
					{
						if (flag3)
						{
							flag = false;
						}
						Tile tile = Main.tile[Main.SmartInteractX, Main.SmartInteractY];
						empty = ((!TileID.Sets.TileInteractRead[tile.type]) ? (empty + BuildCommand(Lang.misc[79].Value, keyConfiguration.KeyStatus["MouseRight"])) : (empty + BuildCommand(Lang.misc[81].Value, keyConfiguration.KeyStatus["MouseRight"])));
					}
				}
				else if (WiresUI.Settings.DrawToolModeUI)
				{
					empty += BuildCommand(Lang.misc[89].Value, keyConfiguration.KeyStatus["MouseRight"]);
				}
				else if (ItemID.Sets.HasRightFire[item.type])
				{
					empty += BuildCommand(Language.GetTextValue("UI.ActionAltAction"), keyConfiguration.KeyStatus["MouseRight"]);
				}
				if ((!GrappleAndInteractAreShared || (!WiresUI.Settings.DrawToolModeUI && (!Main.SmartInteractShowingGenuine || !Main.HasSmartInteractTarget) && (!Main.SmartInteractShowingFake || flag) && !ItemID.Sets.HasRightFire[item.type])) && Main.LocalPlayer.QuickGrapple_GetItemToUse() != null)
				{
					empty += BuildCommand(Lang.misc[57].Value, keyConfiguration.KeyStatus["Grapple"]);
				}
			}
		}
		return empty;
	}

	public static string BuildCommand(string CommandText, params List<string>[] Bindings)
	{
		string text = "";
		if (Bindings.Length == 0)
		{
			return text;
		}
		text += GenerateGlyphList(Bindings[0]);
		for (int i = 1; i < Bindings.Length; i++)
		{
			string text2 = GenerateGlyphList(Bindings[i]);
			if (text2.Length > 0)
			{
				text = text + "/" + text2;
			}
		}
		if (text.Length > 0)
		{
			text = text + ": " + CommandText + "   ";
		}
		return text;
	}

	public static string GenerateInputTag_ForCurrentGamemode(bool tagForGameplay, string triggerName)
	{
		if (UsingGamepad)
		{
			return GenerateGlyphList(CurrentProfile.InputModes[tagForGameplay ? InputMode.XBoxGamepad : InputMode.XBoxGamepadUI].KeyStatus[triggerName]);
		}
		return GenerateRawInputList(CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus[triggerName]);
	}

	private static string LocalizeKey(string keyName)
	{
		if (keyName == "Mouse1")
		{
			return Language.GetTextValue("Controls.LeftClick");
		}
		if (keyName == "Mouse2")
		{
			return Language.GetTextValue("Controls.RightClick");
		}
		return keyName;
	}

	private static string GenerateGlyphList(List<string> list)
	{
		if (list.Count == 0)
		{
			return "";
		}
		string text = GlyphTagHandler.GenerateTag(list[0]);
		for (int i = 1; i < list.Count; i++)
		{
			text = text + "/" + GlyphTagHandler.GenerateTag(list[i]);
		}
		return text;
	}

	private static string GenerateRawInputList(List<string> list)
	{
		if (list.Count == 0)
		{
			return "";
		}
		string text = LocalizeKey(list[0]);
		for (int i = 1; i < list.Count; i++)
		{
			text = text + "/" + LocalizeKey(list[i]);
		}
		return text;
	}

	public static void NavigatorCachePosition()
	{
		PreUIX = MouseX;
		PreUIY = MouseY;
	}

	public static void NavigatorUnCachePosition()
	{
		MouseX = PreUIX;
		MouseY = PreUIY;
	}

	public static void LockOnCachePosition()
	{
		PreLockOnX = MouseX;
		PreLockOnY = MouseY;
	}

	public static void LockOnUnCachePosition()
	{
		MouseX = PreLockOnX;
		MouseY = PreLockOnY;
	}

	public static void PrettyPrintProfiles(ref string text)
	{
		string[] array = text.Split(new string[1] { "\r\n" }, StringSplitOptions.None);
		foreach (string text2 in array)
		{
			if (text2.Contains(": {"))
			{
				string text3 = text2.Substring(0, text2.IndexOf('"'));
				string text4 = text2 + "\r\n  ";
				string newValue = text4.Replace(": {\r\n  ", ": \r\n" + text3 + "{\r\n  ");
				text = text.Replace(text4, newValue);
			}
		}
		text = text.Replace("[\r\n        ", "[");
		text = text.Replace("[\r\n      ", "[");
		text = text.Replace("\"\r\n      ", "\"");
		text = text.Replace("\",\r\n        ", "\", ");
		text = text.Replace("\",\r\n      ", "\", ");
		text = text.Replace("\r\n    ]", "]");
	}

	public static void PrettyPrintProfilesOld(ref string text)
	{
		text = text.Replace(": {\r\n  ", ": \r\n  {\r\n  ");
		text = text.Replace("[\r\n      ", "[");
		text = text.Replace("\"\r\n      ", "\"");
		text = text.Replace("\",\r\n      ", "\", ");
		text = text.Replace("\r\n    ]", "]");
	}

	public static void Reset(KeyConfiguration c, PresetProfiles style, InputMode mode)
	{
		switch (style)
		{
		case PresetProfiles.Redigit:
			switch (mode)
			{
			case InputMode.Keyboard:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Jump"].Add("Space");
				c.KeyStatus["Inventory"].Add("Escape");
				c.KeyStatus["Grapple"].Add("E");
				c.KeyStatus["SmartSelect"].Add("LeftShift");
				c.KeyStatus["SmartCursor"].Add("LeftControl");
				c.KeyStatus["QuickMount"].Add("R");
				c.KeyStatus["QuickHeal"].Add("H");
				c.KeyStatus["QuickMana"].Add("J");
				c.KeyStatus["QuickBuff"].Add("B");
				c.KeyStatus["MapStyle"].Add("Tab");
				c.KeyStatus["MapFull"].Add("M");
				c.KeyStatus["MapZoomIn"].Add("Add");
				c.KeyStatus["MapZoomOut"].Add("Subtract");
				c.KeyStatus["MapAlphaUp"].Add("PageUp");
				c.KeyStatus["MapAlphaDown"].Add("PageDown");
				c.KeyStatus["Hotbar1"].Add("D1");
				c.KeyStatus["Hotbar2"].Add("D2");
				c.KeyStatus["Hotbar3"].Add("D3");
				c.KeyStatus["Hotbar4"].Add("D4");
				c.KeyStatus["Hotbar5"].Add("D5");
				c.KeyStatus["Hotbar6"].Add("D6");
				c.KeyStatus["Hotbar7"].Add("D7");
				c.KeyStatus["Hotbar8"].Add("D8");
				c.KeyStatus["Hotbar9"].Add("D9");
				c.KeyStatus["Hotbar10"].Add("D0");
				c.KeyStatus["ViewZoomOut"].Add("OemMinus");
				c.KeyStatus["ViewZoomIn"].Add("OemPlus");
				c.KeyStatus["ToggleCreativeMenu"].Add("C");
				c.KeyStatus["Loadout1"].Add("F1");
				c.KeyStatus["Loadout2"].Add("F2");
				c.KeyStatus["Loadout3"].Add("F3");
				c.KeyStatus["ToggleCameraMode"].Add("F4");
				break;
			case InputMode.KeyboardUI:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseLeft"].Add("Space");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Up"].Add("Up");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Down"].Add("Down");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Left"].Add("Left");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Right"].Add("Right");
				c.KeyStatus["Inventory"].Add(Keys.Escape.ToString());
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				break;
			case InputMode.XBoxGamepad:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Jump"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.B));
				c.KeyStatus["LockOn"].Add(string.Concat(Buttons.X));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.A));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.LeftStick));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["DpadSnap1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadSnap3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadSnap4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadSnap2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["MapStyle"].Add(string.Concat(Buttons.Back));
				break;
			case InputMode.XBoxGamepadUI:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.A));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.X));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.Back));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["DpadSnap1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadSnap3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadSnap4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadSnap2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				break;
			case InputMode.Mouse:
				break;
			}
			break;
		case PresetProfiles.Yoraiz0r:
			switch (mode)
			{
			case InputMode.Keyboard:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Jump"].Add("Space");
				c.KeyStatus["Inventory"].Add("Escape");
				c.KeyStatus["Grapple"].Add("E");
				c.KeyStatus["SmartSelect"].Add("LeftShift");
				c.KeyStatus["SmartCursor"].Add("LeftControl");
				c.KeyStatus["QuickMount"].Add("R");
				c.KeyStatus["QuickHeal"].Add("H");
				c.KeyStatus["QuickMana"].Add("J");
				c.KeyStatus["QuickBuff"].Add("B");
				c.KeyStatus["MapStyle"].Add("Tab");
				c.KeyStatus["MapFull"].Add("M");
				c.KeyStatus["MapZoomIn"].Add("Add");
				c.KeyStatus["MapZoomOut"].Add("Subtract");
				c.KeyStatus["MapAlphaUp"].Add("PageUp");
				c.KeyStatus["MapAlphaDown"].Add("PageDown");
				c.KeyStatus["Hotbar1"].Add("D1");
				c.KeyStatus["Hotbar2"].Add("D2");
				c.KeyStatus["Hotbar3"].Add("D3");
				c.KeyStatus["Hotbar4"].Add("D4");
				c.KeyStatus["Hotbar5"].Add("D5");
				c.KeyStatus["Hotbar6"].Add("D6");
				c.KeyStatus["Hotbar7"].Add("D7");
				c.KeyStatus["Hotbar8"].Add("D8");
				c.KeyStatus["Hotbar9"].Add("D9");
				c.KeyStatus["Hotbar10"].Add("D0");
				c.KeyStatus["ViewZoomOut"].Add("OemMinus");
				c.KeyStatus["ViewZoomIn"].Add("OemPlus");
				c.KeyStatus["ToggleCreativeMenu"].Add("C");
				c.KeyStatus["Loadout1"].Add("F1");
				c.KeyStatus["Loadout2"].Add("F2");
				c.KeyStatus["Loadout3"].Add("F3");
				c.KeyStatus["ToggleCameraMode"].Add("F4");
				break;
			case InputMode.KeyboardUI:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseLeft"].Add("Space");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Up"].Add("Up");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Down"].Add("Down");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Left"].Add("Left");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Right"].Add("Right");
				c.KeyStatus["Inventory"].Add(Keys.Escape.ToString());
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				break;
			case InputMode.XBoxGamepad:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Jump"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.LeftStick));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.X));
				c.KeyStatus["QuickHeal"].Add(string.Concat(Buttons.A));
				c.KeyStatus["RadialHotbar"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["DpadSnap1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadSnap3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadSnap4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadSnap2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["MapStyle"].Add(string.Concat(Buttons.Back));
				break;
			case InputMode.XBoxGamepadUI:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.A));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["LockOn"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.X));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.Back));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["DpadSnap1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadSnap3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadSnap4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadSnap2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				break;
			case InputMode.Mouse:
				break;
			}
			break;
		case PresetProfiles.ConsolePS:
			switch (mode)
			{
			case InputMode.Keyboard:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Jump"].Add("Space");
				c.KeyStatus["Inventory"].Add("Escape");
				c.KeyStatus["Grapple"].Add("E");
				c.KeyStatus["SmartSelect"].Add("LeftShift");
				c.KeyStatus["SmartCursor"].Add("LeftControl");
				c.KeyStatus["QuickMount"].Add("R");
				c.KeyStatus["QuickHeal"].Add("H");
				c.KeyStatus["QuickMana"].Add("J");
				c.KeyStatus["QuickBuff"].Add("B");
				c.KeyStatus["MapStyle"].Add("Tab");
				c.KeyStatus["MapFull"].Add("M");
				c.KeyStatus["MapZoomIn"].Add("Add");
				c.KeyStatus["MapZoomOut"].Add("Subtract");
				c.KeyStatus["MapAlphaUp"].Add("PageUp");
				c.KeyStatus["MapAlphaDown"].Add("PageDown");
				c.KeyStatus["Hotbar1"].Add("D1");
				c.KeyStatus["Hotbar2"].Add("D2");
				c.KeyStatus["Hotbar3"].Add("D3");
				c.KeyStatus["Hotbar4"].Add("D4");
				c.KeyStatus["Hotbar5"].Add("D5");
				c.KeyStatus["Hotbar6"].Add("D6");
				c.KeyStatus["Hotbar7"].Add("D7");
				c.KeyStatus["Hotbar8"].Add("D8");
				c.KeyStatus["Hotbar9"].Add("D9");
				c.KeyStatus["Hotbar10"].Add("D0");
				c.KeyStatus["ViewZoomOut"].Add("OemMinus");
				c.KeyStatus["ViewZoomIn"].Add("OemPlus");
				c.KeyStatus["ToggleCreativeMenu"].Add("C");
				c.KeyStatus["Loadout1"].Add("F1");
				c.KeyStatus["Loadout2"].Add("F2");
				c.KeyStatus["Loadout3"].Add("F3");
				c.KeyStatus["ToggleCameraMode"].Add("F4");
				break;
			case InputMode.KeyboardUI:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseLeft"].Add("Space");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Up"].Add("Up");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Down"].Add("Down");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Left"].Add("Left");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Right"].Add("Right");
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["Inventory"].Add(Keys.Escape.ToString());
				break;
			case InputMode.XBoxGamepad:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Jump"].Add(string.Concat(Buttons.A));
				c.KeyStatus["LockOn"].Add(string.Concat(Buttons.X));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.LeftStick));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["DpadRadial1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadRadial3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadRadial4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadRadial2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.Back));
				break;
			case InputMode.XBoxGamepadUI:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.A));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.X));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.Back));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["DpadRadial1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadRadial3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadRadial4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadRadial2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				break;
			case InputMode.Mouse:
				break;
			}
			break;
		case PresetProfiles.ConsoleXBox:
			switch (mode)
			{
			case InputMode.Keyboard:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Jump"].Add("Space");
				c.KeyStatus["Inventory"].Add("Escape");
				c.KeyStatus["Grapple"].Add("E");
				c.KeyStatus["SmartSelect"].Add("LeftShift");
				c.KeyStatus["SmartCursor"].Add("LeftControl");
				c.KeyStatus["QuickMount"].Add("R");
				c.KeyStatus["QuickHeal"].Add("H");
				c.KeyStatus["QuickMana"].Add("J");
				c.KeyStatus["QuickBuff"].Add("B");
				c.KeyStatus["MapStyle"].Add("Tab");
				c.KeyStatus["MapFull"].Add("M");
				c.KeyStatus["MapZoomIn"].Add("Add");
				c.KeyStatus["MapZoomOut"].Add("Subtract");
				c.KeyStatus["MapAlphaUp"].Add("PageUp");
				c.KeyStatus["MapAlphaDown"].Add("PageDown");
				c.KeyStatus["Hotbar1"].Add("D1");
				c.KeyStatus["Hotbar2"].Add("D2");
				c.KeyStatus["Hotbar3"].Add("D3");
				c.KeyStatus["Hotbar4"].Add("D4");
				c.KeyStatus["Hotbar5"].Add("D5");
				c.KeyStatus["Hotbar6"].Add("D6");
				c.KeyStatus["Hotbar7"].Add("D7");
				c.KeyStatus["Hotbar8"].Add("D8");
				c.KeyStatus["Hotbar9"].Add("D9");
				c.KeyStatus["Hotbar10"].Add("D0");
				c.KeyStatus["ViewZoomOut"].Add("OemMinus");
				c.KeyStatus["ViewZoomIn"].Add("OemPlus");
				c.KeyStatus["ToggleCreativeMenu"].Add("C");
				c.KeyStatus["Loadout1"].Add("F1");
				c.KeyStatus["Loadout2"].Add("F2");
				c.KeyStatus["Loadout3"].Add("F3");
				c.KeyStatus["ToggleCameraMode"].Add("F4");
				break;
			case InputMode.KeyboardUI:
				c.KeyStatus["MouseLeft"].Add("Mouse1");
				c.KeyStatus["MouseLeft"].Add("Space");
				c.KeyStatus["MouseRight"].Add("Mouse2");
				c.KeyStatus["Up"].Add("W");
				c.KeyStatus["Up"].Add("Up");
				c.KeyStatus["Down"].Add("S");
				c.KeyStatus["Down"].Add("Down");
				c.KeyStatus["Left"].Add("A");
				c.KeyStatus["Left"].Add("Left");
				c.KeyStatus["Right"].Add("D");
				c.KeyStatus["Right"].Add("Right");
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["Inventory"].Add(Keys.Escape.ToString());
				break;
			case InputMode.XBoxGamepad:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Jump"].Add(string.Concat(Buttons.A));
				c.KeyStatus["LockOn"].Add(string.Concat(Buttons.X));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.LeftStick));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["DpadRadial1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadRadial3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadRadial4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadRadial2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.Back));
				break;
			case InputMode.XBoxGamepadUI:
				c.KeyStatus["MouseLeft"].Add(string.Concat(Buttons.A));
				c.KeyStatus["MouseRight"].Add(string.Concat(Buttons.LeftShoulder));
				c.KeyStatus["SmartCursor"].Add(string.Concat(Buttons.RightShoulder));
				c.KeyStatus["Up"].Add(string.Concat(Buttons.LeftThumbstickUp));
				c.KeyStatus["Down"].Add(string.Concat(Buttons.LeftThumbstickDown));
				c.KeyStatus["Left"].Add(string.Concat(Buttons.LeftThumbstickLeft));
				c.KeyStatus["Right"].Add(string.Concat(Buttons.LeftThumbstickRight));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.B));
				c.KeyStatus["Inventory"].Add(string.Concat(Buttons.Y));
				c.KeyStatus["HotbarMinus"].Add(string.Concat(Buttons.LeftTrigger));
				c.KeyStatus["HotbarPlus"].Add(string.Concat(Buttons.RightTrigger));
				c.KeyStatus["Grapple"].Add(string.Concat(Buttons.X));
				c.KeyStatus["MapFull"].Add(string.Concat(Buttons.Start));
				c.KeyStatus["SmartSelect"].Add(string.Concat(Buttons.Back));
				c.KeyStatus["QuickMount"].Add(string.Concat(Buttons.RightStick));
				c.KeyStatus["DpadRadial1"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["DpadRadial3"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["DpadRadial4"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["DpadRadial2"].Add(string.Concat(Buttons.DPadRight));
				c.KeyStatus["MenuUp"].Add(string.Concat(Buttons.DPadUp));
				c.KeyStatus["MenuDown"].Add(string.Concat(Buttons.DPadDown));
				c.KeyStatus["MenuLeft"].Add(string.Concat(Buttons.DPadLeft));
				c.KeyStatus["MenuRight"].Add(string.Concat(Buttons.DPadRight));
				break;
			case InputMode.Mouse:
				break;
			}
			break;
		}
	}

	public static void SetZoom_UI()
	{
		SetZoom_Scaled(1f / Main.UIScale);
	}

	public static void SetZoom_Background()
	{
		SetZoom_Scaled(1f / Main.BackgroundViewMatrix.RenderZoom.X);
	}

	public static void SetZoom_World()
	{
		SetZoom_Unscaled();
		SetZoom_MouseInWorld();
	}

	public static void SetZoom_Unscaled()
	{
		Main.lastMouseX = _originalLastMouseX;
		Main.lastMouseY = _originalLastMouseY;
		Main.mouseX = _originalMouseX;
		Main.mouseY = _originalMouseY;
		Main.screenWidth = _originalScreenWidth;
		Main.screenHeight = _originalScreenHeight;
	}

	public static void SetZoom_MouseInWorld()
	{
		Vector2 vector = Main.screenPosition + new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
		Vector2 vector2 = Main.screenPosition + new Vector2(_originalMouseX, _originalMouseY);
		Vector2 vector3 = Main.screenPosition + new Vector2(_originalLastMouseX, _originalLastMouseY);
		Vector2 vector4 = vector2 - vector;
		Vector2 vector5 = vector3 - vector;
		float num = 1f / Main.GameViewMatrix.RenderZoom.X;
		Vector2 vector6 = vector - Main.screenPosition + vector4 * num;
		Main.mouseX = (int)vector6.X;
		Main.mouseY = (int)vector6.Y;
		Vector2 vector7 = vector - Main.screenPosition + vector5 * num;
		Main.lastMouseX = (int)vector7.X;
		Main.lastMouseY = (int)vector7.Y;
	}

	private static void SetZoom_Scaled(float scale)
	{
		Main.lastMouseX = (int)((float)_originalLastMouseX * scale);
		Main.lastMouseY = (int)((float)_originalLastMouseY * scale);
		Main.mouseX = (int)((float)_originalMouseX * scale);
		Main.mouseY = (int)((float)_originalMouseY * scale);
		Main.screenWidth = (int)((float)_originalScreenWidth * scale);
		Main.screenHeight = (int)((float)_originalScreenHeight * scale);
	}
}
