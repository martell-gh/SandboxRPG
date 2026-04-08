using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEditor.UI;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.Tools;

public class TriggerZoneTool
{
    private MapData _map;
    private MapManager _mapManager;
    private Texture2D _pixel;
    private GraphicsDevice _graphics;
    private SpriteFont _font;

    private TriggerZoneData? _selectedTrigger;

    // UI state
    private bool _typingId;
    private bool _typingSpawnPoint;
    private string _inputId = "trigger_1";
    private string _inputTargetMap = "";
    private string _inputSpawnPoint = "default";
    private int _selectedActionTypeIndex;
    private KeyboardState _prevKeys;

    // для непрерывного рисования
    private Point _lastPaintedTile = new(-1, -1);

    // флаг — панель поглотила клик в этом кадре
    private bool _panelConsumedClick;

    // диалог выбора карты
    private MapSelectDialog _mapSelectDialog;

    public bool IsTyping => _typingId || _typingSpawnPoint;
    public bool IsDialogOpen => _mapSelectDialog.IsOpen;
    public bool IsInputBlocking => IsTyping || IsDialogOpen || _panelConsumedClick;

    private static readonly string[] ActionTypeLabels = { "Location Transition" };
    private static readonly string[] ActionTypeValues = { TriggerActionTypes.LocationTransition };

    private static readonly Color[] ZoneColors =
    {
        new(255, 100, 100), new(100, 255, 100), new(100, 100, 255),
        new(255, 255, 100), new(255, 100, 255), new(100, 255, 255),
        new(255, 180, 100), new(180, 100, 255), new(100, 255, 180),
    };

    private const int PanelWidth = 330;
    private const int LabelWidth = 116;
    private const int FieldWidth = 190;
    private const int LineH = 18;
    private const int Pad = 8;

    public TriggerZoneData? SelectedTrigger => _selectedTrigger;

    public TriggerZoneTool(MapData map, MapManager mapManager, GraphicsDevice graphics, SpriteFont font)
    {
        _map = map;
        _mapManager = mapManager;
        _graphics = graphics;
        _font = font;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _mapSelectDialog = new MapSelectDialog(font, graphics);
    }

    public void SetMap(MapData map)
    {
        _map = map;
        _selectedTrigger = null;
        _inputId = "trigger_1";
        _inputTargetMap = "";
        _inputSpawnPoint = "default";
    }

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        _panelConsumedClick = false;

        // обновляем диалог выбора карты
        _mapSelectDialog.Update(mouse, prev, keys, prevKeys);
        if (_mapSelectDialog.IsOpen)
        {
            _panelConsumedClick = true;
            _prevKeys = keys;
            return;
        }

        HandleTextInput(keys, mouse, prev);
        _prevKeys = keys;

