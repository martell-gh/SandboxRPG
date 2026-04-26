using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.Tools;

/// <summary>
/// Редактор area-zones. Поведение похоже на TriggerZoneTool:
///   ЛКМ (brush)  — рисует тайлы выбранной area
///   ПКМ (brush)  — стирает тайлы под курсором
///   ЛКМ (mouse)  — выбирает area под курсором / расширяет выбранную
///   ПКМ (mouse)  — убирает тайл из выбранной area
///   Shift+ЛКМ    — поставить именованную точку (auto-id, либо текущий "point id")
///   Shift+ПКМ    — удалить ближайшую точку выбранной area
///   [ / ]        — переключить Kind
///   Tab          — фокус на ID / property field / point id field
/// </summary>
public class AreaZoneTool
{
    private MapData _map;
    private readonly Texture2D _pixel;
    private readonly GraphicsDevice _graphics;
    private readonly SpriteFont _font;

    private AreaZoneData? _selected;

    private bool _typingId;
    private bool _typingPropKey;
    private bool _typingPropValue;
    private bool _typingPointId;
    private string _inputId = "area_1";
    private string _inputPropKey = "professionId";
    private string _inputPropValue = "";
    private string _inputPointId = "bed_slot_a";
    private int _kindIndex;
    private KeyboardState _prevKeys;

    private Point _lastPaintedTile = new(-1, -1);
    private bool _panelConsumedClick;

    private static readonly string[] KindValues =
    {
        AreaZoneKinds.House, AreaZoneKinds.Profession, AreaZoneKinds.School,
        AreaZoneKinds.Inn, AreaZoneKinds.Tavern, AreaZoneKinds.Orphanage,
        AreaZoneKinds.Wander, AreaZoneKinds.District, AreaZoneKinds.Settlement
    };

    private static readonly Color[] KindColors =
    {
        new(120, 180, 255),  // house
        new(255, 180, 80),   // profession
        new(180, 255, 120),  // school
        new(220, 220, 120),  // inn
        new(220, 140, 80),   // tavern
        new(160, 200, 220),  // orphanage
        new(180, 180, 180),  // wander
        new(80, 200, 200),   // district
        new(255, 100, 220)   // settlement
    };

    private const int PanelWidth = 360;
    private const int LabelWidth = 116;
    private const int FieldWidth = 220;
    private const int LineH = 18;
    private const int Pad = 8;

    public bool IsTyping => _typingId || _typingPropKey || _typingPropValue || _typingPointId;
    public bool IsInputBlocking => IsTyping || _panelConsumedClick;
    public AreaZoneData? Selected => _selected;

    public AreaZoneTool(MapData map, GraphicsDevice graphics, SpriteFont font)
    {
        _map = map;
        _graphics = graphics;
        _font = font;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void SetMap(MapData map)
    {
        _map = map;
        _selected = null;
        _inputId = "area_1";
        _inputPropValue = "";
    }

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        _panelConsumedClick = false;
        HandleTextInput(keys, mouse, prev);
        _prevKeys = keys;
        if (GetPanelRect().Contains(mouse.X, mouse.Y))
            _panelConsumedClick = true;
    }

    public void UpdateWorldInput(MouseState mouse, MouseState prev, Vector2 worldPos, PointerTool pointerTool, KeyboardState keys)
    {
        if (_panelConsumedClick) return;
        var tileX = (int)(worldPos.X / _map.TileSize);
        var tileY = (int)(worldPos.Y / _map.TileSize);
        if (tileX < 0 || tileX >= _map.Width || tileY < 0 || tileY >= _map.Height)
            return;

        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);

