#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;
using SandboxGame.Save;
using SandboxGame.Settings;

namespace SandboxGame.UI;

public enum GameState
{
    MainMenu,
    Playing,
    Paused
}

public enum MenuScreen
{
    None,
    Main,
    Pause,
    Settings,
    SaveSlots,
    LoadSlots,
    SaveNamePrompt,
    Confirm,
    CharacterCreator,
    NewGameMaps
}

public enum SettingsSection
{
    General,
    Controls,
    Service
}

public enum SettingsFocus
{
    Sidebar,
    Content
}

public class MenuSystem
{
    private readonly GameSettings _settings;
    private readonly InputManager _input;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;
    private Texture2D _mainMenuBackground = null!;
    private GraphicsDevice _gd = null!;

    private MenuDefinition? _mainMenuDef;
    private MenuDefinition? _pauseMenuDef;
    private readonly Dictionary<string, Action> _actionMap = new();

    private readonly List<MenuItem> _menuItems = new();
    private readonly List<MenuItem> _settingsNavItems = new();
    private readonly List<MenuItem> _settingsContentItems = new();

    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private int _selectedSettingsSectionIndex;
    private int _hoveredSettingsSectionIndex = -1;
    private int _selectedSettingsContentIndex;
    private int _hoveredSettingsContentIndex = -1;
    private int _settingsContentScroll;
    private SettingsFocus _settingsFocus = SettingsFocus.Content;
    private MenuScreen _returnTo = MenuScreen.Main;
    private string? _waitingForKey;
    private KeyboardState _prevKb;
    private Rectangle _lastSettingsNavRect;
    private Rectangle _lastSettingsContentRect;
    private string _overlayTitle = "";
    private string _overlayDescription = "";
    private string _overlayNavHeader = "Actions";
    private string _overlayNavDescription = "";
    private string _saveNameInput = "";
    private int _pendingSaveSlotIndex;
    private bool _saveNamePromptIsRename;
    private bool _confirmAsModal;
    private bool _resolutionDropdownOpen;
    private bool _languageDropdownOpen;
    private bool _pendingResolutionConfirmation;
    private float _pendingResolutionSeconds;
    private DateTime _pendingResolutionDeadlineUtc;
    private Point _pendingResolutionPrevious;
    private Point _pendingResolutionTarget;
    private Action? _confirmAccept;
    private Action? _confirmCancel;
    private readonly List<ResolutionOption> _availableResolutionOptions = new();
    private readonly PlayerCharacterDraft _characterDraft = new();
    private int _characterHairColorIndex = 2;
    private int _characterSkinColorIndex = 1;
    /// <summary>0=down,1=left,2=up,3=right.</summary>
    private int _previewFacing;
    private Rectangle _rotateButtonRect;
    private Rectangle _skinTabRect;
    private Rectangle _hairTabRect;
    private HsvColorPicker? _colorPicker;
    private HsvColorPicker.Picker? _activePicker;
    /// <summary>0=skin, 1=hair. Какому полю draft пикер применяет результат.</summary>
    private int _colorTarget;
    private string _lastPickerSourceColor = "";

    private const float MinUiScale = 0.75f;
    private const float MaxUiScale = 2f;
    private const float UiScaleStep = 0.05f;

    private readonly record struct ResolutionOption(Point WindowSize, Point PixelSize)
    {
        public string Label => $"{PixelSize.X}x{PixelSize.Y}";
    }

    public GameState GameState { get; set; } = GameState.MainMenu;
    public MenuScreen CurrentScreen { get; private set; } = MenuScreen.Main;
    public Func<IReadOnlyList<SaveSlotSummary>>? SaveSlotProvider { get; set; }
    public Func<int, string>? SaveNameSuggestionProvider { get; set; }
    public Func<IReadOnlyList<MapCatalogEntry>>? MapCatalogProvider { get; set; }
    public Func<string, IReadOnlyList<CharacterCreatorHairOption>>? HairOptionsProvider { get; set; }
    public CharacterPreviewRenderer? CharacterPreview { get; set; }
    public Func<(bool Ok, string Message)>? StartLocationValidator { get; set; }

    public event Action<string>? OnStartGameWithMap;
    public event Action<PlayerCharacterDraft>? OnStartGameRequested;
    public event Action? OnLoadRiver;
    public event Action? OnExitGame;
    public event Action? OnResumeGame;
    public event Action? OnReturnToMainMenu;
    public event Action<int, string>? OnSaveSlotConfirmed;
    public event Action<int, string>? OnRenameSlotRequested;
    public event Action<int>? OnDeleteSlotRequested;
    public event Action<int>? OnLoadSlotSelected;
    public event Action<int, int>? OnResolutionChanged;

    public bool IsBlockingInput => GameState != GameState.Playing || CurrentScreen != MenuScreen.None;

    public MenuSystem(GameSettings settings, InputManager input)
    {
        _settings = settings;
        _input = input;
    }

    public void SetGraphics(GraphicsDevice gd, SpriteFont font)
    {
        _gd = gd;
        _font = font;
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _mainMenuBackground = _pixel;
        RefreshResolutionOptions();
        LoadMenuDefinitions();
    }

    private void LoadMenuDefinitions()
    {
        var uiDir = ContentPaths.ResolveDirectory(Path.Combine(ContentPaths.ContentRoot, "UI"));

        var mainPath = Path.Combine(uiDir, "MainMenu.xml");
        if (File.Exists(mainPath))
            _mainMenuDef = MenuDefinition.LoadFromFile(mainPath);

        var pausePath = Path.Combine(uiDir, "PauseMenu.xml");
        if (File.Exists(pausePath))
            _pauseMenuDef = MenuDefinition.LoadFromFile(pausePath);

        // маппинг строковых action -> делегатов
        _actionMap["start_game"] = OpenCharacterCreator;
        _actionMap["load_river"] = () => OnLoadRiver?.Invoke();
        _actionMap["open_load"] = OpenLoadSlots;
        _actionMap["open_save"] = OpenSaveSlots;
        _actionMap["open_settings"] = OpenSettings;
        _actionMap["exit_confirm"] = () => OpenConfirmation(
            "Подтверждение",
            "Выйти из игры?",
            () => OnExitGame?.Invoke());
        _actionMap["resume"] = () => { CloseMenu(); OnResumeGame?.Invoke(); };
        _actionMap["main_menu_confirm"] = () => OpenConfirmation(
            "Подтверждение",
            "Вернуться в главное меню?",
            () => OnReturnToMainMenu?.Invoke());
    }

