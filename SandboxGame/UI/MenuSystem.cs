#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
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
    Settings
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

    public GameState GameState { get; set; } = GameState.MainMenu;
    public MenuScreen CurrentScreen { get; private set; } = MenuScreen.Main;

    public event Action? OnStartGame;
    public event Action? OnLoadRiver;
    public event Action? OnExitGame;
    public event Action? OnResumeGame;
    public event Action? OnReturnToMainMenu;

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
        _hoveredIndex = -1;
        _hoveredSettingsSectionIndex = -1;
        _hoveredSettingsContentIndex = -1;
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

    public void Draw(SpriteBatch sb)
    {
        if (CurrentScreen == MenuScreen.None)
            return;

        var vp = _gd.Viewport;
        var title = CurrentScreen switch
        {
            MenuScreen.Main => "SANDBOX RPG",
            MenuScreen.Pause => "PAUSE",
            MenuScreen.Settings => "SETTINGS",
            _ => string.Empty
        };

        var pageRect = new Rectangle(72, 72, vp.Width - 144, vp.Height - 144);
        var navWidth = Math.Min(320, (int)(pageRect.Width * 0.28f));
        var navRect = new Rectangle(pageRect.X + 24, pageRect.Y + 100, navWidth, pageRect.Height - 148);
        var contentRect = new Rectangle(
            navRect.Right + 20,
            navRect.Y,
            pageRect.Right - (navRect.Right + 44),
            navRect.Height);

        if (CurrentScreen == MenuScreen.Settings)
        {
            _lastSettingsNavRect = navRect;
            _lastSettingsContentRect = contentRect;
        }

        sb.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);

        DrawBackground(sb, vp.Bounds);
        DrawPanel(sb, pageRect, new Color(10, 18, 14, 228), new Color(84, 120, 92), 2);
        DrawPanel(sb, navRect, new Color(18, 28, 22, 240), new Color(72, 108, 80), 2);
        DrawPanel(sb, contentRect, new Color(16, 24, 20, 240), new Color(72, 108, 80), 2);

        var titleSize = _font.MeasureString(title);
        sb.DrawString(_font, title, new Vector2(pageRect.X + 24, pageRect.Y + 24), Color.White);
        sb.Draw(_pixel, new Rectangle(pageRect.X + 24, pageRect.Y + 24 + (int)titleSize.Y + 10, pageRect.Width - 48, 2), new Color(84, 120, 92));

        if (CurrentScreen == MenuScreen.Settings)
            DrawSettingsLayout(sb, navRect, contentRect);
        else
            DrawStandardLayout(sb, navRect, contentRect);

        var hint = GetHintText();
        var hintSize = _font.MeasureString(hint);
        sb.DrawString(_font, hint, new Vector2(pageRect.Right - hintSize.X - 24, pageRect.Bottom - 32), new Color(150, 170, 150));

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

        var hoverPos = new Point(_input.MousePosition.X, _input.MousePosition.Y);
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

        if (IsNewPress(kb, Keys.Escape) && CurrentScreen == MenuScreen.Pause)
        {
            CloseMenu();
            OnResumeGame?.Invoke();
        }
    }

    private void UpdateSettings(KeyboardState kb)
    {
        if (IsNewPress(kb, Keys.Left) || IsNewPress(kb, Keys.A))
            _settingsFocus = SettingsFocus.Sidebar;

        if (IsNewPress(kb, Keys.Right) || IsNewPress(kb, Keys.D))
            _settingsFocus = SettingsFocus.Content;

        var hoverPos = new Point(_input.MousePosition.X, _input.MousePosition.Y);
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

    private void DrawStandardLayout(SpriteBatch sb, Rectangle navRect, Rectangle contentRect)
    {
        var heading = CurrentScreen == MenuScreen.Main ? "Main Menu" : "Pause Menu";
        var description = _menuItems.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _menuItems.Count
            ? _menuItems[_selectedIndex].Description
            : "Select an option on the left.";

        DrawSectionHeader(sb, navRect, "Navigation", "All primary actions are listed here.");
        DrawMenuList(sb, _menuItems, navRect, _selectedIndex, _hoveredIndex, 66, 46, 8);

        DrawSectionHeader(sb, contentRect, heading, description ?? string.Empty);

        if (_menuItems.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _menuItems.Count)
        {
            var item = _menuItems[_selectedIndex];
            var card = new Rectangle(contentRect.X + 28, contentRect.Y + 98, contentRect.Width - 56, 116);
            DrawPanel(sb, card, new Color(22, 36, 28, 230), new Color(88, 132, 96), 2);
            DrawText(sb, item.Label, new Vector2(card.X + 20, card.Y + 18), Color.White);
            if (!string.IsNullOrWhiteSpace(item.Description))
                DrawMultilineText(sb, item.Description!, new Vector2(card.X + 20, card.Y + 52), card.Width - 40, new Color(190, 208, 192));
        }
    }

    private void DrawSettingsLayout(SpriteBatch sb, Rectangle navRect, Rectangle contentRect)
    {
        DrawSectionHeader(sb, navRect, "Settings", "Subsections stay visible here all the time.");
        DrawMenuList(sb, _settingsNavItems, navRect, _selectedSettingsSectionIndex, _hoveredSettingsSectionIndex, 66, 46, 8, _settingsFocus == SettingsFocus.Sidebar);

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

        DrawSectionHeader(sb, contentRect, heading, description);
        DrawMenuList(sb, _settingsContentItems, contentRect, _selectedSettingsContentIndex, _hoveredSettingsContentIndex, 98, 34, 6, _settingsFocus == SettingsFocus.Content, _settingsContentScroll);
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
                ? accentSelection ? new Color(56, 92, 66, 240) : new Color(40, 62, 48, 220)
                : isHovered ? new Color(36, 54, 42, 228)
                : new Color(25, 37, 30, 220);
            var border = isSelected ? new Color(118, 176, 128) : isHovered ? new Color(96, 138, 104) : new Color(62, 92, 70);
            var textColor = isSelected ? Color.White : new Color(196, 208, 198);

            DrawPanel(sb, bounds, bg, border, 1);
            if (isSelected)
                sb.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, 4, bounds.Height), new Color(180, 222, 128));

            DrawText(sb, item.Label, new Vector2(bounds.X + 14, bounds.Y + 9), textColor);
            if (!string.IsNullOrWhiteSpace(item.Value))
            {
                var safeValue = SanitizeText(item.Value!);
                var valueSize = _font.MeasureString(safeValue);
                var valueColor = _waitingForKey == item.Tag ? Color.Yellow : new Color(162, 214, 164);
                sb.DrawString(_font, safeValue, new Vector2(bounds.Right - valueSize.X - 14, bounds.Y + 9), valueColor);
            }

            y += itemHeight + gap;
        }

        if (items.Count > maxVisible)
            DrawScrollBar(sb, panelRect, startOffsetY, visibleHeight, startIndex, maxVisible, items.Count);
    }

    private void DrawSectionHeader(SpriteBatch sb, Rectangle panelRect, string title, string description)
    {
        DrawText(sb, title, new Vector2(panelRect.X + 18, panelRect.Y + 18), Color.White);
        if (!string.IsNullOrWhiteSpace(description))
            DrawMultilineText(sb, description, new Vector2(panelRect.X + 18, panelRect.Y + 46), panelRect.Width - 36, new Color(150, 172, 156));
    }

    private void DrawBackground(SpriteBatch sb, Rectangle bounds)
    {
        if (CurrentScreen == MenuScreen.Main)
        {
            sb.Draw(_mainMenuBackground, bounds, Color.Black);
            return;
        }

        sb.Draw(_pixel, bounds, new Color(4, 8, 6, 250));
        var topGlow = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height / 3);
        sb.Draw(_pixel, topGlow, new Color(22, 40, 28, 58));
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

        switch (CurrentScreen)
        {
            case MenuScreen.Main:
                _menuItems.Add(new MenuItem(
                    "Начать игру",
                    Description: "Base game start hook. Right now this option is a placeholder and does not launch a map yet.",
                    Action: () => OnStartGame?.Invoke()));
                if (_settings.DevMode)
                {
                    _menuItems.Add(new MenuItem(
                        "Река разработчика",
                        Description: "Loads the river developer map immediately for quick testing.",
                        Action: () => OnLoadRiver?.Invoke()));
                }
                _menuItems.Add(new MenuItem(
                    "Настройки",
                    Description: "Open gameplay options, developer mode and all current key bindings.",
                    Action: OpenSettings));
                _menuItems.Add(new MenuItem(
                    "Выход",
                    Description: "Close the game application.",
                    Action: () => OnExitGame?.Invoke()));
                break;

            case MenuScreen.Pause:
                _menuItems.Add(new MenuItem(
                    "Продолжить",
                    Description: "Return to the game without changing the current scene.",
                    Action: () =>
                    {
                        CloseMenu();
                        OnResumeGame?.Invoke();
                    }));
                _menuItems.Add(new MenuItem(
                    "Настройки",
                    Description: "Change settings without leaving the current session.",
                    Action: OpenSettings));
                _menuItems.Add(new MenuItem(
                    "Главное меню",
                    Description: "Leave the current play state and return to the main menu shell.",
                    Action: () =>
                    {
                        OpenMainMenu();
                        OnReturnToMainMenu?.Invoke();
                    }));
                _menuItems.Add(new MenuItem(
                    "Выход",
                    Description: "Quit the game.",
                    Action: () => OnExitGame?.Invoke()));
                break;
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

    private string GetHintText()
    {
        return CurrentScreen switch
        {
            MenuScreen.Main => "W/S move  Enter select",
            MenuScreen.Pause => "W/S move  Enter select  Esc resume",
            MenuScreen.Settings when _waitingForKey != null => "Press any key  Esc cancel",
            MenuScreen.Settings => "W/S move  A/D switch pane  Enter apply  Esc back",
            _ => string.Empty
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

    private readonly record struct MenuItem(
        string Label,
        string? Value = null,
        string? Description = null,
        string? Tag = null,
        Action? Action = null,
        Rectangle Bounds = default);
}
