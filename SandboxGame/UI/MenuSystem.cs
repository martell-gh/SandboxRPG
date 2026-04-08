#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
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
    Confirm
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
    private Action? _confirmAccept;
    private Action? _confirmCancel;

    private const float MinUiScale = 0.75f;
    private const float MaxUiScale = 2f;
    private const float UiScaleStep = 0.05f;

    public GameState GameState { get; set; } = GameState.MainMenu;
    public MenuScreen CurrentScreen { get; private set; } = MenuScreen.Main;
    public Func<IReadOnlyList<SaveSlotSummary>>? SaveSlotProvider { get; set; }
    public Func<int, string>? SaveNameSuggestionProvider { get; set; }

    public event Action? OnStartGame;
    public event Action? OnLoadRiver;
    public event Action? OnExitGame;
    public event Action? OnResumeGame;
    public event Action? OnReturnToMainMenu;
    public event Action<int, string>? OnSaveSlotConfirmed;
    public event Action<int, string>? OnRenameSlotRequested;
    public event Action<int>? OnDeleteSlotRequested;
    public event Action<int>? OnLoadSlotSelected;

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
        _actionMap["start_game"] = () => OnStartGame?.Invoke();
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
            samplerState: Math.Abs(uiScale - 1f) > 0.01f ? SamplerState.LinearClamp : SamplerState.PointClamp,
            transformMatrix: Matrix.CreateScale(uiScale, uiScale, 1f));

        DrawBackground(sb, vp, def.Background);
        DrawPanel(sb, pageRect, def.PagePanel.Fill, def.PagePanel.Border, def.PagePanel.BorderThickness);
        DrawPanel(sb, navRect, def.NavPanel.Fill, def.NavPanel.Border, def.NavPanel.BorderThickness);
        DrawPanel(sb, contentRect, def.ContentPanel.Fill, def.ContentPanel.Border, def.ContentPanel.BorderThickness);

        var tb = def.TitleBar;
        var titleSafe = SanitizeText(title);
        var titleSize = _font.MeasureString(titleSafe);
        sb.DrawString(_font, titleSafe, new Vector2(pageRect.X + tb.OffsetX, pageRect.Y + tb.OffsetY), tb.Color);
        sb.Draw(_pixel, new Rectangle(pageRect.X + tb.OffsetX, pageRect.Y + tb.OffsetY + (int)titleSize.Y + tb.SeparatorGap, pageRect.Width - tb.OffsetX * 2, tb.SeparatorHeight), tb.SeparatorColor);

        if (CurrentScreen == MenuScreen.Settings)
            DrawSettingsLayout(sb, navRect, contentRect, def);
        else
            DrawStandardLayout(sb, navRect, contentRect, def);

        var hintDef = def.Hint;
        var hint = GetScreenHintText(hintDef);
        if (!string.IsNullOrEmpty(hint))
        {
            var hintSafe = SanitizeText(hint);
            var hintSize = _font.MeasureString(hintSafe);
            var hintPos = ResolveHintPosition(pageRect, hintDef, hintSize);
            sb.DrawString(_font, hintSafe, hintPos, hintDef.Color);
        }

        sb.End();
    }

    private void DrawModalConfirmation(SpriteBatch sb, Rectangle viewportRect, MenuDefinition def, float uiScale)
    {
        sb.Begin(
            blendState: BlendState.AlphaBlend,
            samplerState: Math.Abs(uiScale - 1f) > 0.01f ? SamplerState.LinearClamp : SamplerState.PointClamp,
            transformMatrix: Matrix.CreateScale(uiScale, uiScale, 1f));

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
        else if (IsNewPress(kb, Keys.Escape) && (CurrentScreen == MenuScreen.SaveSlots || CurrentScreen == MenuScreen.LoadSlots))
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
                var safeValue = SanitizeText(item.Value!);
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
        sb.DrawString(_font, SanitizeText(text), position, color);
    }

    private void DrawMultilineText(SpriteBatch sb, string text, Vector2 position, int maxWidth, Color color)
    {
        var words = SanitizeText(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = string.Empty;
        var y = position.Y;

        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
            if (_font.MeasureString(candidate).X > maxWidth && !string.IsNullOrEmpty(line))
            {
                sb.DrawString(_font, line, new Vector2(position.X, y), color);
                line = word;
                y += 20;
            }
            else
            {
                line = candidate;
            }
        }

        if (!string.IsNullOrEmpty(line))
            sb.DrawString(_font, line, new Vector2(position.X, y), color);
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
                _settingsContentItems.Add(new MenuItem(
                    "Режим разработчика",
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
            MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.SaveNamePrompt or MenuScreen.Confirm => _overlayTitle,
            _ => def.Title
        };
    }

    private string GetContentHeading(MenuDefinition def)
    {
        return CurrentScreen switch
        {
            MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.SaveNamePrompt or MenuScreen.Confirm => _overlayTitle,
            _ => def.Title
        };
    }

    private string GetContentDescription(string? selectedDescription)
    {
        if (CurrentScreen is MenuScreen.SaveSlots or MenuScreen.LoadSlots or MenuScreen.SaveNamePrompt or MenuScreen.Confirm)
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
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('−', '-')
            .Replace('←', '<')
            .Replace('→', '>')
            .Replace('↑', '^')
            .Replace('↓', 'v')
            .Replace('Ё', 'Е')
            .Replace('ё', 'е');
    }

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
    {
        var scale = GetUiScale();
        return new Rectangle(
            0,
            0,
            Math.Max(1, (int)MathF.Round(_gd.Viewport.Width / scale)),
            Math.Max(1, (int)MathF.Round(_gd.Viewport.Height / scale)));
    }

    private Point GetScaledMousePosition()
    {
        var scale = GetUiScale();
        return new Point(
            (int)MathF.Round(_input.MousePosition.X / scale),
            (int)MathF.Round(_input.MousePosition.Y / scale));
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