    private bool EvaluateCondition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        return condition switch
        {
            "dev_mode" => _settings.DevMode,
            _ => true
        };
    }

    public void OpenPause()
    {
        if (GameState != GameState.Playing)
            return;

        GameState = GameState.Paused;
        CurrentScreen = MenuScreen.Pause;
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    public void OpenMainMenu()
    {
        GameState = GameState.MainMenu;
        CurrentScreen = MenuScreen.Main;
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    public void CloseMenu()
    {
        GameState = GameState.Playing;
        CurrentScreen = MenuScreen.None;
        _waitingForKey = null;
        _overlayTitle = "";
        _overlayDescription = "";
        _confirmAccept = null;
        _confirmCancel = null;
        _confirmAsModal = false;
        _hoveredIndex = -1;
        _hoveredSettingsSectionIndex = -1;
        _hoveredSettingsContentIndex = -1;
    }

    public void OpenConfirmation(string title, string description, Action onConfirm, Action? onCancel = null)
    {
        _returnTo = CurrentScreen;
        CurrentScreen = MenuScreen.Confirm;
        _confirmAsModal = false;
        _overlayTitle = title;
        _overlayDescription = description;
        _overlayNavHeader = "Подтверждение";
        _overlayNavDescription = "Выберите действие.";
        _confirmAccept = onConfirm;
        _confirmCancel = onCancel;
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    public void OpenModalConfirmation(string title, string description, Action onConfirm, Action? onCancel = null)
    {
        _returnTo = MenuScreen.None;
        GameState = GameState.Playing;
        CurrentScreen = MenuScreen.Confirm;
        _confirmAsModal = true;
        _overlayTitle = title;
        _overlayDescription = description;
        _overlayNavHeader = "Подтверждение";
        _overlayNavDescription = "Выберите действие.";
        _confirmAccept = onConfirm;
        _confirmCancel = onCancel;
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    public void Update()
    {
        if (CurrentScreen == MenuScreen.None)
            return;

        UpdatePendingResolutionConfirmation();

        var kb = Keyboard.GetState();

        if (_waitingForKey != null)
        {
            CaptureKeyBinding(kb);
            _prevKb = kb;
            return;
        }

        if (CurrentScreen == MenuScreen.Settings)
        {
            UpdateSettings(kb);
            _prevKb = kb;
            return;
        }

        UpdateStandardMenu(kb);
        _prevKb = kb;
    }

    public void OnTextInput(char c)
    {
        if (CurrentScreen != MenuScreen.SaveNamePrompt)
            return;

        if (c == '\b' || c == '\r' || c == '\n' || c == '\t' || char.IsControl(c))
            return;

        if (_saveNameInput.Length >= 48)
            return;

        _saveNameInput += c;
    }

    private MenuDefinition GetActiveDef()
    {
        return CurrentScreen switch
        {
            MenuScreen.Main => _mainMenuDef ?? _defaultDef,
            MenuScreen.Pause => _pauseMenuDef ?? _defaultDef,
            MenuScreen.Settings or MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.Confirm
                or MenuScreen.CharacterCreator or MenuScreen.NewGameMaps
                => (_returnTo == MenuScreen.Main ? _mainMenuDef : _pauseMenuDef) ?? _defaultDef,
            _ => _defaultDef
        };
    }

    private static readonly MenuDefinition _defaultDef = new();

    public void Draw(SpriteBatch sb)
    {
        if (CurrentScreen == MenuScreen.None)
            return;

        var def = GetActiveDef();
        var uiScale = GetUiScale();
        if (CurrentScreen == MenuScreen.Confirm && _confirmAsModal)
        {
            DrawModalConfirmation(sb, GetScaledViewportBounds(), def, uiScale);
            return;
        }

        var lay = def.Layout;
        var vp = GetScaledViewportBounds();
        var title = GetScreenTitle(def);

        var m = lay.Margin;
        var pp = lay.PanelPadding;
        var pageRect = new Rectangle(m, m, vp.Width - m * 2, vp.Height - m * 2);
        var navWidth = Math.Min(lay.NavMaxWidth, (int)(pageRect.Width * lay.NavWidthPercent));
        var navRect = new Rectangle(pageRect.X + pp, pageRect.Y + lay.TitleBottomMargin, navWidth, pageRect.Height - lay.TitleBottomMargin - pp);
        var contentRect = new Rectangle(
            navRect.Right + lay.NavContentGap,
            navRect.Y,
            pageRect.Right - (navRect.Right + lay.NavContentGap + pp),
            navRect.Height);

        if (CurrentScreen == MenuScreen.Settings)
        {
            _lastSettingsNavRect = navRect;
            _lastSettingsContentRect = contentRect;
        }

        sb.Begin(
            blendState: BlendState.AlphaBlend,
            samplerState: GetTextFriendlySampler(uiScale),
            transformMatrix: GameEngine.Instance.GetUiTransform(uiScale));

        DrawBackground(sb, vp, def.Background);
        DrawPanel(sb, pageRect, def.PagePanel.Fill, def.PagePanel.Border, def.PagePanel.BorderThickness);
        DrawPanel(sb, navRect, def.NavPanel.Fill, def.NavPanel.Border, def.NavPanel.BorderThickness);
        DrawPanel(sb, contentRect, def.ContentPanel.Fill, def.ContentPanel.Border, def.ContentPanel.BorderThickness);

        var tb = def.TitleBar;
        var titleSafe = DisplayText(title);
        var titleSize = _font.MeasureString(titleSafe);
        sb.DrawString(_font, titleSafe, Snap(new Vector2(pageRect.X + tb.OffsetX, pageRect.Y + tb.OffsetY)), tb.Color);
        sb.Draw(_pixel, new Rectangle(pageRect.X + tb.OffsetX, pageRect.Y + tb.OffsetY + (int)titleSize.Y + tb.SeparatorGap, pageRect.Width - tb.OffsetX * 2, tb.SeparatorHeight), tb.SeparatorColor);

        if (CurrentScreen == MenuScreen.Settings)
            DrawSettingsLayout(sb, navRect, contentRect, def);
        else
            DrawStandardLayout(sb, navRect, contentRect, def);

        var hintDef = def.Hint;
        var hint = GetScreenHintText(hintDef);
        if (!string.IsNullOrEmpty(hint))
        {
            var hintSafe = DisplayText(hint);
            var hintSize = _font.MeasureString(hintSafe);
            var hintPos = ResolveHintPosition(pageRect, hintDef, hintSize);
            sb.DrawString(_font, hintSafe, Snap(hintPos), hintDef.Color);
        }

        sb.End();
    }

    private void DrawModalConfirmation(SpriteBatch sb, Rectangle viewportRect, MenuDefinition def, float uiScale)
    {
        sb.Begin(
            blendState: BlendState.AlphaBlend,
            samplerState: GetTextFriendlySampler(uiScale),
            transformMatrix: GameEngine.Instance.GetUiTransform(uiScale));

        sb.Draw(_pixel, viewportRect, new Color(0, 0, 0, 110));

        var dialogWidth = Math.Min(520, viewportRect.Width - 80);
        var dialogHeight = 220;
        var dialogRect = new Rectangle(
            viewportRect.Center.X - dialogWidth / 2,
            viewportRect.Center.Y - dialogHeight / 2,
            dialogWidth,
            dialogHeight);

        DrawPanel(sb, dialogRect, def.PagePanel.Fill, def.PagePanel.Border, def.PagePanel.BorderThickness);
        DrawText(sb, _overlayTitle, new Vector2(dialogRect.X + 22, dialogRect.Y + 20), def.TitleBar.Color);
        DrawMultilineText(sb, _overlayDescription, new Vector2(dialogRect.X + 22, dialogRect.Y + 56), dialogRect.Width - 44, def.ContentPanel.DescriptionColor);

        var itemStyle = def.ItemStyle;
        var buttonWidth = dialogRect.Width - 44;
        var buttonHeight = 40;
        var buttonsY = dialogRect.Bottom - 96;

        for (var i = 0; i < _menuItems.Count; i++)
        {
            var bounds = new Rectangle(dialogRect.X + 22, buttonsY + i * (buttonHeight + 8), buttonWidth, buttonHeight);
            _menuItems[i] = _menuItems[i] with { Bounds = bounds };

            var isSelected = i == _selectedIndex;
            var isHovered = i == _hoveredIndex;
            var fill = isSelected ? itemStyle.SelectedFill : isHovered ? itemStyle.HoveredFill : itemStyle.DefaultFill;
            var border = isSelected ? itemStyle.SelectedBorder : isHovered ? itemStyle.HoveredBorder : itemStyle.DefaultBorder;
            var textColor = isSelected ? itemStyle.SelectedText : itemStyle.DefaultText;

            DrawPanel(sb, bounds, fill, border, 1);
            if (isSelected && itemStyle.AccentBarWidth > 0)
                sb.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, itemStyle.AccentBarWidth, bounds.Height), itemStyle.AccentBarColor);

            DrawText(sb, _menuItems[i].Label, new Vector2(bounds.X + 14, bounds.Y + 10), textColor);
        }

        sb.End();
    }

    private void UpdateStandardMenu(KeyboardState kb)
    {
        if (_menuItems.Count == 0)
            return;

        if (IsNewPress(kb, Keys.Up) || IsNewPress(kb, Keys.W))
            _selectedIndex = (_selectedIndex - 1 + _menuItems.Count) % _menuItems.Count;

        if (IsNewPress(kb, Keys.Down) || IsNewPress(kb, Keys.S))
            _selectedIndex = (_selectedIndex + 1) % _menuItems.Count;

        if (CurrentScreen == MenuScreen.CharacterCreator)
        {
            if (IsNewPress(kb, Keys.Left) || IsNewPress(kb, Keys.A))
                AdjustCharacterCreatorSelection(-1);
            if (IsNewPress(kb, Keys.Right) || IsNewPress(kb, Keys.D))
                AdjustCharacterCreatorSelection(1);

            HandleCharacterCreatorMouse();
        }

        var hoverPos = GetScaledMousePosition();
        _hoveredIndex = -1;
        for (int i = 0; i < _menuItems.Count; i++)
        {
            if (_menuItems[i].Bounds.Contains(hoverPos))
            {
                _hoveredIndex = i;
                break;
            }
        }

        var confirm = IsNewPress(kb, Keys.Enter) || IsNewPress(kb, Keys.Space);
        if (!confirm && _input.LeftClicked && _selectedIndex >= 0 && _selectedIndex < _menuItems.Count
            && _menuItems[_selectedIndex].Bounds.Contains(hoverPos))
            confirm = true;

        if (!confirm && _input.LeftClicked)
        {
            for (int i = 0; i < _menuItems.Count; i++)
            {
                if (!_menuItems[i].Bounds.Contains(hoverPos))
                    continue;

                _selectedIndex = i;
                _hoveredIndex = i;
                confirm = true;
                break;
            }
        }

        if (confirm && _selectedIndex >= 0 && _selectedIndex < _menuItems.Count)
            _menuItems[_selectedIndex].Action?.Invoke();

        if ((CurrentScreen == MenuScreen.SaveSlots || CurrentScreen == MenuScreen.LoadSlots) && TryGetSelectedSlotIndex(out var selectedSlotIndex))
        {
            if (IsNewPress(kb, Keys.R))
                OpenRenamePrompt(selectedSlotIndex);

            if (IsNewPress(kb, Keys.Delete) || IsNewPress(kb, Keys.Back))
                OnDeleteSlotRequested?.Invoke(selectedSlotIndex);
        }

        if (IsNewPress(kb, Keys.Escape) && CurrentScreen == MenuScreen.Pause)
        {
            CloseMenu();
            OnResumeGame?.Invoke();
        }
        else if (IsNewPress(kb, Keys.Escape) && CurrentScreen == MenuScreen.Confirm)
        {
            var action = _confirmCancel;
            _confirmAccept = null;
            _confirmCancel = null;
            CurrentScreen = _returnTo;
            _confirmAsModal = false;
            if (CurrentScreen != MenuScreen.None)
                BuildMenuItems();
            action?.Invoke();
        }
        else if (CurrentScreen == MenuScreen.SaveNamePrompt)
        {
            if (IsNewPress(kb, Keys.Back) && _saveNameInput.Length > 0)
                _saveNameInput = _saveNameInput[..^1];

            if (IsNewPress(kb, Keys.Escape))
            {
                CurrentScreen = MenuScreen.SaveSlots;
                _selectedIndex = Math.Clamp(_pendingSaveSlotIndex - 1, 0, Math.Max(0, _menuItems.Count - 1));
                BuildMenuItems();
            }
        }
        else if (IsNewPress(kb, Keys.Escape)
                 && (CurrentScreen == MenuScreen.SaveSlots
                     || CurrentScreen == MenuScreen.LoadSlots
                     || CurrentScreen == MenuScreen.CharacterCreator
                     || CurrentScreen == MenuScreen.NewGameMaps))
        {
            CurrentScreen = _returnTo;
            BuildMenuItems();
        }
    }

    private void UpdateSettings(KeyboardState kb)
    {
        var leftAdjust = IsNewPress(kb, Keys.Left) || IsNewPress(kb, Keys.A);
        var rightAdjust = IsNewPress(kb, Keys.Right) || IsNewPress(kb, Keys.D);
        var selectedSlider = GetSelectedSettingsSliderItem();

        if (_settingsFocus == SettingsFocus.Content && selectedSlider is { IsSlider: true })
        {
            if (leftAdjust)
                AdjustSlider(selectedSlider.Value, -selectedSlider.Value.SliderStep);

            if (rightAdjust)
                AdjustSlider(selectedSlider.Value, selectedSlider.Value.SliderStep);
        }
        else
        {
            if (leftAdjust)
                _settingsFocus = SettingsFocus.Sidebar;

            if (rightAdjust)
                _settingsFocus = SettingsFocus.Content;
        }

        var hoverPos = GetScaledMousePosition();
        _hoveredSettingsSectionIndex = -1;
        _hoveredSettingsContentIndex = -1;
        for (int i = 0; i < _settingsNavItems.Count; i++)
        {
            if (!_settingsNavItems[i].Bounds.Contains(hoverPos))
                continue;

            _hoveredSettingsSectionIndex = i;
            break;
        }

        for (int i = 0; i < _settingsContentItems.Count; i++)
        {
            if (!_settingsContentItems[i].Bounds.Contains(hoverPos))
                continue;

            _hoveredSettingsContentIndex = i;
            break;
        }

        if (_input.LeftClicked)
        {
            if (_lastSettingsNavRect.Contains(hoverPos))
                _settingsFocus = SettingsFocus.Sidebar;
            else if (_lastSettingsContentRect.Contains(hoverPos))
                _settingsFocus = SettingsFocus.Content;
        }

        if (_lastSettingsContentRect.Contains(hoverPos) && _input.ScrollDelta != 0)
        {
            _settingsFocus = SettingsFocus.Content;
            ScrollSettingsContent(_input.ScrollDelta > 0 ? -1 : 1);
        }

        if (_input.LeftDown && TryUpdateHoveredSlider(hoverPos))
        {
            _settingsFocus = SettingsFocus.Content;
            return;
        }

        if (_settingsFocus == SettingsFocus.Sidebar && _settingsNavItems.Count > 0)
        {
            if (IsNewPress(kb, Keys.Up) || IsNewPress(kb, Keys.W))
            {
                _selectedSettingsSectionIndex = (_selectedSettingsSectionIndex - 1 + _settingsNavItems.Count) % _settingsNavItems.Count;
                RebuildSettingsContent();
            }

            if (IsNewPress(kb, Keys.Down) || IsNewPress(kb, Keys.S))
            {
                _selectedSettingsSectionIndex = (_selectedSettingsSectionIndex + 1) % _settingsNavItems.Count;
                RebuildSettingsContent();
            }
        }
        else if (_settingsContentItems.Count > 0)
        {
            if (IsNewPress(kb, Keys.Up) || IsNewPress(kb, Keys.W))
            {
                _selectedSettingsContentIndex = (_selectedSettingsContentIndex - 1 + _settingsContentItems.Count) % _settingsContentItems.Count;
                EnsureSelectedContentVisible();
            }

            if (IsNewPress(kb, Keys.Down) || IsNewPress(kb, Keys.S))
            {
                _selectedSettingsContentIndex = (_selectedSettingsContentIndex + 1) % _settingsContentItems.Count;
                EnsureSelectedContentVisible();
            }
        }

        var confirm = IsNewPress(kb, Keys.Enter) || IsNewPress(kb, Keys.Space);
        if (!confirm && _input.LeftClicked)
        {
            if (_settingsFocus == SettingsFocus.Sidebar && _selectedSettingsSectionIndex < _settingsNavItems.Count
                && _settingsNavItems[_selectedSettingsSectionIndex].Bounds.Contains(hoverPos))
            {
                confirm = true;
            }
            else if (_settingsFocus == SettingsFocus.Content && _selectedSettingsContentIndex < _settingsContentItems.Count
                && _settingsContentItems[_selectedSettingsContentIndex].Bounds.Contains(hoverPos))
            {
                confirm = true;
            }

            if (!confirm)
            {
                for (int i = 0; i < _settingsNavItems.Count; i++)
                {
                    if (!_settingsNavItems[i].Bounds.Contains(hoverPos))
                        continue;

                    _selectedSettingsSectionIndex = i;
                    _hoveredSettingsSectionIndex = i;
                    _settingsFocus = SettingsFocus.Sidebar;
                    RebuildSettingsContent();
                    confirm = true;
                    break;
                }
            }

            if (!confirm)
            {
                for (int i = 0; i < _settingsContentItems.Count; i++)
                {
                    if (!_settingsContentItems[i].Bounds.Contains(hoverPos))
                        continue;

                    _selectedSettingsContentIndex = i;
                    _hoveredSettingsContentIndex = i;
                    _settingsFocus = SettingsFocus.Content;
                    EnsureSelectedContentVisible();
                    confirm = true;
                    break;
                }
            }
        }

        if (confirm)
        {
            if (_settingsFocus == SettingsFocus.Sidebar)
            {
                RebuildSettingsContent();
            }
            else if (_selectedSettingsContentIndex >= 0 && _selectedSettingsContentIndex < _settingsContentItems.Count)
            {
                _settingsContentItems[_selectedSettingsContentIndex].Action?.Invoke();
            }
        }

        if (IsNewPress(kb, Keys.Escape))
        {
            CurrentScreen = _returnTo;
            _selectedIndex = 0;
            BuildMenuItems();
        }
    }

    private void CaptureKeyBinding(KeyboardState kb)
    {
        foreach (var key in kb.GetPressedKeys())
        {
            if (key == Keys.None || _prevKb.IsKeyDown(key))
                continue;

            if (key == Keys.Escape)
            {
                _waitingForKey = null;
                return;
            }

            _settings.SetKey(_waitingForKey!, key);
            _settings.Save();
            _waitingForKey = null;
            RebuildSettingsContent();
            return;
        }
    }

    private void DrawStandardLayout(SpriteBatch sb, Rectangle navRect, Rectangle contentRect, MenuDefinition def)
    {
        var nav = def.NavPanel;
        var cp = def.ContentPanel;
        var description = _menuItems.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _menuItems.Count
            ? _menuItems[_selectedIndex].Description
            : "";

        var navHeader = CurrentScreen is MenuScreen.Main or MenuScreen.Pause ? nav.Header : _overlayNavHeader;
        var navDescription = CurrentScreen is MenuScreen.Main or MenuScreen.Pause ? nav.HeaderDescription : _overlayNavDescription;
        DrawSectionHeader(sb, navRect, navHeader, navDescription, nav.HeaderColor, nav.HeaderDescColor);
        DrawMenuList(sb, _menuItems, navRect, _selectedIndex, _hoveredIndex, nav.ItemStartY, nav.ItemHeight, nav.ItemGap, def.ItemStyle);

        var heading = GetContentHeading(def);
        var contentDescription = GetContentDescription(description);
        DrawSectionHeader(sb, contentRect, heading, contentDescription, cp.LabelColor, cp.DescriptionColor);

        if (CurrentScreen == MenuScreen.SaveNamePrompt)
        {
            DrawSaveNamePrompt(sb, contentRect, cp);
        }
        else if (CurrentScreen == MenuScreen.CharacterCreator)
        {
            DrawCharacterCreatorDetails(sb, contentRect, cp);
        }
        else if (_menuItems.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _menuItems.Count)
        {
            var item = _menuItems[_selectedIndex];
            var card = new Rectangle(contentRect.X + cp.CardOffsetX, contentRect.Y + cp.CardOffsetY, contentRect.Width - cp.CardOffsetX * 2, cp.CardHeight);
            DrawPanel(sb, card, cp.CardFill, cp.CardBorder, cp.CardBorderThickness);
            DrawText(sb, item.Label, new Vector2(card.X + cp.CardPadding, card.Y + 18), cp.LabelColor);
            if (!string.IsNullOrWhiteSpace(item.Description))
                DrawMultilineText(sb, item.Description!, new Vector2(card.X + cp.CardPadding, card.Y + 52), card.Width - cp.CardPadding * 2, cp.DescriptionColor);
        }
    }

    private void DrawCharacterCreatorDetails(SpriteBatch sb, Rectangle contentRect, MenuContentPanelDef cp)
    {
        // Целевая высота карты: 460 (вмещает ×1.5 превью + rotate-кнопку + цветовой пикер).
        var card = new Rectangle(
            contentRect.X + cp.CardOffsetX,
            contentRect.Y + cp.CardOffsetY,
            contentRect.Width - cp.CardOffsetX * 2,
            Math.Min(460, contentRect.Height - cp.CardOffsetY - 28));

        DrawPanel(sb, card, cp.CardFill, cp.CardBorder, cp.CardBorderThickness);

        // Превью ×1.5 (104×148 → 156×222).
        var preview = new Rectangle(card.X + cp.CardPadding, card.Y + 22, 156, 222);
        DrawPanel(sb, preview, new Color(12, 20, 16, 235), new Color(64, 92, 70), 1);

        var facing = FacingVector(_previewFacing);
        var renderedRealPreview = CharacterPreview?.Render(sb, preview, _characterDraft, facing) == true;
        if (!renderedRealPreview)
        {
            const string fallbackText = "Preview unavailable";
            var fallbackSize = _font.MeasureString(fallbackText);
            DrawText(sb, fallbackText,
                new Vector2(preview.Center.X - fallbackSize.X / 2f, preview.Center.Y - fallbackSize.Y / 2f),
                cp.DescriptionColor);
        }

        // Кнопка поворота под превью.
        _rotateButtonRect = new Rectangle(preview.X, preview.Bottom + 8, preview.Width, 30);
        var rotHover = _rotateButtonRect.Contains(GetScaledMousePosition());
        DrawPanel(sb,
            _rotateButtonRect,
            rotHover ? new Color(60, 88, 70, 230) : new Color(28, 50, 36, 220),
            new Color(96, 140, 104),
            1);
        var rotLabel = $"Повернуть ({FacingLabel(_previewFacing)})";
        var rotSize = _font.MeasureString(SanitizeText(rotLabel));
        DrawText(sb, rotLabel,
            new Vector2(_rotateButtonRect.Center.X - rotSize.X / 2f, _rotateButtonRect.Y + 6),
            cp.LabelColor);

        // Правая колонка: пол / прическа / переключатели цвета.
        var x = preview.Right + 22;
        var y = card.Y + 24;
        DrawText(sb, GetGenderLabel(_characterDraft.Gender), new Vector2(x, y), cp.LabelColor);
        y += 28;
        DrawText(sb, $"Возраст: {_characterDraft.AgeYears}", new Vector2(x, y), cp.DescriptionColor);
        y += 28;
        DrawText(sb, GetSelectedHairOption().Label, new Vector2(x, y), cp.DescriptionColor);
        y += 32;

        _skinTabRect = DrawCreatorColorTab(sb, x, y, "Кожа", 0, AssetManager.ParseHexColor(_characterDraft.SkinColor, Color.White), cp);
        _hairTabRect = DrawCreatorColorTab(sb, x + 130, y, "Волосы", 1, AssetManager.ParseHexColor(_characterDraft.HairColor, Color.White), cp);
        y += 38;

        // HSV color picker (всегда виден).
        EnsureColorPicker();
        var pickerArea = new Rectangle(x, y, Math.Max(200, card.Right - x - cp.CardPadding), 168);
        DrawColorPickerPanel(sb, pickerArea);

        var startState = StartLocationValidator?.Invoke() ?? (true, "Стартовая карта настроена.");
        var startText = startState.Ok ? "Стартовая карта берётся из MTEditor -> Global Settings." : startState.Message;
        DrawMultilineText(sb, startText,
            new Vector2(x, pickerArea.Bottom + 10),
            Math.Max(120, card.Right - x - cp.CardPadding),
            cp.DescriptionColor);
    }

    private Rectangle DrawCreatorColorTab(SpriteBatch sb, int x, int y, string label, int target, Color colorValue, MenuContentPanelDef cp)
    {
        var rect = new Rectangle(x, y, 120, 28);
        var active = _colorTarget == target;
        DrawPanel(sb, rect,
            active ? new Color(60, 92, 72, 230) : new Color(24, 40, 30, 210),
            active ? new Color(120, 180, 130) : new Color(60, 92, 72),
            1);
        var swatch = new Rectangle(rect.X + 6, rect.Y + 6, 16, 16);
        sb.Draw(_pixel, swatch, colorValue);
        DrawPanel(sb, swatch, Color.Transparent, cp.CardBorder, 1);
        DrawText(sb, label, new Vector2(swatch.Right + 8, rect.Y + 6), active ? cp.LabelColor : cp.DescriptionColor);
        return rect;
    }

    private void EnsureColorPicker()
    {
        _colorPicker ??= new HsvColorPicker(_gd);
        var sourceHex = _colorTarget == 1 ? _characterDraft.HairColor : _characterDraft.SkinColor;
        if (_activePicker == null || !string.Equals(sourceHex, _lastPickerSourceColor, StringComparison.OrdinalIgnoreCase))
        {
            _activePicker = _colorPicker.BuildPicker(AssetManager.ParseHexColor(sourceHex, Color.White), Rectangle.Empty);
            _lastPickerSourceColor = sourceHex;
        }
    }

    private void DrawColorPickerPanel(SpriteBatch sb, Rectangle area)
    {
        if (_colorPicker == null || _activePicker == null)
            return;

        _colorPicker.Layout(_activePicker, area);
        _colorPicker.Draw(sb, _activePicker, _pixel);
    }

    private static Vector2 FacingVector(int idx) => idx switch
    {
        0 => new Vector2(0, 1),   // down
        1 => new Vector2(-1, 0),  // left
        2 => new Vector2(0, -1),  // up
        3 => new Vector2(1, 0),   // right
        _ => new Vector2(0, 1)
    };

    private static string FacingLabel(int idx) => idx switch
    {
        0 => "к нам",
        1 => "влево",
        2 => "от нас",
        3 => "вправо",
        _ => ""
    };

    private void DrawSaveNamePrompt(SpriteBatch sb, Rectangle contentRect, MenuContentPanelDef cp)
    {
        var card = new Rectangle(
            contentRect.X + cp.CardOffsetX,
            contentRect.Y + cp.CardOffsetY,
            contentRect.Width - cp.CardOffsetX * 2,
            cp.CardHeight + 36);

        DrawPanel(sb, card, cp.CardFill, cp.CardBorder, cp.CardBorderThickness);
        DrawText(sb, "Имя сохранения", new Vector2(card.X + cp.CardPadding, card.Y + 18), cp.LabelColor);
        DrawMultilineText(
            sb,
            "Введите понятное имя слота. Его можно менять при каждом сохранении.",
            new Vector2(card.X + cp.CardPadding, card.Y + 52),
            card.Width - cp.CardPadding * 2,
            cp.DescriptionColor);

        var inputRect = new Rectangle(card.X + cp.CardPadding, card.Y + 106, card.Width - cp.CardPadding * 2, 40);
        DrawPanel(sb, inputRect, new Color(15, 24, 20, 220), cp.CardBorder, 1);

        var displayName = string.IsNullOrWhiteSpace(_saveNameInput) ? "Введите имя..." : _saveNameInput;
        var textColor = string.IsNullOrWhiteSpace(_saveNameInput) ? new Color(132, 156, 138) : cp.LabelColor;
        DrawText(sb, displayName, new Vector2(inputRect.X + 12, inputRect.Y + 10), textColor);

        if ((DateTime.UtcNow.Millisecond / 500) % 2 == 0)
        {
            var cursorX = inputRect.X + 12 + (int)_font.MeasureString(SanitizeText(_saveNameInput)).X;
            sb.Draw(_pixel, new Rectangle(cursorX, inputRect.Y + 8, 1, inputRect.Height - 16), cp.LabelColor);
        }
    }

    private void DrawSettingsLayout(SpriteBatch sb, Rectangle navRect, Rectangle contentRect, MenuDefinition def)
    {
        var nav = def.NavPanel;
        var content = def.ContentPanel;
        DrawSectionHeader(sb, navRect, "Settings", "Subsections stay visible here all the time.", nav.HeaderColor, nav.HeaderDescColor);
        DrawMenuList(sb, _settingsNavItems, navRect, _selectedSettingsSectionIndex, _hoveredSettingsSectionIndex, nav.ItemStartY, nav.ItemHeight, nav.ItemGap, def.ItemStyle, _settingsFocus == SettingsFocus.Sidebar);

        var section = GetCurrentSettingsSection();
        var heading = section switch
        {
            SettingsSection.General => "General",
            SettingsSection.Controls => "Controls",
            SettingsSection.Service => "Service",
            _ => "Settings"
        };

        var description = section switch
        {
            SettingsSection.General => "Core switches for the current game session.",
            SettingsSection.Controls => _waitingForKey != null
                ? "Press any key to assign it. Escape cancels rebinding."
                : "Current key bindings. Pick an action on the right to rebind it.",
            SettingsSection.Service => "Reset settings or return to the previous menu.",
            _ => string.Empty
        };

        DrawSectionHeader(sb, contentRect, heading, description, content.LabelColor, content.DescriptionColor);
        DrawMenuList(sb, _settingsContentItems, contentRect, _selectedSettingsContentIndex, _hoveredSettingsContentIndex, content.CardOffsetY, 46, 6, def.ItemStyle, _settingsFocus == SettingsFocus.Content, _settingsContentScroll);
    }

    private void DrawMenuList(
        SpriteBatch sb,
        List<MenuItem> items,
        Rectangle panelRect,
        int selectedIndex,
        int hoveredIndex,
        int startOffsetY,
        int itemHeight,
        int gap,
        MenuItemStyleDef style,
        bool accentSelection = true,
        int scrollOffset = 0)
    {
        var x = panelRect.X + 16;
        var width = panelRect.Width - 32;
        var y = panelRect.Y + startOffsetY;
        var visibleHeight = panelRect.Height - startOffsetY - 16;
        var step = itemHeight + gap;
        var maxVisible = Math.Max(1, visibleHeight / step);
        var startIndex = Math.Clamp(scrollOffset, 0, Math.Max(0, items.Count - maxVisible));
        var endIndex = Math.Min(items.Count, startIndex + maxVisible);

        for (int i = 0; i < items.Count; i++)
            items[i] = items[i] with { Bounds = Rectangle.Empty };

        for (int i = startIndex; i < endIndex; i++)
        {
            var bounds = new Rectangle(x, y, width, itemHeight);
            var item = items[i] with { Bounds = bounds };
            items[i] = item;

            var isSelected = i == selectedIndex;
            var isHovered = i == hoveredIndex;
            var bg = isSelected
                ? style.SelectedFill
                : isHovered ? style.HoveredFill
                : style.DefaultFill;
            var border = isSelected ? style.SelectedBorder : isHovered ? style.HoveredBorder : style.DefaultBorder;
            var textColor = isSelected ? style.SelectedText : style.DefaultText;

            DrawPanel(sb, bounds, bg, border, 1);
            if (isSelected && accentSelection && style.AccentBarWidth > 0)
                sb.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, style.AccentBarWidth, bounds.Height), style.AccentBarColor);

            DrawText(sb, item.Label, new Vector2(bounds.X + 14, bounds.Y + 9), textColor);
            if (!string.IsNullOrWhiteSpace(item.Value))
            {
                var safeValue = DisplayText(item.Value!);
                var valueSize = _font.MeasureString(safeValue);
                var valueColor = _waitingForKey == item.Tag ? Color.Yellow : new Color(162, 214, 164);
                sb.DrawString(_font, safeValue, new Vector2(bounds.Right - valueSize.X - 14, bounds.Y + 9), valueColor);
            }

            if (item.IsSlider)
                DrawSlider(sb, item);

            y += itemHeight + gap;
        }

        if (items.Count > maxVisible)
            DrawScrollBar(sb, panelRect, startOffsetY, visibleHeight, startIndex, maxVisible, items.Count);
    }

    private void DrawSectionHeader(SpriteBatch sb, Rectangle panelRect, string title, string description, Color titleColor, Color descriptionColor)
    {
        DrawText(sb, title, new Vector2(panelRect.X + 18, panelRect.Y + 18), titleColor);
        if (!string.IsNullOrWhiteSpace(description))
            DrawMultilineText(sb, description, new Vector2(panelRect.X + 18, panelRect.Y + 46), panelRect.Width - 36, descriptionColor);
    }

    private void DrawBackground(SpriteBatch sb, Rectangle bounds, MenuBackgroundDef background)
    {
        if (background.Type.Equals("solid", StringComparison.OrdinalIgnoreCase))
        {
            sb.Draw(_mainMenuBackground, bounds, background.Color);
            return;
        }

        sb.Draw(_pixel, bounds, background.Color);
        var glowHeight = Math.Max(1, (int)(bounds.Height * MathHelper.Clamp(background.GlowHeightPercent, 0f, 1f)));
        var topGlow = new Rectangle(bounds.X, bounds.Y, bounds.Width, glowHeight);
        sb.Draw(_pixel, topGlow, background.GlowColor);
    }

    private void DrawPanel(SpriteBatch sb, Rectangle rect, Color fill, Color border, int borderThickness)
    {
        sb.Draw(_pixel, rect, fill);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, borderThickness), border);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - borderThickness, rect.Width, borderThickness), border);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, borderThickness, rect.Height), border);
        sb.Draw(_pixel, new Rectangle(rect.Right - borderThickness, rect.Y, borderThickness, rect.Height), border);
    }

    private void DrawScrollBar(SpriteBatch sb, Rectangle panelRect, int startOffsetY, int visibleHeight, int startIndex, int maxVisible, int totalItems)
    {
        var barRect = new Rectangle(panelRect.Right - 10, panelRect.Y + startOffsetY, 4, visibleHeight);
        sb.Draw(_pixel, barRect, new Color(24, 40, 30, 220));

        var thumbHeight = Math.Max(24, (int)(visibleHeight * (maxVisible / (float)totalItems)));
        var maxScroll = Math.Max(1, totalItems - maxVisible);
        var travel = Math.Max(0, visibleHeight - thumbHeight);
        var thumbY = barRect.Y + (int)(travel * (startIndex / (float)maxScroll));
        sb.Draw(_pixel, new Rectangle(barRect.X, thumbY, barRect.Width, thumbHeight), new Color(120, 168, 128));
    }

    private void DrawText(SpriteBatch sb, string text, Vector2 position, Color color)
    {
        sb.DrawString(_font, DisplayText(text), Snap(position), color);
    }

    private void DrawMultilineText(SpriteBatch sb, string text, Vector2 position, int maxWidth, Color color)
    {
        var words = DisplayText(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = string.Empty;
        var y = position.Y;

        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
            if (_font.MeasureString(candidate).X > maxWidth && !string.IsNullOrEmpty(line))
            {
                sb.DrawString(_font, line, Snap(new Vector2(position.X, y)), color);
                line = word;
                y += 20;
            }
            else
            {
                line = candidate;
            }
        }

        if (!string.IsNullOrEmpty(line))
            sb.DrawString(_font, line, Snap(new Vector2(position.X, y)), color);
    }

    private static Vector2 Snap(Vector2 position)
        => new(MathF.Round(position.X), MathF.Round(position.Y));

    private static SamplerState GetTextFriendlySampler(float uiScale)
        => SamplerState.PointClamp;

    // Fractions of the native display used to generate windowed options that
    // always match the user's monitor aspect ratio (16:9, 16:10, 21:9, …).
    private static readonly float[] ResolutionScaleSteps =
    {
        1.00f, 0.90f, 0.80f, 0.75f, 0.66f, 0.60f, 0.50f, 0.40f, 0.33f, 0.25f
    };

    private void RefreshResolutionOptions()
    {
        _availableResolutionOptions.Clear();

        var nativeSize = GameEngine.Instance.GetNativeDisplayPixelSize();
        var nativePxW = nativeSize.X;
        var nativePxH = nativeSize.Y;

        bool Fits(Point size)
            => size.X >= GameEngine.MinResolutionWidth
            && size.Y >= GameEngine.MinResolutionHeight
            && size.X <= nativePxW
            && size.Y <= nativePxH;

        void Add(Point pixelSize)
        {
            if (!Fits(pixelSize))
                return;

            var option = new ResolutionOption(
                GameEngine.Instance.GetWindowSizeForPixelSize(pixelSize),
                pixelSize);

            if (!_availableResolutionOptions.Contains(option))
                _availableResolutionOptions.Add(option);
        }

        Add(new Point(nativePxW, nativePxH));

        foreach (var step in ResolutionScaleSteps)
        {
            var w = (int)MathF.Round(nativePxW * step);
            var h = (int)MathF.Round(nativePxH * step);
            if ((w & 1) == 1) w--;
            if ((h & 1) == 1) h--;
            Add(new Point(w, h));
        }

        Add(GameEngine.Instance.GetPixelSizeForWindowSize(GameEngine.Instance.GetUiClientSize()));
        Add(GameEngine.Instance.GetPixelSizeForWindowSize(new Point(_settings.ScreenWidth, _settings.ScreenHeight)));

        _availableResolutionOptions.Sort((a, b) =>
        {
            var areaCompare = (a.PixelSize.X * a.PixelSize.Y).CompareTo(b.PixelSize.X * b.PixelSize.Y);
            return areaCompare != 0 ? areaCompare : a.PixelSize.X.CompareTo(b.PixelSize.X);
        });
    }

    private void ToggleResolutionDropdown()
    {
        RefreshResolutionOptions();
        _resolutionDropdownOpen = !_resolutionDropdownOpen;
        RebuildSettingsContent();
    }

    private void ToggleLanguageDropdown()
    {
        _languageDropdownOpen = !_languageDropdownOpen;
        RebuildSettingsContent();
    }

    private IReadOnlyList<LocalizationLanguage> GetLocalizationOptions()
    {
        var loc = GetLocalizationManager();
        if (loc is { Languages.Count: > 0 })
            return loc.Languages;

        return new[]
        {
            new LocalizationLanguage { Id = LocalizationManager.RussianId, Name = "Русский" },
            new LocalizationLanguage { Id = LocalizationManager.EnglishId, Name = "English" }
        };
    }

    private void ApplyLanguage(LocalizationLanguage language)
    {
        _settings.LocalizationId = language.Id;
        _settings.Save();
        GetLocalizationManager()?.SetLanguage(language.Id);
        _languageDropdownOpen = false;
        BuildMenuItems();
        RebuildSettingsContent();
    }

    private static LocalizationManager? GetLocalizationManager()
        => ServiceLocator.Has<LocalizationManager>() ? ServiceLocator.Get<LocalizationManager>() : null;

    private void ApplyResolutionOption(ResolutionOption resolution)
    {
        if (_settings.ScreenWidth == resolution.WindowSize.X && _settings.ScreenHeight == resolution.WindowSize.Y)
        {
            _resolutionDropdownOpen = false;
            RebuildSettingsContent();
            return;
        }

        _pendingResolutionPrevious = new Point(_settings.ScreenWidth, _settings.ScreenHeight);
        _pendingResolutionTarget = resolution.WindowSize;
        _pendingResolutionConfirmation = true;
        _pendingResolutionSeconds = 7f;
        _pendingResolutionDeadlineUtc = DateTime.UtcNow.AddSeconds(_pendingResolutionSeconds);
        _settings.ScreenWidth = resolution.WindowSize.X;
        _settings.ScreenHeight = resolution.WindowSize.Y;
        _resolutionDropdownOpen = false;
        OnResolutionChanged?.Invoke(resolution.WindowSize.X, resolution.WindowSize.Y);
        RefreshResolutionOptions();
        var savedReturnTo = _returnTo;
        OpenConfirmation(
            "Подтвердить разрешение",
            BuildPendingResolutionDescription(),
            () => { ConfirmPendingResolutionChange(); _returnTo = savedReturnTo; },
            () => { RevertPendingResolutionChange(); _returnTo = savedReturnTo; });
    }

    private void ConfirmPendingResolutionChange()
    {
        if (!_pendingResolutionConfirmation)
            return;

        _pendingResolutionConfirmation = false;
        _pendingResolutionSeconds = 0f;
        _pendingResolutionDeadlineUtc = default;
        _settings.Save();
        RebuildSettingsContent();
    }

    private void RevertPendingResolutionChange()
    {
        if (!_pendingResolutionConfirmation)
            return;

        _pendingResolutionConfirmation = false;
        _pendingResolutionSeconds = 0f;
        _pendingResolutionDeadlineUtc = default;
        _settings.ScreenWidth = _pendingResolutionPrevious.X;
        _settings.ScreenHeight = _pendingResolutionPrevious.Y;
        OnResolutionChanged?.Invoke(_pendingResolutionPrevious.X, _pendingResolutionPrevious.Y);
        RefreshResolutionOptions();
        RebuildSettingsContent();
    }

    private void UpdatePendingResolutionConfirmation()
    {
        if (!_pendingResolutionConfirmation || CurrentScreen != MenuScreen.Confirm)
            return;

        _pendingResolutionSeconds = Math.Max(0f, (float)(_pendingResolutionDeadlineUtc - DateTime.UtcNow).TotalSeconds);
        _overlayDescription = BuildPendingResolutionDescription();

        if (_pendingResolutionSeconds <= 0f)
        {
            RevertPendingResolutionChange();
            var action = _confirmCancel;
            _confirmAccept = null;
            _confirmCancel = null;
            CurrentScreen = _returnTo;
            _confirmAsModal = false;
            if (CurrentScreen != MenuScreen.None)
                BuildMenuItems();
            action?.Invoke();
        }
    }

    private string BuildPendingResolutionDescription()
    {
        var pixelSize = GameEngine.Instance.GetPixelSizeForWindowSize(_pendingResolutionTarget);
        return $"Оставить {pixelSize.X}x{pixelSize.Y}? Разрешение вернётся обратно через {Math.Max(0, (int)MathF.Ceiling(_pendingResolutionSeconds))} сек.";
    }

    private void BuildMenuItems()
    {
        _menuItems.Clear();

        if (CurrentScreen == MenuScreen.Confirm)
        {
            _menuItems.Add(new MenuItem(
                "Да",
                Description: _overlayDescription,
                Action: () =>
                {
                    var action = _confirmAccept;
                    _confirmAccept = null;
                    _confirmCancel = null;
                    CurrentScreen = _returnTo;
                    _confirmAsModal = false;
                    if (CurrentScreen != MenuScreen.None)
                        BuildMenuItems();
                    action?.Invoke();
                }));
            _menuItems.Add(new MenuItem(
                "Нет",
                Description: "Отменить действие и вернуться назад.",
                Action: () =>
                {
                    var action = _confirmCancel;
                    _confirmAccept = null;
                    _confirmCancel = null;
                    CurrentScreen = _returnTo;
                    _confirmAsModal = false;
                    if (CurrentScreen != MenuScreen.None)
                        BuildMenuItems();
                    action?.Invoke();
                }));
            return;
        }

        if (CurrentScreen == MenuScreen.SaveNamePrompt)
        {
            _menuItems.Add(new MenuItem(
                _saveNamePromptIsRename ? "Переименовать" : "Сохранить",
                Description: _saveNamePromptIsRename
                    ? "Применить новое имя к выбранному слоту."
                    : "Записать текущий мир в выбранный слот.",
                Action: () =>
                {
                    var finalName = string.IsNullOrWhiteSpace(_saveNameInput)
                        ? $"Слот {_pendingSaveSlotIndex}"
                        : _saveNameInput.Trim();
                    if (_saveNamePromptIsRename)
                        OnRenameSlotRequested?.Invoke(_pendingSaveSlotIndex, finalName);
                    else
                        OnSaveSlotConfirmed?.Invoke(_pendingSaveSlotIndex, finalName);
                }));
            _menuItems.Add(new MenuItem(
                "Назад",
                Description: "Вернуться к выбору слота без сохранения.",
                Action: () =>
                {
                    CurrentScreen = MenuScreen.SaveSlots;
                    _selectedIndex = Math.Clamp(_pendingSaveSlotIndex - 1, 0, Math.Max(0, _menuItems.Count - 1));
                    BuildMenuItems();
                }));
            return;
        }

        if (CurrentScreen == MenuScreen.CharacterCreator)
        {
            BuildCharacterCreatorItems();
            return;
        }

        if (CurrentScreen == MenuScreen.NewGameMaps)
        {
            var allMaps = MapCatalogProvider?.Invoke() ?? Array.Empty<MapCatalogEntry>();
            var maps = allMaps.Where(m => m.InGame).ToList();
            if (maps.Count == 0)
            {
                _menuItems.Add(new MenuItem(
                    "Нет игровых карт",
                    Description: "Откройте MTEditor и пометьте хотя бы одну карту как игровую.",
                    Action: null));
            }
            else
            {
                foreach (var map in maps)
                {
                    var mapId = map.Id;
                    _menuItems.Add(new MenuItem(
                        map.Name,
                        Description: $"Идентификатор: {map.Id}",
                        Action: () => OnStartGameWithMap?.Invoke(mapId)));
                }
            }

            _menuItems.Add(new MenuItem(
                "Назад",
                Description: "Вернуться в главное меню.",
                Action: () =>
                {
                    CurrentScreen = _returnTo;
                    BuildMenuItems();
                }));

            if (_selectedIndex >= _menuItems.Count)
                _selectedIndex = 0;
            return;
        }

        if (CurrentScreen == MenuScreen.SaveSlots || CurrentScreen == MenuScreen.LoadSlots)
        {
            var summaries = SaveSlotProvider?.Invoke() ?? Array.Empty<SaveSlotSummary>();
            foreach (var summary in summaries)
            {
                var slotIndex = summary.SlotIndex;
                var canLoad = CurrentScreen != MenuScreen.LoadSlots || summary.HasData;
                _menuItems.Add(new MenuItem(
                    summary.Title,
                    Description: summary.Description,
                    Value: summary.HasData ? "USED" : "EMPTY",
                    Tag: slotIndex.ToString(),
                    Action: canLoad
                        ? () =>
                        {
                            if (CurrentScreen == MenuScreen.SaveSlots)
                                OpenSaveNamePrompt(slotIndex);
                            else
                                OnLoadSlotSelected?.Invoke(slotIndex);
                        }
                        : null));
            }

            _menuItems.Add(new MenuItem(
                "Назад",
                Description: "Вернуться в предыдущее меню.",
                Action: () =>
                {
                    CurrentScreen = _returnTo;
                    BuildMenuItems();
                }));

            if (_selectedIndex >= _menuItems.Count)
                _selectedIndex = 0;
            return;
        }

        var def = CurrentScreen switch
        {
            MenuScreen.Main => _mainMenuDef,
            MenuScreen.Pause => _pauseMenuDef,
            _ => null
        };

        if (def != null)
        {
            foreach (var item in def.Items)
            {
                if (!EvaluateCondition(item.Condition))
                    continue;

                _actionMap.TryGetValue(item.Action, out var action);
                _menuItems.Add(new MenuItem(
                    item.Label,
                    Description: item.Description,
                    Action: action));
            }
        }

        if (_selectedIndex >= _menuItems.Count)
            _selectedIndex = 0;
    }

    private void OpenSettings()
    {
        _returnTo = CurrentScreen;
        CurrentScreen = MenuScreen.Settings;
        _waitingForKey = null;
        _settingsFocus = SettingsFocus.Content;
        _selectedSettingsSectionIndex = 0;
        _selectedSettingsContentIndex = 0;
        _hoveredSettingsSectionIndex = -1;
        _hoveredSettingsContentIndex = -1;
        _settingsContentScroll = 0;
        _prevKb = Keyboard.GetState();
        BuildSettingsNavigation();
        RebuildSettingsContent();
    }

    private void OpenSaveSlots()
    {
        _returnTo = CurrentScreen;
        CurrentScreen = MenuScreen.SaveSlots;
        _overlayTitle = "Сохранение";
        _overlayDescription = "Выберите слот для сохранения текущего мира.";
        _overlayNavHeader = "Слоты";
        _overlayNavDescription = "Сохранение перезапишет выбранный слот.";
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    private void OpenCharacterCreator()
    {
        _returnTo = CurrentScreen;
        CurrentScreen = MenuScreen.CharacterCreator;
        _overlayTitle = "Создание персонажа";
        _overlayDescription = "Выберите базовую внешность героя перед входом в мир.";
        _overlayNavHeader = "Персонаж";
        _overlayNavDescription = "A/D or Left/Right меняют выбранный параметр.";
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        EnsureCharacterHairSelection();
        BuildMenuItems();
    }

    private void OpenNewGameMaps()
    {
        _returnTo = CurrentScreen;
        CurrentScreen = MenuScreen.NewGameMaps;
        _overlayTitle = "Выбор карты";
        _overlayDescription = "Выберите карту, с которой начнётся новая игра.";
        _overlayNavHeader = "Карты";
        _overlayNavDescription = "Доступны только карты, помеченные в редакторе как игровые.";
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    public void ReturnToSaveSlots(int selectedSlotIndex)
    {
        CurrentScreen = MenuScreen.SaveSlots;
        _saveNamePromptIsRename = false;
        _confirmAsModal = false;
        _overlayTitle = "Сохранение";
        _overlayDescription = "Выберите слот для сохранения текущего мира.";
        _overlayNavHeader = "Слоты";
        _overlayNavDescription = "Сохранение перезапишет выбранный слот.";
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
        _selectedIndex = Math.Clamp(selectedSlotIndex - 1, 0, Math.Max(0, _menuItems.Count - 1));
    }

    public void ReturnToLoadSlots(int selectedSlotIndex)
    {
        CurrentScreen = MenuScreen.LoadSlots;
        _saveNamePromptIsRename = false;
        _confirmAsModal = false;
        _overlayTitle = "Загрузка";
        _overlayDescription = "Выберите слот сохранения.";
        _overlayNavHeader = "Слоты";
        _overlayNavDescription = "Можно загрузить любой заполненный слот.";
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
        _selectedIndex = Math.Clamp(selectedSlotIndex - 1, 0, Math.Max(0, _menuItems.Count - 1));
    }

    private void OpenSaveNamePrompt(int slotIndex)
    {
        _pendingSaveSlotIndex = slotIndex;
        _saveNamePromptIsRename = false;
        _saveNameInput = SaveNameSuggestionProvider?.Invoke(slotIndex)?.Trim() ?? $"Слот {slotIndex}";
        CurrentScreen = MenuScreen.SaveNamePrompt;
        _overlayTitle = "Имя сохранения";
        _overlayDescription = "Настройте имя, под которым слот будет виден в списке.";
        _overlayNavHeader = $"Слот {slotIndex}";
        _overlayNavDescription = "Введите имя и подтвердите сохранение.";
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    public void OpenRenamePrompt(int slotIndex)
    {
        _pendingSaveSlotIndex = slotIndex;
        _saveNamePromptIsRename = true;
        _saveNameInput = SaveNameSuggestionProvider?.Invoke(slotIndex)?.Trim() ?? $"Слот {slotIndex}";
        CurrentScreen = MenuScreen.SaveNamePrompt;
        _overlayTitle = "Переименование";
        _overlayDescription = "Введите новое имя для сохранения.";
        _overlayNavHeader = $"Слот {slotIndex}";
        _overlayNavDescription = "Переименование меняет только отображаемое имя слота.";
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    private void OpenLoadSlots()
    {
        _returnTo = CurrentScreen;
        CurrentScreen = MenuScreen.LoadSlots;
        _overlayTitle = "Загрузка";
        _overlayDescription = "Выберите слот сохранения.";
        _overlayNavHeader = "Слоты";
        _overlayNavDescription = "Можно загрузить любой заполненный слот.";
        _selectedIndex = 0;
        _hoveredIndex = -1;
        _prevKb = Keyboard.GetState();
        BuildMenuItems();
    }

    private void BuildCharacterCreatorItems()
    {
        EnsureCharacterHairSelection();
        var startState = StartLocationValidator?.Invoke() ?? (true, "Стартовая карта настроена.");
        var hairLabel = GetSelectedHairOption().Label;

        _menuItems.Add(new MenuItem(
            "Пол",
            Value: GetGenderLabel(_characterDraft.Gender),
            Description: "Меняет пол персонажа. Причёски ниже фильтруются под выбранный пол.",
            Action: () => { ToggleCharacterGender(); BuildMenuItems(); }));
        _menuItems.Add(new MenuItem(
            "Возраст",
            Value: $"{_characterDraft.AgeYears}",
            Description: $"Возраст персонажа на старте. Минимум: {PlayerCharacterDraft.MinAgeYears}.",
            Action: () => { AdjustCharacterAge(1); BuildMenuItems(); }));
        _menuItems.Add(new MenuItem(
            "Причёска",
            Value: hairLabel,
            Description: "Выберите один из hair-прототипов, доступных для этого пола.",
            Action: () => { CycleHairStyle(1); BuildMenuItems(); }));
        _menuItems.Add(new MenuItem(
            "Начать игру",
            Description: startState.Ok
                ? "Создать персонажа и загрузить стартовую карту из Global Settings."
                : startState.Message,
            Action: startState.Ok ? () => OnStartGameRequested?.Invoke(_characterDraft.Clone()) : null));
        _menuItems.Add(new MenuItem(
            "Назад",
            Description: "Вернуться в главное меню.",
            Action: () =>
            {
                CurrentScreen = _returnTo;
                BuildMenuItems();
            }));
    }

    private void HandleCharacterCreatorMouse()
    {
        var pos = GetScaledMousePosition();

        if (_input.LeftClicked)
        {
            if (_rotateButtonRect.Contains(pos))
            {
                _previewFacing = (_previewFacing + 1) % 4;
                return;
            }

            if (_skinTabRect.Contains(pos))
            {
                _colorTarget = 0;
                _activePicker = null;
                return;
            }

            if (_hairTabRect.Contains(pos))
            {
                _colorTarget = 1;
                _activePicker = null;
                return;
            }
        }

        // Drag по hue/sv: реагируем пока кнопка зажата.
        if (_input.LeftDown && _colorPicker != null && _activePicker != null
            && _colorPicker.HandleMouse(_activePicker, pos, leftDown: true))
        {
            var color = _colorPicker.CurrentColor(_activePicker);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}FF";
            if (_colorTarget == 1)
                _characterDraft.HairColor = hex;
            else
                _characterDraft.SkinColor = hex;
            _lastPickerSourceColor = hex;
        }
    }

    private void AdjustCharacterCreatorSelection(int delta)
    {
        if (CurrentScreen != MenuScreen.CharacterCreator || _selectedIndex < 0)
            return;

        switch (_selectedIndex)
        {
            case 0:
                ToggleCharacterGender();
                break;
            case 1:
                AdjustCharacterAge(delta);
                break;
            case 2:
                CycleHairStyle(delta);
                break;
            default:
                return;
        }

        BuildMenuItems();
    }

    private void ToggleCharacterGender()
    {
        _characterDraft.Gender = string.Equals(_characterDraft.Gender, "Female", StringComparison.OrdinalIgnoreCase)
            ? "Male"
            : "Female";
        EnsureCharacterHairSelection();
    }

    private void AdjustCharacterAge(int delta)
    {
        _characterDraft.AgeYears = Math.Clamp(
            _characterDraft.AgeYears + delta,
            PlayerCharacterDraft.MinAgeYears,
            PlayerCharacterDraft.MaxAgeYears);
    }

    private void CycleHairStyle(int delta)
    {
        var options = GetHairOptionsForCurrentGender();
        if (options.Count == 0)
            return;

        var index = options.FindIndex(option => string.Equals(option.Id, _characterDraft.HairStyleId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = 0;

        index = PositiveModulo(index + delta, options.Count);
        _characterDraft.HairStyleId = options[index].Id;
    }

    private void EnsureCharacterHairSelection()
    {
        _characterDraft.Gender = string.Equals(_characterDraft.Gender, "Female", StringComparison.OrdinalIgnoreCase)
            ? "Female"
            : "Male";
        _characterDraft.AgeYears = Math.Clamp(_characterDraft.AgeYears, PlayerCharacterDraft.MinAgeYears, PlayerCharacterDraft.MaxAgeYears);

        _characterHairColorIndex = FindPresetIndex(CharacterCreatorDefaults.HairColors, _characterDraft.HairColor, _characterHairColorIndex);
        _characterSkinColorIndex = FindPresetIndex(CharacterCreatorDefaults.SkinColors, _characterDraft.SkinColor, _characterSkinColorIndex);

        var options = GetHairOptionsForCurrentGender();
        if (options.Count == 0)
        {
            _characterDraft.HairStyleId = "";
            return;
        }

        if (options.Any(option => string.Equals(option.Id, _characterDraft.HairStyleId, StringComparison.OrdinalIgnoreCase)))
            return;

        _characterDraft.HairStyleId = options[0].Id;
    }

    private List<CharacterCreatorHairOption> GetHairOptionsForCurrentGender()
    {
        var provided = HairOptionsProvider?.Invoke(_characterDraft.Gender) ?? CharacterCreatorDefaults.EmptyHairOptions;
        var options = provided
            .Where(option => option != null)
            .GroupBy(option => option.Id ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (options.All(option => !string.IsNullOrEmpty(option.Id)))
            options.Insert(0, new CharacterCreatorHairOption { Id = "", Label = "Без волос", Gender = "Unisex" });

        return options;
    }

    private CharacterCreatorHairOption GetSelectedHairOption()
    {
        var options = GetHairOptionsForCurrentGender();
        return options.FirstOrDefault(option => string.Equals(option.Id, _characterDraft.HairStyleId, StringComparison.OrdinalIgnoreCase))
               ?? options.FirstOrDefault()
               ?? new CharacterCreatorHairOption { Id = "", Label = "Без волос", Gender = "Unisex" };
    }

    private static string GetGenderLabel(string gender)
        => string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase) ? "Женский" : "Мужской";

    private static int FindPresetIndex(IReadOnlyList<string> presets, string value, int fallback)
    {
        for (var i = 0; i < presets.Count; i++)
        {
            if (string.Equals(presets[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return Math.Clamp(fallback, 0, Math.Max(0, presets.Count - 1));
    }

    private static int PositiveModulo(int value, int divisor)
        => divisor <= 0 ? 0 : ((value % divisor) + divisor) % divisor;

    private void BuildSettingsNavigation()
    {
        _settingsNavItems.Clear();
        _settingsNavItems.Add(new MenuItem("Общие", Description: "Core toggles and shared options."));
        _settingsNavItems.Add(new MenuItem("Клавиши", Description: "All current keyboard bindings."));
        _settingsNavItems.Add(new MenuItem("Сервис", Description: "Reset settings or go back."));
    }

    private void RebuildSettingsContent()
    {
        _settingsContentItems.Clear();

        switch (GetCurrentSettingsSection())
        {
            case SettingsSection.General:
                _settingsContentScroll = 0;
                _settingsContentItems.Add(new MenuItem(
                    "Масштаб интерфейса",
                    Value: $"{GetUiScale():0.00}x",
                    Description: "Общий масштаб меню, HUD и окон интерфейса.",
                    IsSlider: true,
                    SliderValue: GetUiScale(),
                    SliderMin: MinUiScale,
                    SliderMax: MaxUiScale,
                    SliderStep: UiScaleStep,
                    SliderChanged: value =>
                    {
                        _settings.UiScale = Math.Clamp(value, MinUiScale, MaxUiScale);
                        _settings.Save();
                        RebuildSettingsContent();
                    }));
                var loc = GetLocalizationManager();
                var currentLanguageName = loc?.GetLanguageName(_settings.LocalizationId) ?? _settings.LocalizationId;
                _settingsContentItems.Add(new MenuItem(
                    LocalizationManager.T("--ui-settings-language"),
                    Value: _languageDropdownOpen ? "^" : currentLanguageName,
                    Description: "Выбор активной папки из Content/Localization.",
                    Action: ToggleLanguageDropdown));
                if (_languageDropdownOpen)
                {
                    foreach (var language in GetLocalizationOptions())
                    {
                        var isCurrent = string.Equals(language.Id, _settings.LocalizationId, StringComparison.OrdinalIgnoreCase);
                        _settingsContentItems.Add(new MenuItem(
                            isCurrent ? $"> {language.Name}" : $"  {language.Name}",
                            Value: language.Id,
                            Description: $"Content/Localization/{language.Id}",
                            Action: () => ApplyLanguage(language)));
                    }
                }
                _settingsContentItems.Add(new MenuItem(
                    "--mm-devmode",
                    Value: _settings.DevMode ? "ON" : "OFF",
                    Description: "Controls access to dev-only UI, console and developer map entry.",
                    Action: () =>
                    {
                        _settings.DevMode = !_settings.DevMode;
                        _settings.Save();
                        DevConsole.DevMode = _settings.DevMode;
                        BuildMenuItems();
                        RebuildSettingsContent();
                    }));
                break;

            case SettingsSection.Controls:
                foreach (var action in GameSettings.DefaultKeys.Keys)
                {
                    var label = GameSettings.ActionLabels.GetValueOrDefault(action, action);
                    _settingsContentItems.Add(new MenuItem(
                        label,
                        Value: GetKeyDisplayName(_settings.GetKey(action)),
                        Tag: action,
                        Description: $"Current binding for {label}. Press Enter to remap.",
                        Action: () => _waitingForKey = action));
                }
                break;

            case SettingsSection.Service:
                _settingsContentScroll = 0;
                _settingsContentItems.Add(new MenuItem(
                    "Сбросить настройки",
                    Description: "Restore developer mode and all key bindings to their default values.",
                    Action: () =>
                    {
                        _settings.ResetToDefaults();
                        _settings.Save();
                        DevConsole.DevMode = _settings.DevMode;
                        BuildMenuItems();
                        RebuildSettingsContent();
                    }));
                _settingsContentItems.Add(new MenuItem(
                    "Назад",
                    Description: "Return to the previous menu screen.",
                    Action: () =>
                    {
                        CurrentScreen = _returnTo;
                        _selectedIndex = 0;
                        BuildMenuItems();
                    }));
                break;
        }

        if (_selectedSettingsContentIndex >= _settingsContentItems.Count)
            _selectedSettingsContentIndex = Math.Max(0, _settingsContentItems.Count - 1);

        EnsureSelectedContentVisible();
    }

    private SettingsSection GetCurrentSettingsSection()
        => (SettingsSection)Math.Clamp(_selectedSettingsSectionIndex, 0, _settingsNavItems.Count - 1);

    private string GetScreenTitle(MenuDefinition def)
    {
        return CurrentScreen switch
        {
            MenuScreen.Settings => "SETTINGS",
            MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.SaveNamePrompt
                or MenuScreen.Confirm or MenuScreen.CharacterCreator => _overlayTitle,
            _ => def.Title
        };
    }

    private string GetContentHeading(MenuDefinition def)
    {
        return CurrentScreen switch
        {
            MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.SaveNamePrompt
                or MenuScreen.Confirm or MenuScreen.CharacterCreator => _overlayTitle,
            _ => def.Title
        };
    }

    private string GetContentDescription(string? selectedDescription)
    {
        if (CurrentScreen == MenuScreen.Confirm)
            return _overlayDescription;

        if (CurrentScreen is MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.SaveNamePrompt or MenuScreen.CharacterCreator)
            return string.IsNullOrWhiteSpace(selectedDescription) ? _overlayDescription : selectedDescription;

        return selectedDescription ?? string.Empty;
    }

    private string GetSettingsHintText()
    {
        return _waitingForKey != null
            ? "Press any key  Esc cancel"
            : "W/S move  A/D switch pane  Enter apply  Esc back";
    }

    private string GetScreenHintText(MenuHintDef hintDef)
    {
        return CurrentScreen switch
        {
            MenuScreen.Settings => GetSettingsHintText(),
            MenuScreen.SaveSlots => "W/S move  Enter save  R rename  Del delete  Esc back",
            MenuScreen.LoadSlots => "W/S move  Enter load  R rename  Del delete  Esc back",
            MenuScreen.SaveNamePrompt => "Type name  Enter save  Backspace erase  Esc back",
            MenuScreen.Confirm => "W/S move  Enter confirm  Esc cancel",
            MenuScreen.CharacterCreator => "W/S move  A/D change  Enter apply/start  Esc back",
            _ => hintDef.Text
        };
    }

    private bool TryGetSelectedSlotIndex(out int slotIndex)
    {
        slotIndex = 0;
        if (_selectedIndex < 0 || _selectedIndex >= _menuItems.Count)
            return false;

        var tag = _menuItems[_selectedIndex].Tag;
        return int.TryParse(tag, out slotIndex);
    }

    private Vector2 ResolveHintPosition(Rectangle pageRect, MenuHintDef hintDef, Vector2 hintSize)
    {
        return hintDef.Align.ToLowerInvariant() switch
        {
            "left-top" => new Vector2(pageRect.X + hintDef.OffsetX, pageRect.Y + hintDef.OffsetY),
            "left-bottom" => new Vector2(pageRect.X + hintDef.OffsetX, pageRect.Bottom - hintDef.OffsetY),
            "right-top" => new Vector2(pageRect.Right - hintSize.X - hintDef.OffsetX, pageRect.Y + hintDef.OffsetY),
            _ => new Vector2(pageRect.Right - hintSize.X - hintDef.OffsetX, pageRect.Bottom - hintDef.OffsetY)
        };
    }

    private bool IsNewPress(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && !_prevKb.IsKeyDown(key);

    private static string GetKeyDisplayName(Keys key) => key switch
    {
        Keys.None => "-",
        Keys.Space => "Space",
        Keys.Escape => "Esc",
        Keys.Tab => "Tab",
        Keys.Enter => "Enter",
        Keys.Back => "Backspace",
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.LeftShift or Keys.RightShift => "Shift",
        Keys.LeftControl or Keys.RightControl => "Ctrl",
        Keys.LeftAlt or Keys.RightAlt => "Alt",
        _ => key.ToString()
    };

    private static string SanitizeText(string text)
    {
        return text
            .Replace('↻', 'R')
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('−', '-')
            .Replace('←', '<')
            .Replace('→', '>')
            .Replace('↑', '^')
            .Replace('↓', 'v');
    }

    private static string DisplayText(string? text)
        => SanitizeText(LocalizationManager.T(text));

    private void ScrollSettingsContent(int delta)
    {
        var maxVisible = 10;
        var maxScroll = Math.Max(0, _settingsContentItems.Count - maxVisible);
        _settingsContentScroll = Math.Clamp(_settingsContentScroll + delta, 0, maxScroll);
    }

    private void EnsureSelectedContentVisible()
    {
        var maxVisible = 10;
        if (_selectedSettingsContentIndex < _settingsContentScroll)
            _settingsContentScroll = _selectedSettingsContentIndex;
        else if (_selectedSettingsContentIndex >= _settingsContentScroll + maxVisible)
            _settingsContentScroll = _selectedSettingsContentIndex - maxVisible + 1;

        _settingsContentScroll = Math.Max(0, _settingsContentScroll);
    }

    private float GetUiScale()
        => Math.Clamp(_settings.UiScale, MinUiScale, MaxUiScale);

    private Rectangle GetScaledViewportBounds()
        => GameEngine.Instance.GetUiLogicalBounds(GetUiScale());

    private Point GetScaledMousePosition()
    {
        var scale = GetUiScale();
        var mouse = _input.MousePosition;
        return new Point(
            (int)MathF.Round(mouse.X / scale),
            (int)MathF.Round(mouse.Y / scale));
    }

    private MenuItem? GetSelectedSettingsSliderItem()
    {
        if (_selectedSettingsContentIndex < 0 || _selectedSettingsContentIndex >= _settingsContentItems.Count)
            return null;

        var item = _settingsContentItems[_selectedSettingsContentIndex];
        return item.IsSlider ? item : null;
    }

    private bool TryUpdateHoveredSlider(Point hoverPos)
    {
        for (var i = 0; i < _settingsContentItems.Count; i++)
        {
            var item = _settingsContentItems[i];
            if (!item.IsSlider)
                continue;

            var sliderRect = GetSliderRect(item.Bounds);
            if (!sliderRect.Contains(hoverPos))
                continue;

            _selectedSettingsContentIndex = i;
            _hoveredSettingsContentIndex = i;
            ApplySliderValueFromPosition(item, hoverPos.X);
            return true;
        }

        return false;
    }

    private void AdjustSlider(MenuItem item, float delta)
    {
        if (!item.IsSlider || item.SliderChanged == null)
            return;

        var next = Math.Clamp(item.SliderValue + delta, item.SliderMin, item.SliderMax);
        next = QuantizeSliderValue(next, item.SliderMin, item.SliderStep);
        item.SliderChanged(next);
    }

    private void ApplySliderValueFromPosition(MenuItem item, int mouseX)
    {
        if (!item.IsSlider || item.SliderChanged == null)
            return;

        var sliderRect = GetSliderRect(item.Bounds);
        var ratio = sliderRect.Width <= 1
            ? 0f
            : Math.Clamp((mouseX - sliderRect.X) / (float)sliderRect.Width, 0f, 1f);
        var value = MathHelper.Lerp(item.SliderMin, item.SliderMax, ratio);
        value = QuantizeSliderValue(value, item.SliderMin, item.SliderStep);
        item.SliderChanged(value);
    }

    private static float QuantizeSliderValue(float value, float min, float step)
    {
        if (step <= 0f)
            return value;

        var units = MathF.Round((value - min) / step);
        return min + units * step;
    }

    private void DrawSlider(SpriteBatch sb, MenuItem item)
    {
        var sliderRect = GetSliderRect(item.Bounds);
        var trackRect = new Rectangle(sliderRect.X, sliderRect.Center.Y - 2, sliderRect.Width, 4);
        var ratio = item.SliderMax <= item.SliderMin
            ? 0f
            : Math.Clamp((item.SliderValue - item.SliderMin) / (item.SliderMax - item.SliderMin), 0f, 1f);
        var fillWidth = Math.Max(4, (int)MathF.Round(trackRect.Width * ratio));
        var knobX = sliderRect.X + (int)MathF.Round((sliderRect.Width - 10) * ratio);
        var knobRect = new Rectangle(knobX, sliderRect.Y, 10, sliderRect.Height);

        sb.Draw(_pixel, trackRect, new Color(38, 56, 44));
        sb.Draw(_pixel, new Rectangle(trackRect.X, trackRect.Y, fillWidth, trackRect.Height), new Color(110, 210, 132));
        sb.Draw(_pixel, knobRect, new Color(214, 236, 216));
        sb.Draw(_pixel, new Rectangle(knobRect.X, knobRect.Y, knobRect.Width, 1), Color.White * 0.8f);
        sb.Draw(_pixel, new Rectangle(knobRect.X, knobRect.Bottom - 1, knobRect.Width, 1), Color.Black * 0.55f);
    }

    private static Rectangle GetSliderRect(Rectangle itemBounds)
        => new(itemBounds.X + 14, itemBounds.Bottom - 16, Math.Max(40, itemBounds.Width - 130), 10);

    private readonly record struct MenuItem(
        string Label,
        string? Value = null,
        string? Description = null,
        string? Tag = null,
        Action? Action = null,
        Rectangle Bounds = default,
        bool IsSlider = false,
        float SliderValue = 0f,
        float SliderMin = 0f,
        float SliderMax = 1f,
        float SliderStep = 0.05f,
        Action<float>? SliderChanged = null);
}