        // Shift+LMB — добавить named point в выбранную area
        if (shift && mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (_selected == null) return;
            var pid = string.IsNullOrWhiteSpace(_inputPointId) ? $"point_{_selected.Points.Count + 1}" : _inputPointId;
            _selected.Points.RemoveAll(p => p.Id == pid);
            _selected.Points.Add(new AreaPointData { Id = pid, X = tileX, Y = tileY });
            return;
        }
        // Shift+RMB — удалить ближайшую точку выбранной area в этом тайле
        if (shift && mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released)
        {
            _selected?.Points.RemoveAll(p => p.X == tileX && p.Y == tileY);
            return;
        }

        if (pointerTool == PointerTool.Mouse)
        {
            if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
            {
                var found = _map.Areas.FirstOrDefault(a => a.ContainsTile(tileX, tileY));
                if (found != null) { _selected = found; SyncUIFromArea(found); return; }
                _selected?.AddTile(tileX, tileY);
            }
            if (mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released && _selected != null)
            {
                _selected.RemoveTile(tileX, tileY);
                if (_selected.Tiles.Count == 0) { _map.Areas.Remove(_selected); _selected = null; }
            }
            return;
        }

        // brush
        var current = new Point(tileX, tileY);
        if (mouse.LeftButton == ButtonState.Pressed)
        {
            if (string.IsNullOrWhiteSpace(_inputId)) return;
            if (current == _lastPaintedTile) return;
            var area = _map.Areas.FirstOrDefault(a => a.Id == _inputId);
            if (area == null)
            {
                area = new AreaZoneData { Id = _inputId, Kind = KindValues[_kindIndex] };
                _map.Areas.Add(area);
            }
            else
            {
                area.Kind = KindValues[_kindIndex];
            }
            area.AddTile(tileX, tileY);
            _selected = area;
            _lastPaintedTile = current;
        }
        else if (mouse.LeftButton == ButtonState.Released)
        {
            _lastPaintedTile = new Point(-1, -1);
        }