        // панель поглощает клики
        if (GetPanelRect().Contains(mouse.X, mouse.Y))
            _panelConsumedClick = true;
    }

    /// <summary>
    /// Обработка рисования/стирания на карте. Вызывается только если курсор не в UI.
    /// </summary>
    public void UpdateWorldInput(MouseState mouse, MouseState prev, Vector2 worldPos, PointerTool pointerTool)
    {
        if (_panelConsumedClick || _mapSelectDialog.IsOpen) return;

        var tileX = (int)(worldPos.X / _map.TileSize);
        var tileY = (int)(worldPos.Y / _map.TileSize);
        if (tileX < 0 || tileX >= _map.Width || tileY < 0 || tileY >= _map.Height)
            return;

        var currentTile = new Point(tileX, tileY);

        if (pointerTool == PointerTool.Mouse)
        {
            // ЛКМ в mouse mode сначала выбирает существующий триггер под курсором.
            // Если под курсором пусто, но триггер уже выбран, клик расширяет его.
            if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
            {
                var found = _map.Triggers.FirstOrDefault(t => t.ContainsTile(tileX, tileY));
                if (found != null)
                {
                    _selectedTrigger = found;
                    SyncUIFromTrigger(found);
                    return;
                }

                if (_selectedTrigger != null)
                {
                    _selectedTrigger.AddTile(tileX, tileY);
                    SyncUIFromTrigger(_selectedTrigger);
                }
            }

            // ПКМ в mouse mode удаляет тайл только у выбранного триггера.
            if (mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released && _selectedTrigger != null)
            {
                _selectedTrigger.RemoveTile(tileX, tileY);
                if (_selectedTrigger.Tiles.Count == 0)
                {
                    _map.Triggers.Remove(_selectedTrigger);
                    _selectedTrigger = null;
                }
            }

            // Средняя кнопка оставляем как быстрый pick, если удобно.
            if (mouse.MiddleButton == ButtonState.Pressed && prev.MiddleButton == ButtonState.Released)
            {
                var found = _map.Triggers.FirstOrDefault(t => t.ContainsTile(tileX, tileY));
                if (found != null)
                {
                    _selectedTrigger = found;
                    SyncUIFromTrigger(found);
                }
            }

            return;
        }

        // ЛКМ — рисуем тайлы (непрерывно при зажатии) в brush mode
        if (mouse.LeftButton == ButtonState.Pressed)
        {
            if (string.IsNullOrWhiteSpace(_inputId)) return;

            if (currentTile == _lastPaintedTile)
                return;

            var trigger = _map.Triggers.FirstOrDefault(t => t.Id == _inputId);
            if (trigger == null)
            {
                trigger = new TriggerZoneData
                {
                    Id = _inputId,
                    Action = CreateActionFromUI()
                };
                _map.Triggers.Add(trigger);
            }

            trigger.AddTile(tileX, tileY);
            _selectedTrigger = trigger;
            SyncUIFromTrigger(trigger);
            _lastPaintedTile = currentTile;
        }
        else if (mouse.LeftButton == ButtonState.Released)
        {
            _lastPaintedTile = new Point(-1, -1);
        }

        // ПКМ — стираем тайлы (непрерывно при зажатии) в brush mode
        if (mouse.RightButton == ButtonState.Pressed)
        {
            var any = false;
            foreach (var trigger in _map.Triggers)
            {
                if (trigger.ContainsTile(tileX, tileY))
                {
                    trigger.RemoveTile(tileX, tileY);
                    any = true;
                }
            }

            if (any)
            {
                _map.Triggers.RemoveAll(t => t.Tiles.Count == 0);
                if (_selectedTrigger != null && _selectedTrigger.Tiles.Count == 0)
                    _selectedTrigger = null;
            }
        }

        // Средняя кнопка — выбрать триггер под курсором
        if (mouse.MiddleButton == ButtonState.Pressed && prev.MiddleButton == ButtonState.Released)
        {
            var found = _map.Triggers.FirstOrDefault(t => t.ContainsTile(tileX, tileY));
            if (found != null)
            {
                _selectedTrigger = found;
                SyncUIFromTrigger(found);
            }
        }
    }

    public void DeleteSelectedTrigger()
    {
        if (_selectedTrigger == null) return;
        _map.Triggers.Remove(_selectedTrigger);
        _selectedTrigger = null;
    }

    public void ApplyUIToSelected()
    {
        if (_selectedTrigger == null) return;
        _selectedTrigger.Id = _inputId;
        _selectedTrigger.Action = CreateActionFromUI();
    }

    private TriggerActionData CreateActionFromUI()
    {
        var action = new TriggerActionData
        {
            Type = ActionTypeValues[_selectedActionTypeIndex]
        };

        if (action.Type == TriggerActionTypes.LocationTransition)
        {
            action.TargetMapId = string.IsNullOrWhiteSpace(_inputTargetMap) ? null : _inputTargetMap;
            action.SpawnPointId = string.IsNullOrWhiteSpace(_inputSpawnPoint) ? "default" : _inputSpawnPoint;
        }

        return action;
    }

    private void SyncUIFromTrigger(TriggerZoneData trigger)
    {
        _inputId = trigger.Id;
        var typeIdx = Array.IndexOf(ActionTypeValues, trigger.Action.Type);
        _selectedActionTypeIndex = typeIdx >= 0 ? typeIdx : 0;

        if (trigger.Action.Type == TriggerActionTypes.LocationTransition)
        {
            _inputTargetMap = trigger.Action.TargetMapId ?? "";
            _inputSpawnPoint = trigger.Action.SpawnPointId ?? "default";
        }
    }

    private void OpenMapSelectDialog()
    {
        var maps = _mapManager.GetAvailableMaps();
        _mapSelectDialog.Open(maps, mapId =>
        {
            _inputTargetMap = mapId;
            ApplyUIToSelected();
        });
    }

    // === Drawing ===

    public void Draw(SpriteBatch spriteBatch, AssetManager assets, SpriteFont font)
    {
        foreach (var trigger in _map.Triggers)
        {
            var color = GetTriggerColor(trigger.Id);
            var isSelected = trigger == _selectedTrigger;
            var alpha = isSelected ? 0.5f : 0.3f;
            var ts = _map.TileSize;

            // собираем HashSet для быстрой проверки соседей
            var tileSet = new HashSet<(int x, int y)>();
            foreach (var t in trigger.Tiles)
                tileSet.Add((t.X, t.Y));

            // заливка — сплошная без внутренних рамок
            foreach (var tile in trigger.Tiles)
            {
                var rect = new Rectangle(tile.X * ts, tile.Y * ts, ts, ts);
                spriteBatch.Draw(_pixel, rect, color * alpha);
            }

            // рамка — только внешние грани (где нет соседа)
            var borderColor = isSelected ? color * 0.95f : color * 0.7f;
            var borderWidth = isSelected ? 2 : 1;
            foreach (var tile in trigger.Tiles)
            {
                var px = tile.X * ts;
                var py = tile.Y * ts;

                if (!tileSet.Contains((tile.X, tile.Y - 1))) // верх
                    spriteBatch.Draw(_pixel, new Rectangle(px, py, ts, borderWidth), borderColor);
                if (!tileSet.Contains((tile.X, tile.Y + 1))) // низ
                    spriteBatch.Draw(_pixel, new Rectangle(px, py + ts - borderWidth, ts, borderWidth), borderColor);
                if (!tileSet.Contains((tile.X - 1, tile.Y))) // лево
                    spriteBatch.Draw(_pixel, new Rectangle(px, py, borderWidth, ts), borderColor);
                if (!tileSet.Contains((tile.X + 1, tile.Y))) // право
                    spriteBatch.Draw(_pixel, new Rectangle(px + ts - borderWidth, py, borderWidth, ts), borderColor);
            }

            // подпись
            if (trigger.Tiles.Count > 0)
            {
                var minX = trigger.Tiles.Min(t => t.X);
                var minY = trigger.Tiles.Min(t => t.Y);
                var labelPos = new Vector2(minX * ts + 2, minY * ts - 14);
                spriteBatch.DrawString(font, trigger.Id, labelPos, color);
            }
        }
    }

    public void DrawUI(SpriteBatch spriteBatch, SpriteFont font)
    {
        var panel = GetPanelRect();
        spriteBatch.Draw(_pixel, panel, Color.Black * 0.88f);

        var y = panel.Y + Pad;
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;

        // Title
        spriteBatch.DrawString(font, "TRIGGER ZONE TOOL [4]", new Vector2(x, y), Color.LimeGreen);
        y += LineH + 6;

        // Trigger ID
        DrawLabel(spriteBatch, font, "Trigger ID:", x, y);
        var idRect = new Rectangle(fieldX, y, FieldWidth, LineH);
        DrawInputField(spriteBatch, font, idRect, _inputId, _typingId);
        y += LineH + 8;

        // Action Type
        DrawLabel(spriteBatch, font, "Action:", x, y);
        spriteBatch.DrawString(font, $"[/]  {ActionTypeLabels[_selectedActionTypeIndex]}", new Vector2(fieldX, y), Color.Cyan);
        y += LineH + 8;

        // Location Transition params
        if (ActionTypeValues[_selectedActionTypeIndex] == TriggerActionTypes.LocationTransition)
        {
            // Target Map — кнопка + текущее значение
            DrawLabel(spriteBatch, font, "Target Map:", x, y);
            var btnRect = _selectMapButtonRect = new Rectangle(fieldX, y, FieldWidth, LineH);
            var hasMap = !string.IsNullOrWhiteSpace(_inputTargetMap);
            spriteBatch.Draw(_pixel, btnRect, hasMap ? Color.DarkGreen * 0.6f : Color.Gray * 0.4f);
            var mapText = hasMap ? _inputTargetMap : "-- select map --";
            spriteBatch.DrawString(font, mapText, new Vector2(btnRect.X + 4, btnRect.Y + 1), hasMap ? Color.White : Color.Gray);
            y += LineH + 8;

            // Spawn Point
            DrawLabel(spriteBatch, font, "Spawn Point:", x, y);
            var spawnRect = new Rectangle(fieldX, y, FieldWidth, LineH);
            DrawInputField(spriteBatch, font, spawnRect, _inputSpawnPoint, _typingSpawnPoint);
            y += LineH + 8;
        }

        // help
        var modeHelp = "Brush: LClick=paint  RClick=erase";
        spriteBatch.DrawString(font, modeHelp, new Vector2(x, y), Color.Gray);
        y += LineH;
        spriteBatch.DrawString(font, "Mouse: LClick=select/add  RClick=trim  MClick=pick", new Vector2(x, y), Color.Gray);
        y += LineH + 2;

        if (_selectedTrigger != null)
        {
            spriteBatch.DrawString(font, $"Selected: {_selectedTrigger.Id} ({_selectedTrigger.Tiles.Count} tiles)", new Vector2(x, y), Color.Yellow);
            y += LineH;
            spriteBatch.DrawString(font, "Enter=apply  Delete=remove trigger", new Vector2(x, y), Color.Gray);
            y += LineH;
        }

        spriteBatch.DrawString(font, $"Triggers: {_map.Triggers.Count}", new Vector2(x, y), Color.Cyan);

        // диалог выбора карты поверх всего
        _mapSelectDialog.Draw(spriteBatch);
    }

    private Rectangle _selectMapButtonRect;

    // === Text Input ===

    private void HandleTextInput(KeyboardState keys, MouseState mouse, MouseState prev)
    {
        var panel = GetPanelRect();
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;
        var y = panel.Y + Pad + LineH + 6; // после заголовка

        // ID field
        var idRect = new Rectangle(fieldX, y, FieldWidth, LineH);
        y += LineH + 8 + LineH + 8; // skip action type row

        // spawn field (if location transition)
        Rectangle spawnRect = Rectangle.Empty;
        if (ActionTypeValues[_selectedActionTypeIndex] == TriggerActionTypes.LocationTransition)
        {
            y += LineH + 8; // skip target map row
            spawnRect = new Rectangle(fieldX, y, FieldWidth, LineH);
        }

        // клик
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            var mp = mouse.Position;

            var clickedId = idRect.Contains(mp);
            var clickedSpawn = spawnRect != Rectangle.Empty && spawnRect.Contains(mp);

            _typingId = clickedId;
            _typingSpawnPoint = clickedSpawn;

            // клик по кнопке выбора карты
            if (_selectMapButtonRect.Contains(mp))
            {
                OpenMapSelectDialog();
                _typingId = false;
                _typingSpawnPoint = false;
            }
        }

        // Escape — снять фокус
        if (IsPressed(keys, Keys.Escape))
        {
            _typingId = false;
            _typingSpawnPoint = false;
        }

        // Enter — применить
        if (IsPressed(keys, Keys.Enter))
        {
            ApplyUIToSelected();
            _typingId = false;
            _typingSpawnPoint = false;
        }

        // Delete (только если не печатаем)
        if (!IsTyping && IsPressed(keys, Keys.Delete))
            DeleteSelectedTrigger();

        // [ ] — переключение типа действия
        if (!IsTyping)
        {
            if (IsPressed(keys, Keys.OemOpenBrackets))
                _selectedActionTypeIndex = Math.Max(0, _selectedActionTypeIndex - 1);
            if (IsPressed(keys, Keys.OemCloseBrackets))
                _selectedActionTypeIndex = Math.Min(ActionTypeValues.Length - 1, _selectedActionTypeIndex + 1);
        }

        // ввод текста
        if (_typingId) _inputId = ProcessTyping(keys, _inputId);
        else if (_typingSpawnPoint) _inputSpawnPoint = ProcessTyping(keys, _inputSpawnPoint);
    }

    private string ProcessTyping(KeyboardState keys, string current)
    {
        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key)) continue;

            if (key == Keys.Back && current.Length > 0)
                return current[..^1];

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift));
            if (ch != '\0')
                return current + ch;
        }
        return current;
    }

    private bool IsPressed(KeyboardState keys, Keys key)
        => keys.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    // === Helpers ===

    private Rectangle GetPanelRect()
    {
        var viewport = _graphics.Viewport;
        var h = CalculatePanelHeight();
        return new Rectangle(viewport.Width - PanelWidth - 10, viewport.Height - h - 44, PanelWidth, h);
    }

    private int CalculatePanelHeight()
    {
        var h = Pad + LineH + 6; // title
        h += LineH + 8; // trigger ID
        h += LineH + 8; // action type
        if (ActionTypeValues[_selectedActionTypeIndex] == TriggerActionTypes.LocationTransition)
        {
            h += LineH + 8; // target map
            h += LineH + 8; // spawn point
        }
        h += LineH * 2 + 2; // help lines
        if (_selectedTrigger != null)
            h += LineH * 2;
        h += LineH + Pad; // triggers count + bottom padding
        return h;
    }

    private static Color GetTriggerColor(string id)
    {
        var hash = Math.Abs(id.GetHashCode());
        return ZoneColors[hash % ZoneColors.Length];
    }

    private void DrawLabel(SpriteBatch spriteBatch, SpriteFont font, string text, int x, int y)
    {
        spriteBatch.DrawString(font, text, new Vector2(x, y), Color.White);
    }

    private void DrawInputField(SpriteBatch spriteBatch, SpriteFont font, Rectangle rect, string text, bool active)
    {
        spriteBatch.Draw(_pixel, rect, active ? Color.DarkGreen * 0.8f : Color.Gray * 0.4f);
        spriteBatch.DrawString(font, text + (active ? "_" : ""), new Vector2(rect.X + 4, rect.Y + 1), Color.White);
    }

    private void DrawTileBorder(SpriteBatch spriteBatch, Rectangle rect, Color color)
    {
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        spriteBatch.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.OemMinus => '_',
            Keys.Space => '_',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }
}