        if (mouse.RightButton == ButtonState.Pressed)
        {
            var changed = false;
            foreach (var area in _map.Areas)
                if (area.ContainsTile(tileX, tileY)) { area.RemoveTile(tileX, tileY); changed = true; }
            if (changed)
            {
                _map.Areas.RemoveAll(a => a.Tiles.Count == 0);
                if (_selected != null && _selected.Tiles.Count == 0) _selected = null;
            }
        }
    }

    public void DeleteSelected()
    {
        if (_selected == null) return;
        _map.Areas.Remove(_selected);
        _selected = null;
    }

    public void ApplyUIToSelected()
    {
        if (_selected == null) return;
        _selected.Id = _inputId;
        _selected.Kind = KindValues[_kindIndex];
        if (!string.IsNullOrWhiteSpace(_inputPropKey) && !string.IsNullOrWhiteSpace(_inputPropValue))
            _selected.Properties[_inputPropKey] = _inputPropValue;
    }

    private void SyncUIFromArea(AreaZoneData a)
    {
        _inputId = a.Id;
        var idx = Array.IndexOf(KindValues, a.Kind);
        _kindIndex = idx >= 0 ? idx : 0;
        if (a.Properties.TryGetValue(_inputPropKey, out var v))
            _inputPropValue = v;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        var ts = _map.TileSize;
        foreach (var area in _map.Areas)
        {
            var color = ResolveColor(area.Kind);
            var isSelected = area == _selected;
            var alpha = isSelected ? 0.5f : 0.28f;
            var tileSet = new HashSet<(int x, int y)>();
            foreach (var t in area.Tiles) tileSet.Add((t.X, t.Y));

            foreach (var tile in area.Tiles)
                spriteBatch.Draw(_pixel, new Rectangle(tile.X * ts, tile.Y * ts, ts, ts), color * alpha);

            var border = isSelected ? color * 0.95f : color * 0.7f;
            var bw = isSelected ? 2 : 1;
            foreach (var tile in area.Tiles)
            {
                var px = tile.X * ts; var py = tile.Y * ts;
                if (!tileSet.Contains((tile.X, tile.Y - 1))) spriteBatch.Draw(_pixel, new Rectangle(px, py, ts, bw), border);
                if (!tileSet.Contains((tile.X, tile.Y + 1))) spriteBatch.Draw(_pixel, new Rectangle(px, py + ts - bw, ts, bw), border);
                if (!tileSet.Contains((tile.X - 1, tile.Y))) spriteBatch.Draw(_pixel, new Rectangle(px, py, bw, ts), border);
                if (!tileSet.Contains((tile.X + 1, tile.Y))) spriteBatch.Draw(_pixel, new Rectangle(px + ts - bw, py, bw, ts), border);
            }

            if (area.Tiles.Count > 0)
            {
                var minX = area.Tiles.Min(t => t.X);
                var minY = area.Tiles.Min(t => t.Y);
                spriteBatch.DrawString(_font, $"{area.Kind}: {area.Id}", new Vector2(minX * ts + 2, minY * ts - 14), color);
            }

            // points
            foreach (var p in area.Points)
            {
                var rx = p.X * ts; var ry = p.Y * ts;
                spriteBatch.Draw(_pixel, new Rectangle(rx + ts / 2 - 4, ry + ts / 2 - 4, 8, 8), Color.Yellow);
                spriteBatch.DrawString(_font, p.Id, new Vector2(rx + ts / 2 + 6, ry + ts / 2 - 8), Color.Yellow);
            }
        }
    }

    public void DrawUI(SpriteBatch spriteBatch)
    {
        var panel = GetPanelRect();
        spriteBatch.Draw(_pixel, panel, Color.Black * 0.88f);

        var y = panel.Y + Pad;
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;

        spriteBatch.DrawString(_font, "AREA ZONE TOOL [5]", new Vector2(x, y), Color.SkyBlue); y += LineH + 6;

        DrawLabel(spriteBatch, "Area ID:", x, y);
        DrawInputField(spriteBatch, new Rectangle(fieldX, y, FieldWidth, LineH), _inputId, _typingId);
        y += LineH + 6;

        DrawLabel(spriteBatch, "Kind:", x, y);
        spriteBatch.DrawString(_font, $"[/] {KindValues[_kindIndex]}", new Vector2(fieldX, y), Color.Cyan);
        y += LineH + 6;

        DrawLabel(spriteBatch, "Prop key:", x, y);
        DrawInputField(spriteBatch, new Rectangle(fieldX, y, FieldWidth, LineH), _inputPropKey, _typingPropKey);
        y += LineH + 6;

        DrawLabel(spriteBatch, "Prop value:", x, y);
        DrawInputField(spriteBatch, new Rectangle(fieldX, y, FieldWidth, LineH), _inputPropValue, _typingPropValue);
        y += LineH + 6;

        DrawLabel(spriteBatch, "Point id:", x, y);
        DrawInputField(spriteBatch, new Rectangle(fieldX, y, FieldWidth, LineH), _inputPointId, _typingPointId);
        y += LineH + 6;

        spriteBatch.DrawString(_font, "Brush: LClick=paint  RClick=erase", new Vector2(x, y), Color.Gray); y += LineH;
        spriteBatch.DrawString(_font, "Mouse: LClick=select/add  RClick=trim", new Vector2(x, y), Color.Gray); y += LineH;
        spriteBatch.DrawString(_font, "Shift+LMB add point  Shift+RMB del point", new Vector2(x, y), Color.Gray); y += LineH + 2;

        if (_selected != null)
        {
            spriteBatch.DrawString(_font, $"Selected: {_selected.Kind}/{_selected.Id} ({_selected.Tiles.Count}t, {_selected.Points.Count}p)", new Vector2(x, y), Color.Yellow);
            y += LineH;
            spriteBatch.DrawString(_font, "Enter=apply  Delete=remove area", new Vector2(x, y), Color.Gray);
            y += LineH;
            if (_selected.Properties.Count > 0)
            {
                var props = string.Join(", ", _selected.Properties.Select(p => $"{p.Key}={p.Value}"));
                spriteBatch.DrawString(_font, $"Props: {props}", new Vector2(x, y), Color.LightGreen);
                y += LineH;
            }
        }

        spriteBatch.DrawString(_font, $"Areas: {_map.Areas.Count}", new Vector2(x, y), Color.Cyan);
    }

    private void HandleTextInput(KeyboardState keys, MouseState mouse, MouseState prev)
    {
        var panel = GetPanelRect();
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;
        var y = panel.Y + Pad + LineH + 6;
        var idRect = new Rectangle(fieldX, y, FieldWidth, LineH); y += LineH + 6;
        y += LineH + 6; // kind row
        var propKeyRect = new Rectangle(fieldX, y, FieldWidth, LineH); y += LineH + 6;
        var propValRect = new Rectangle(fieldX, y, FieldWidth, LineH); y += LineH + 6;
        var pointIdRect = new Rectangle(fieldX, y, FieldWidth, LineH);

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            var mp = mouse.Position;
            _typingId = idRect.Contains(mp);
            _typingPropKey = propKeyRect.Contains(mp);
            _typingPropValue = propValRect.Contains(mp);
            _typingPointId = pointIdRect.Contains(mp);
        }

        if (IsPressed(keys, Keys.Escape))
        { _typingId = _typingPropKey = _typingPropValue = _typingPointId = false; }

        if (IsPressed(keys, Keys.Enter))
        {
            ApplyUIToSelected();
            _typingId = _typingPropKey = _typingPropValue = _typingPointId = false;
        }

        if (!IsTyping && IsPressed(keys, Keys.Delete)) DeleteSelected();

        if (!IsTyping)
        {
            if (IsPressed(keys, Keys.OemOpenBrackets))
                _kindIndex = (_kindIndex - 1 + KindValues.Length) % KindValues.Length;
            if (IsPressed(keys, Keys.OemCloseBrackets))
                _kindIndex = (_kindIndex + 1) % KindValues.Length;
        }

        if (_typingId) _inputId = ProcessTyping(keys, _inputId);
        else if (_typingPropKey) _inputPropKey = ProcessTyping(keys, _inputPropKey);
        else if (_typingPropValue) _inputPropValue = ProcessTyping(keys, _inputPropValue);
        else if (_typingPointId) _inputPointId = ProcessTyping(keys, _inputPointId);
    }

    private string ProcessTyping(KeyboardState keys, string current)
    {
        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key)) continue;
            if (key == Keys.Back && current.Length > 0) return current[..^1];
            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift));
            if (ch != '\0') return current + ch;
        }
        return current;
    }

    private bool IsPressed(KeyboardState keys, Keys k) => keys.IsKeyDown(k) && _prevKeys.IsKeyUp(k);

    private Rectangle GetPanelRect()
    {
        var vp = _graphics.Viewport;
        var h = Pad + (LineH + 6) * 6 + LineH * 5 + Pad;
        return new Rectangle(vp.Width - PanelWidth - 10, vp.Height - h - 44, PanelWidth, h);
    }

    private Color ResolveColor(string kind)
    {
        var idx = Array.IndexOf(KindValues, kind);
        return idx >= 0 ? KindColors[idx] : Color.Magenta;
    }

    private void DrawLabel(SpriteBatch sb, string text, int x, int y)
        => sb.DrawString(_font, text, new Vector2(x, y), Color.White);

    private void DrawInputField(SpriteBatch sb, Rectangle rect, string text, bool active)
    {
        sb.Draw(_pixel, rect, active ? Color.DarkSlateBlue * 0.85f : Color.Gray * 0.4f);
        sb.DrawString(_font, text + (active ? "_" : ""), new Vector2(rect.X + 4, rect.Y + 1), Color.White);
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z) return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9) return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.OemMinus => '_',
            Keys.Space => '_',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }
}
