using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEditor.UI;
using MTEngine.Npc;
using MTEngine.World;

namespace MTEditor.Tools;

public class AreaZoneTool
{
    private enum AreaUiFieldKind
    {
        None,
        Profession,
        Name,
        Settlement,
        District,
        Faction,
        RoomPrice,
        BedPrice
    }

    private readonly struct AreaUiField
    {
        public AreaUiField(AreaUiFieldKind kind, string label, Rectangle rect)
        {
            Kind = kind;
            Label = label;
            Rect = rect;
        }

        public AreaUiFieldKind Kind { get; }
        public string Label { get; }
        public Rectangle Rect { get; }
    }

    private readonly struct AreaIssue
    {
        public AreaIssue(bool blocking, string text)
        {
            Blocking = blocking;
            Text = text;
        }

        public bool Blocking { get; }
        public string Text { get; }
    }

    private MapData _map;
    private readonly Texture2D _pixel;
    private readonly GraphicsDevice _graphics;
    private ProfessionCatalog _professionCatalog;
    private readonly MTEngine.Core.PrototypeManager? _prototypes;

    private AreaZoneData? _selectedArea;
    private bool _typingId;
    private bool _typingPointId;
    private bool _typingName;
    private bool _typingSettlement;
    private bool _typingDistrict;
    private bool _typingFaction;
    private bool _typingRoomPrice;
    private bool _typingBedPrice;
    private string _inputId = "area_1";
    private string _inputPointId = "wander_1";
    private string _inputName = "";
    private string _inputSettlement = "";
    private string _inputDistrict = "";
    private string _inputFaction = "";
    private string _inputRoomPrice = "20";
    private string _inputBedPrice = "8";
    private Rectangle _createZoneRect;
    private int _kindIndex;
    private bool _kindDropdownOpen;
    private bool _professionDropdownOpen;
    private KeyboardState _prevKeys;
    private Point _lastPaintedTile = new(-1, -1);
    private bool _panelConsumedClick;

    private static readonly string[] KindValues =
    {
        AreaZoneKinds.House,
        AreaZoneKinds.Profession,
        AreaZoneKinds.School,
        AreaZoneKinds.Inn,
        AreaZoneKinds.Tavern,
        AreaZoneKinds.Orphanage,
        AreaZoneKinds.Wander,
        AreaZoneKinds.District
    };

    private static readonly Color[] KindColors =
    {
        new(120, 180, 255),
        new(255, 180, 80),
        new(180, 255, 120),
        new(220, 220, 120),
        new(220, 140, 80),
        new(160, 200, 220),
        new(180, 180, 180),
        new(80, 200, 200)
    };

    private const int PanelWidth = 360;
    private const int LabelWidth = 112;
    private const int FieldWidth = 220;
    private const int LineH = 18;
    private const int KindRowH = 20;
    private const int Pad = 8;

    public bool IsTyping => _typingId || _typingPointId || _typingName || _typingSettlement || _typingDistrict
                            || _typingFaction || _typingRoomPrice || _typingBedPrice;
    public bool IsInputBlocking => IsTyping || _kindDropdownOpen || _professionDropdownOpen || _panelConsumedClick;

    public AreaZoneTool(MapData map, GraphicsDevice graphics, ProfessionCatalog professionCatalog,
        MTEngine.Core.PrototypeManager? prototypes = null)
    {
        _map = map;
        _graphics = graphics;
        _professionCatalog = professionCatalog;
        _prototypes = prototypes;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void SetProfessionCatalog(ProfessionCatalog professionCatalog)
        => _professionCatalog = professionCatalog;

    public void SetMap(MapData map)
    {
        _map = map;
        _selectedArea = null;
        _inputId = "area_1";
        _inputPointId = "wander_1";
        _inputName = "";
        _inputSettlement = "";
        _inputDistrict = "";
        _inputFaction = "";
        _inputRoomPrice = "20";
        _inputBedPrice = "8";
        _kindDropdownOpen = false;
        _professionDropdownOpen = false;
    }

    public int RepairRequiredAreaMarkers()
    {
        var changes = 0;
        foreach (var area in _map.Areas)
            changes += RepairAreaDefaults(area);
        return changes;
    }

    public IReadOnlyList<string> GetBlockingValidationErrors()
    {
        var errors = new List<string>();
        foreach (var area in _map.Areas)
            errors.AddRange(BuildAreaIssues(area).Where(issue => issue.Blocking).Select(issue => $"{area.Id}: {issue.Text}"));
        return errors;
    }

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        _panelConsumedClick = false;
        HandleTextInput(keys, mouse, prev);
        _prevKeys = keys;

        if (GetPanelRect().Contains(mouse.Position))
            _panelConsumedClick = true;
    }

    public void UpdateWorldInput(MouseState mouse, MouseState prev, Vector2 worldPos, PointerTool pointerTool, KeyboardState keys)
    {
        if (_panelConsumedClick)
            return;

        var tileX = (int)(worldPos.X / _map.TileSize);
        var tileY = (int)(worldPos.Y / _map.TileSize);
        if (tileX < 0 || tileX >= _map.Width || tileY < 0 || tileY >= _map.Height)
            return;

        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        if (shift)
        {
            HandlePointInput(mouse, prev, tileX, tileY);
            return;
        }

        if (pointerTool == PointerTool.Mouse)
        {
            HandleMouseMode(mouse, prev, tileX, tileY);
            return;
        }

        HandleBrushMode(mouse, tileX, tileY);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        var tileSize = _map.TileSize;
        foreach (var area in _map.Areas)
        {
            var color = ResolveColor(area.Kind);
            var selected = area == _selectedArea;
            var hasBlockingIssues = BuildAreaIssues(area).Any(issue => issue.Blocking);
            var alpha = selected ? 0.5f : 0.28f;
            var tileSet = area.Tiles.Select(tile => (tile.X, tile.Y)).ToHashSet();

            foreach (var tile in area.Tiles)
                spriteBatch.Draw(_pixel, new Rectangle(tile.X * tileSize, tile.Y * tileSize, tileSize, tileSize), color * alpha);

            var borderColor = hasBlockingIssues
                ? EditorTheme.Error * 0.95f
                : selected ? color * 0.95f : color * 0.7f;
            var borderWidth = selected ? 2 : 1;
            foreach (var tile in area.Tiles)
            {
                var px = tile.X * tileSize;
                var py = tile.Y * tileSize;
                if (!tileSet.Contains((tile.X, tile.Y - 1)))
                    spriteBatch.Draw(_pixel, new Rectangle(px, py, tileSize, borderWidth), borderColor);
                if (!tileSet.Contains((tile.X, tile.Y + 1)))
                    spriteBatch.Draw(_pixel, new Rectangle(px, py + tileSize - borderWidth, tileSize, borderWidth), borderColor);
                if (!tileSet.Contains((tile.X - 1, tile.Y)))
                    spriteBatch.Draw(_pixel, new Rectangle(px, py, borderWidth, tileSize), borderColor);
                if (!tileSet.Contains((tile.X + 1, tile.Y)))
                    spriteBatch.Draw(_pixel, new Rectangle(px + tileSize - borderWidth, py, borderWidth, tileSize), borderColor);
            }

            if (area.Tiles.Count > 0)
            {
                var minX = area.Tiles.Min(t => t.X);
                var minY = area.Tiles.Min(t => t.Y);
                var prefix = hasBlockingIssues ? "! " : "";
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"{prefix}{area.Kind}: {area.Id}",
                    new Vector2(minX * tileSize + 2, minY * tileSize - 14), hasBlockingIssues ? EditorTheme.Error : color);
            }

            foreach (var point in area.Points)
            {
                var x = point.X * tileSize + tileSize / 2;
                var y = point.Y * tileSize + tileSize / 2;
                spriteBatch.Draw(_pixel, new Rectangle(x - 4, y - 4, 8, 8), EditorTheme.Warning);
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, point.Id, new Vector2(x + 6, y - 8), EditorTheme.Warning);
            }
        }
    }

    public void DrawUI(SpriteBatch spriteBatch)
    {
        var panel = GetPanelRect();
        EditorTheme.FillRect(spriteBatch, panel, EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, panel, EditorTheme.Border);

        var headerRect = new Rectangle(panel.X, panel.Y, panel.Width, 22);
        EditorTheme.FillRect(spriteBatch, headerRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(headerRect.X, headerRect.Y, 3, headerRect.Height), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "AREA ZONE TOOL",
            new Vector2(panel.X + 10, panel.Y + 5), EditorTheme.Text);

        var x = panel.X + Pad;
        var y = panel.Y + 28;
        var fieldX = x + LabelWidth;

        if (_selectedArea == null)
        {
            _createZoneRect = new Rectangle(x, y, panel.Width - Pad * 2, 28);
            EditorTheme.DrawButton(spriteBatch, _createZoneRect, "Create New Zone",
                EditorTheme.Small, _createZoneRect.Contains(Mouse.GetState().Position), active: false);
            y += 38;

            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                "No zone selected. Click a zone with Select tool (V)",
                new Vector2(x, y), EditorTheme.Text);
            y += 16;

            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                "or create a zone, then paint it with Brush tool (B).",
                new Vector2(x, y), EditorTheme.TextMuted);
            y += 16;
        }
        else
        {
            // Show zone editing interface
            DrawLabel(spriteBatch, "Area ID", x, y);
            DrawInputField(spriteBatch, new Rectangle(fieldX, y, FieldWidth, LineH), _inputId, _typingId);
            y += LineH + 8;

            DrawLabel(spriteBatch, "Kind", x, y);
            DrawKindDropdownField(spriteBatch, GetKindRect(), _kindDropdownOpen);
            y += LineH + 8;

            foreach (var field in BuildKindFields(fieldX, y))
            {
                DrawLabel(spriteBatch, field.Label, x, field.Rect.Y);
                if (field.Kind == AreaUiFieldKind.Profession)
                {
                    DrawProfessionDropdownField(spriteBatch, field.Rect, _professionDropdownOpen);
                }
                else
                {
                    DrawInputField(spriteBatch, field.Rect, GetFieldValue(field.Kind), IsFieldTyping(field.Kind));
                }

                y = field.Rect.Bottom + 8;
            }

            DrawLabel(spriteBatch, "Point ID", x, y);
            DrawInputField(spriteBatch, new Rectangle(fieldX, y, FieldWidth, LineH), _inputPointId, _typingPointId);
            y += LineH + 8;
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Brush: LClick paint, RClick erase",
            new Vector2(x, y), EditorTheme.TextMuted);
        y += 14;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Select: LClick select, empty click deselect",
            new Vector2(x, y), EditorTheme.TextMuted);
        y += 14;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Shift+LClick add point, Shift+RClick delete point",
            new Vector2(x, y), EditorTheme.TextMuted);
        y += 16;

        if (_selectedArea != null)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                $"Selected: {_selectedArea.Kind}/{_selectedArea.Id} ({_selectedArea.Tiles.Count}t, {_selectedArea.Points.Count}p)",
                new Vector2(x, y), EditorTheme.Warning);
            y += LineH;
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Enter — apply    Delete — remove    ESC — deselect",
                new Vector2(x, y), EditorTheme.TextMuted);
            y += LineH;

            if (_selectedArea.Properties.Count > 0)
            {
                var properties = string.Join(", ", _selectedArea.Properties.Select(pair => $"{pair.Key}={pair.Value}"));
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, properties, new Vector2(x, y), EditorTheme.Success);
                y += LineH;
            }

            var issues = BuildAreaIssues(_selectedArea);
            foreach (var issue in issues.Take(4))
            {
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                    Truncate((issue.Blocking ? "! " : "- ") + issue.Text, panel.Width - Pad * 2),
                    new Vector2(x, y), issue.Blocking ? EditorTheme.Error : EditorTheme.Warning);
                y += 14;
            }
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Areas: {_map.Areas.Count}",
            new Vector2(x, y), EditorTheme.Accent);
        y += LineH;

        var allIssues = _map.Areas.SelectMany(BuildAreaIssues).ToList();
        if (allIssues.Count > 0)
        {
            var blockingCount = allIssues.Count(issue => issue.Blocking);
            var first = allIssues.FirstOrDefault(issue => issue.Blocking).Text ?? allIssues[0].Text;
            var text = blockingCount > 0
                ? $"{blockingCount} area error(s): {first}"
                : $"{allIssues.Count} area warning(s): {first}";
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(text, panel.Width - Pad * 2),
                new Vector2(x, y), blockingCount > 0 ? EditorTheme.Error : EditorTheme.Warning);
        }

        if (_kindDropdownOpen)
            DrawKindDropdown(spriteBatch);
        if (_professionDropdownOpen)
            DrawProfessionDropdown(spriteBatch);
    }

    private void HandlePointInput(MouseState mouse, MouseState prev, int tileX, int tileY)
    {
        if (_selectedArea == null)
            return;

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            var pointId = string.IsNullOrWhiteSpace(_inputPointId)
                ? $"point_{_selectedArea.Points.Count + 1}"
                : _inputPointId;
            _selectedArea.Points.RemoveAll(point => string.Equals(point.Id, pointId, StringComparison.OrdinalIgnoreCase));
            _selectedArea.Points.Add(new AreaPointData { Id = pointId, X = tileX, Y = tileY });
        }

        if (mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released)
            _selectedArea.Points.RemoveAll(point => point.X == tileX && point.Y == tileY);
    }

    private void HandleMouseMode(MouseState mouse, MouseState prev, int tileX, int tileY)
    {
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            var found = _map.Areas.FirstOrDefault(area => area.ContainsTile(tileX, tileY));
            if (found != null)
            {
                _selectedArea = found;
                SyncUIFromArea(found);
                return;
            }

            _selectedArea = null;
            ClearTyping();
        }

        if (mouse.MiddleButton == ButtonState.Pressed && prev.MiddleButton == ButtonState.Released)
        {
            var found = _map.Areas.FirstOrDefault(area => area.ContainsTile(tileX, tileY));
            if (found != null)
            {
                _selectedArea = found;
                SyncUIFromArea(found);
            }
        }
    }

    private void HandleBrushMode(MouseState mouse, int tileX, int tileY)
    {
        if (_selectedArea == null)
            return;

        var current = new Point(tileX, tileY);
        if (mouse.LeftButton == ButtonState.Pressed)
        {
            if (string.IsNullOrWhiteSpace(_inputId))
                _inputId = GenerateAreaId(KindValues[_kindIndex]);

            if (current == _lastPaintedTile)
                return;

            var area = _selectedArea;

            area.Kind = KindValues[_kindIndex];
            area.AddTile(tileX, tileY);
            ApplyUIToSelected();
            RepairAreaDefaults(area);
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
            {
                if (!area.ContainsTile(tileX, tileY))
                    continue;

                area.RemoveTile(tileX, tileY);
                changed = true;
            }

            if (!changed)
                return;

            _map.Areas.RemoveAll(area => area.Tiles.Count == 0);
            if (_selectedArea != null && _selectedArea.Tiles.Count == 0)
                _selectedArea = null;
        }
    }

    private void ApplyUIToSelected()
    {
        if (_selectedArea == null)
            return;

        _selectedArea.Id = _inputId;
        _selectedArea.Kind = KindValues[_kindIndex];
        ApplyKindProperties(_selectedArea);

        RepairAreaDefaults(_selectedArea);
    }

    private void CreateNewArea()
    {
        var kind = KindValues[_kindIndex];
        var id = GenerateAreaId(kind);
        var area = new AreaZoneData { Id = id, Kind = kind };
        ApplyAutomaticKindProperties(area);
        RepairAreaDefaults(area);
        _map.Areas.Add(area);
        _selectedArea = area;
        SyncUIFromArea(area);
    }

    private string GenerateAreaId(string kind)
    {
        var prefix = string.IsNullOrWhiteSpace(kind) ? "area" : kind.Trim().ToLowerInvariant();
        var index = 1;
        while (_map.Areas.Any(area => string.Equals(area.Id, $"{prefix}_{index}", StringComparison.OrdinalIgnoreCase)))
            index++;
        return $"{prefix}_{index}";
    }

    private void DeleteSelectedArea()
    {
        if (_selectedArea == null)
            return;

        _map.Areas.Remove(_selectedArea);
        _selectedArea = null;
    }

    private void SyncUIFromArea(AreaZoneData area)
    {
        _inputId = area.Id;
        var kindIndex = Array.IndexOf(KindValues, area.Kind);
        _kindIndex = kindIndex >= 0 ? kindIndex : 0;
        SyncKindInputsFromArea(area);
    }

    private Rectangle GetKindRect()
    {
        var panel = GetPanelRect();
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;
        var y = panel.Y + 28 + LineH + 8;
        return new Rectangle(fieldX, y, FieldWidth, LineH);
    }

    private Rectangle GetKindDropdownRect()
    {
        var kindRect = GetKindRect();
        return new Rectangle(kindRect.X, kindRect.Bottom + 2, kindRect.Width, KindValues.Length * KindRowH + 4);
    }

    private Rectangle GetProfessionRect()
    {
        var panel = GetPanelRect();
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;
        var y = panel.Y + 28 + (LineH + 8) * 2;
        return BuildKindFields(fieldX, y)
            .FirstOrDefault(field => field.Kind == AreaUiFieldKind.Profession).Rect;
    }

    private Rectangle GetProfessionDropdownRect()
    {
        var professionRect = GetProfessionRect();
        var rows = Math.Max(1, _professionCatalog.Professions.Count + 1);
        return new Rectangle(professionRect.X, professionRect.Bottom + 2, professionRect.Width, rows * KindRowH + 4);
    }

    private void HandleTextInput(KeyboardState keys, MouseState mouse, MouseState prev)
    {
        var panel = GetPanelRect();
        var x = panel.X + Pad;
        var fieldX = x + LabelWidth;
        var y = panel.Y + 28;
        var idRect = new Rectangle(fieldX, y, FieldWidth, LineH);
        y += LineH + 8;
        var kindRect = new Rectangle(fieldX, y, FieldWidth, LineH);
        y += LineH + 8;
        var kindFields = BuildKindFields(fieldX, y);
        var professionField = kindFields.FirstOrDefault(field => field.Kind == AreaUiFieldKind.Profession);
        if (kindFields.Count > 0)
            y = kindFields[^1].Rect.Bottom + 8;
        var pointIdRect = new Rectangle(fieldX, y, FieldWidth, LineH);

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            var point = mouse.Position;
            if (_selectedArea == null)
            {
                if (_createZoneRect.Contains(point))
                {
                    CreateNewArea();
                    ClearTyping();
                    return;
                }

                ClearTyping();
                _kindDropdownOpen = false;
                _professionDropdownOpen = false;
                return;
            }

            if (_kindDropdownOpen && TryPickKindFromDropdown(point))
            {
                ClearTyping();
                return;
            }

            if (_professionDropdownOpen && TryPickProfessionFromDropdown(point))
            {
                ClearTyping();
                return;
            }

            if (kindRect.Contains(point))
            {
                _kindDropdownOpen = !_kindDropdownOpen;
                _professionDropdownOpen = false;
                ClearTyping();
                return;
            }

            if (professionField.Kind == AreaUiFieldKind.Profession && professionField.Rect.Contains(point))
            {
                _professionDropdownOpen = !_professionDropdownOpen;
                _kindDropdownOpen = false;
                ClearTyping();
                return;
            }

            if (_kindDropdownOpen && !GetKindDropdownRect().Contains(point))
                _kindDropdownOpen = false;
            if (_professionDropdownOpen && !GetProfessionDropdownRect().Contains(point))
                _professionDropdownOpen = false;

            ClearTyping();
            _typingId = idRect.Contains(point);
            _typingPointId = pointIdRect.Contains(point);
            foreach (var field in kindFields)
            {
                if (field.Kind != AreaUiFieldKind.Profession && field.Rect.Contains(point))
                    SetFieldTyping(field.Kind);
            }
        }

        if (IsPressed(keys, Keys.Escape))
        {
            if (IsTyping || _kindDropdownOpen || _professionDropdownOpen)
            {
                ClearTyping();
                _kindDropdownOpen = false;
                _professionDropdownOpen = false;
            }
            else if (_selectedArea != null)
            {
                // Deselect area on ESC if nothing is being edited
                _selectedArea = null;
                _inputId = "";
                _inputPointId = "wander_1";
                ClearTyping();
            }
        }

        if (IsPressed(keys, Keys.Enter))
        {
            ApplyUIToSelected();
            ClearTyping();
        }

        if (!IsTyping && IsPressed(keys, Keys.Delete))
            DeleteSelectedArea();

        if (!IsTyping)
        {
            var kindKeyboardFocused = _kindDropdownOpen || panel.Contains(mouse.Position);
            if (IsPressed(keys, Keys.OemOpenBrackets) || IsPressed(keys, Keys.Left) || IsPressed(keys, Keys.Up))
            {
                if (IsPressed(keys, Keys.OemOpenBrackets) || kindKeyboardFocused)
                    StepKind(-1);
            }
            if (IsPressed(keys, Keys.OemCloseBrackets) || IsPressed(keys, Keys.Right) || IsPressed(keys, Keys.Down))
            {
                if (IsPressed(keys, Keys.OemCloseBrackets) || kindKeyboardFocused)
                    StepKind(1);
            }
        }

        if (_typingId) _inputId = ProcessTyping(keys, _inputId);
        else if (_typingPointId) _inputPointId = ProcessTyping(keys, _inputPointId);
        else ProcessKindFieldTyping(keys);
    }

    private bool TryPickKindFromDropdown(Point point)
    {
        var dropdown = GetKindDropdownRect();
        if (!dropdown.Contains(point))
            return false;

        var index = (point.Y - dropdown.Y - 2) / KindRowH;
        if (index >= 0 && index < KindValues.Length)
            SetKindIndex(index);

        _kindDropdownOpen = false;
        return true;
    }

    private bool TryPickProfessionFromDropdown(Point point)
    {
        var dropdown = GetProfessionDropdownRect();
        if (!dropdown.Contains(point))
            return false;

        var index = (point.Y - dropdown.Y - 2) / KindRowH;
        if (index == 0)
            SetSelectedProfession("");
        else
        {
            var profession = _professionCatalog.Professions
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ElementAtOrDefault(index - 1);
            if (profession != null)
                SetSelectedProfession(profession.Id);
        }

        _professionDropdownOpen = false;
        return true;
    }

    private void StepKind(int delta)
        => SetKindIndex(_kindIndex + delta);

    private void SetKindIndex(int index)
    {
        _kindIndex = (index % KindValues.Length + KindValues.Length) % KindValues.Length;
        if (KindValues[_kindIndex] != AreaZoneKinds.Profession)
            _professionDropdownOpen = false;
        if (KindValues[_kindIndex] == AreaZoneKinds.Profession && (_inputPointId is "" or "wander_1" or "bed_slot_a"))
            _inputPointId = "work_anchor";
        if (KindValues[_kindIndex] == AreaZoneKinds.House && (_inputPointId is "" or "wander_1" or "work_anchor"))
            _inputPointId = "bed_slot_a";
        if (_selectedArea != null)
        {
            _selectedArea.Kind = KindValues[_kindIndex];
            ApplyAutomaticKindProperties(_selectedArea);
            SyncKindInputsFromArea(_selectedArea);
            RepairAreaDefaults(_selectedArea);
        }
    }

    private void SetSelectedProfession(string professionId)
    {
        if (_selectedArea == null)
            return;

        if (!IsProfessionCapableKind(_selectedArea.Kind))
        {
            _selectedArea.Kind = AreaZoneKinds.Profession;
            _kindIndex = Array.IndexOf(KindValues, AreaZoneKinds.Profession);
        }

        if (_selectedArea.Kind == AreaZoneKinds.Profession && (_inputPointId is "" or "wander_1" or "bed_slot_a"))
            _inputPointId = "work_anchor";
        if (string.IsNullOrWhiteSpace(professionId))
            _selectedArea.Properties.Remove("professionId");
        else
            _selectedArea.Properties["professionId"] = professionId;
        SyncKindInputsFromArea(_selectedArea);
        RepairAreaDefaults(_selectedArea);
    }

    private string ProcessTyping(KeyboardState keys, string current)
    {
        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key))
                continue;

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

    private List<AreaUiField> BuildKindFields(int fieldX, int startY)
    {
        var result = new List<AreaUiField>();
        var y = startY;
        var kind = KindValues[_kindIndex];

        void Add(AreaUiFieldKind fieldKind, string label)
        {
            result.Add(new AreaUiField(fieldKind, label, new Rectangle(fieldX, y, FieldWidth, LineH)));
            y += LineH + 8;
        }

        if (kind == AreaZoneKinds.Profession)
        {
            Add(AreaUiFieldKind.Profession, "Profession");
            Add(AreaUiFieldKind.Name, "Name");
            Add(AreaUiFieldKind.Settlement, "Settlement");
            Add(AreaUiFieldKind.District, "District");
        }
        else if (kind == AreaZoneKinds.Tavern)
            Add(AreaUiFieldKind.Name, "Name");
        else if (kind == AreaZoneKinds.Inn)
        {
            Add(AreaUiFieldKind.Name, "Name");
            Add(AreaUiFieldKind.RoomPrice, "Room price");
            Add(AreaUiFieldKind.BedPrice, "Bed price");
        }
        else if (kind == AreaZoneKinds.House)
        {
            Add(AreaUiFieldKind.Name, "Name");
            Add(AreaUiFieldKind.Settlement, "Settlement");
            Add(AreaUiFieldKind.District, "District");
            Add(AreaUiFieldKind.Faction, "Faction");
        }
        else if (kind == AreaZoneKinds.District)
        {
            Add(AreaUiFieldKind.Name, "Name");
            Add(AreaUiFieldKind.Settlement, "Settlement");
            Add(AreaUiFieldKind.Faction, "Faction");
        }
        else if (kind is AreaZoneKinds.School or AreaZoneKinds.Orphanage or AreaZoneKinds.Wander)
        {
            Add(AreaUiFieldKind.Name, "Name");
        }

        return result;
    }

    private void SyncKindInputsFromArea(AreaZoneData area)
    {
        _inputName = GetProperty(area, "name");
        _inputSettlement = FirstNonEmpty(GetProperty(area, "settlementId"), GetProperty(area, "settlement"));
        _inputDistrict = FirstNonEmpty(GetProperty(area, "districtId"), GetProperty(area, "district"));
        _inputFaction = FirstNonEmpty(GetProperty(area, "factionId"), GetProperty(area, "faction"));
        _inputRoomPrice = FirstNonEmpty(GetProperty(area, "roomPrice"), "20");
        _inputBedPrice = FirstNonEmpty(GetProperty(area, "bedPrice"), "8");
    }

    private void ApplyKindProperties(AreaZoneData area)
    {
        SetProperty(area, "name", _inputName);
        SetProperty(area, "settlementId", _inputSettlement);
        SetProperty(area, "districtId", _inputDistrict);
        SetProperty(area, "factionId", _inputFaction);

        var kind = KindValues[_kindIndex];
        if (kind == AreaZoneKinds.Inn)
        {
            SetProperty(area, "roomPrice", _inputRoomPrice);
            SetProperty(area, "bedPrice", _inputBedPrice);
        }
        else
        {
            area.Properties.Remove("roomPrice");
            area.Properties.Remove("bedPrice");
        }

        // Tavern always has innkeeper profession
        if (kind == AreaZoneKinds.Tavern)
        {
            SetProperty(area, "professionId", "innkeeper");
        }
        else if (!IsProfessionCapableKind(kind))
        {
            area.Properties.Remove("professionId");
        }

        ApplyAutomaticKindProperties(area);
    }

    private static void ApplyAutomaticKindProperties(AreaZoneData area)
    {
        if (string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase))
            area.Properties["professionId"] = "innkeeper";
    }

    private string GetFieldValue(AreaUiFieldKind kind) => kind switch
    {
        AreaUiFieldKind.Name => _inputName,
        AreaUiFieldKind.Settlement => _inputSettlement,
        AreaUiFieldKind.District => _inputDistrict,
        AreaUiFieldKind.Faction => _inputFaction,
        AreaUiFieldKind.RoomPrice => _inputRoomPrice,
        AreaUiFieldKind.BedPrice => _inputBedPrice,
        _ => ""
    };

    private bool IsFieldTyping(AreaUiFieldKind kind) => kind switch
    {
        AreaUiFieldKind.Name => _typingName,
        AreaUiFieldKind.Settlement => _typingSettlement,
        AreaUiFieldKind.District => _typingDistrict,
        AreaUiFieldKind.Faction => _typingFaction,
        AreaUiFieldKind.RoomPrice => _typingRoomPrice,
        AreaUiFieldKind.BedPrice => _typingBedPrice,
        _ => false
    };

    private void SetFieldTyping(AreaUiFieldKind kind)
    {
        switch (kind)
        {
            case AreaUiFieldKind.Name: _typingName = true; break;
            case AreaUiFieldKind.Settlement: _typingSettlement = true; break;
            case AreaUiFieldKind.District: _typingDistrict = true; break;
            case AreaUiFieldKind.Faction: _typingFaction = true; break;
            case AreaUiFieldKind.RoomPrice: _typingRoomPrice = true; break;
            case AreaUiFieldKind.BedPrice: _typingBedPrice = true; break;
        }
    }

    private void ProcessKindFieldTyping(KeyboardState keys)
    {
        if (_typingName) _inputName = ProcessTyping(keys, _inputName);
        else if (_typingSettlement) _inputSettlement = ProcessTyping(keys, _inputSettlement);
        else if (_typingDistrict) _inputDistrict = ProcessTyping(keys, _inputDistrict);
        else if (_typingFaction) _inputFaction = ProcessTyping(keys, _inputFaction);
        else if (_typingRoomPrice) _inputRoomPrice = ProcessTyping(keys, _inputRoomPrice);
        else if (_typingBedPrice) _inputBedPrice = ProcessTyping(keys, _inputBedPrice);

        ApplyUIToSelected();
    }

    private void ClearTyping()
    {
        _typingId = false;
        _typingPointId = false;
        _typingName = false;
        _typingSettlement = false;
        _typingDistrict = false;
        _typingFaction = false;
        _typingRoomPrice = false;
        _typingBedPrice = false;
    }

    private static string GetProperty(AreaZoneData area, string key)
        => area.Properties.TryGetValue(key, out var value) ? value : "";

    private static void SetProperty(AreaZoneData area, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            area.Properties.Remove(key);
        else
            area.Properties[key] = value.Trim();
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private Rectangle GetPanelRect()
    {
        var viewport = _graphics.Viewport;
        var visibleFieldCount = 3 + BuildKindFields(0, 0).Count;
        var height = Pad + 22 + (LineH + 8) * visibleFieldCount + 14 * 3 + 16 + LineH * 8 + Pad;
        return new Rectangle(viewport.Width - PanelWidth - 10, viewport.Height - height - 44, PanelWidth, height);
    }

    private int RepairAreaDefaults(AreaZoneData area)
    {
        var changes = 0;

        var beforeProfession = area.Properties.GetValueOrDefault("professionId", "");
        ApplyAutomaticKindProperties(area);
        if (!string.Equals(beforeProfession, area.Properties.GetValueOrDefault("professionId", ""), StringComparison.OrdinalIgnoreCase))
            changes++;

        if (!IsProfessionCapableKind(area.Kind)
            && area.Properties.Remove("professionId"))
        {
            changes++;
        }

        if (string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase))
            changes += EnsureRequiredPoint(area, "bed_slot_a", "bed_slot_", exactId: false);
        else if (string.Equals(area.Kind, AreaZoneKinds.Profession, StringComparison.OrdinalIgnoreCase))
            changes += EnsureRequiredPoint(area, "work_anchor", "work_anchor", exactId: true);
        else if (string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase))
        {
            changes += EnsureDefaultProperty(area, "roomPrice", "20");
            changes += EnsureDefaultProperty(area, "bedPrice", "8");
        }

        return changes;
    }

    private static int EnsureDefaultProperty(AreaZoneData area, string key, string value)
    {
        if (area.Properties.ContainsKey(key))
            return 0;

        area.Properties[key] = value;
        return 1;
    }

    private static int EnsureRequiredPoint(AreaZoneData area, string requiredId, string acceptedPrefix, bool exactId)
    {
        if (area.Tiles.Count == 0)
            return 0;

        var point = exactId
            ? area.GetPoint(requiredId)
            : area.GetPointsByPrefix(acceptedPrefix).FirstOrDefault();
        var tile = PickAreaCenterTile(area);
        if (tile == null)
            return 0;

        if (point == null)
        {
            area.Points.Add(new AreaPointData { Id = requiredId, X = tile.X, Y = tile.Y });
            return 1;
        }

        if (!area.ContainsTile(point.X, point.Y))
        {
            point.X = tile.X;
            point.Y = tile.Y;
            return 1;
        }

        return 0;
    }

    private List<AreaIssue> BuildAreaIssues(AreaZoneData area)
    {
        var issues = new List<AreaIssue>();

        if (string.Equals(area.Kind, AreaZoneKinds.Settlement, StringComparison.OrdinalIgnoreCase))
            issues.Add(new AreaIssue(false, "legacy settlement area ignored; use map City/Faction header"));

        if (string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase))
        {
            var hasNamedSlot = area.GetPointsByPrefix("bed_slot_").Any();
            var hasAutoBed = _prototypes != null
                && MTEngine.Npc.HouseBedScanner.HasAnyBedInsideArea(area, _map, _prototypes);
            if (!hasNamedSlot && !hasAutoBed)
                issues.Add(new AreaIssue(true, "house needs bed_slot_* point or a Bed entity inside"));
            else if (hasNamedSlot
                     && area.GetPointsByPrefix("bed_slot_").Any(point => !area.ContainsTile(point.X, point.Y)))
                issues.Add(new AreaIssue(true, "bed_slot_* must be inside house area"));
        }
        else if (string.Equals(area.Kind, AreaZoneKinds.Profession, StringComparison.OrdinalIgnoreCase))
        {
            if (!area.Properties.TryGetValue("professionId", out var professionId) || string.IsNullOrWhiteSpace(professionId))
                issues.Add(new AreaIssue(true, "profession area needs selected Profession"));
            else if (_professionCatalog.Get(professionId) == null)
                issues.Add(new AreaIssue(true, $"unknown professionId '{professionId}'"));

            var workAnchor = area.GetPoint("work_anchor");
            if (workAnchor == null)
                issues.Add(new AreaIssue(true, "profession area needs work_anchor point"));
            else if (!area.ContainsTile(workAnchor.X, workAnchor.Y))
                issues.Add(new AreaIssue(true, "work_anchor must be inside profession area"));
        }
        else if (string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase))
        {
            if (area.Properties.TryGetValue("professionId", out var professionId) && !string.IsNullOrWhiteSpace(professionId)
                && _professionCatalog.Get(professionId) == null)
            {
                issues.Add(new AreaIssue(true, $"unknown professionId '{professionId}'"));
            }
        }
        else if (string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase))
        {
            if (!area.Properties.ContainsKey("roomPrice") && !area.Properties.ContainsKey("bedPrice") && !area.Properties.ContainsKey("rentPrice"))
                issues.Add(new AreaIssue(false, "inn can define roomPrice / bedPrice / rentPrice"));
        }
        else if (area.Properties.ContainsKey("professionId"))
        {
            issues.Add(new AreaIssue(false, "professionId is only used by profession or tavern areas"));
        }

        return issues;
    }

    private static TriggerTile? PickAreaCenterTile(AreaZoneData area)
    {
        if (area.Tiles.Count == 0)
            return null;

        var minX = area.Tiles.Min(tile => tile.X);
        var maxX = area.Tiles.Max(tile => tile.X);
        var minY = area.Tiles.Min(tile => tile.Y);
        var maxY = area.Tiles.Max(tile => tile.Y);
        var centerX = (minX + maxX) / 2f;
        var centerY = (minY + maxY) / 2f;

        return area.Tiles
            .OrderBy(tile => MathF.Abs(tile.X - centerX) + MathF.Abs(tile.Y - centerY))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .FirstOrDefault();
    }

    private static Color ResolveColor(string kind)
    {
        if (string.Equals(kind, AreaZoneKinds.Settlement, StringComparison.OrdinalIgnoreCase))
            return new Color(255, 100, 220);

        var index = Array.IndexOf(KindValues, kind);
        return index >= 0 ? KindColors[index] : Color.Magenta;
    }

    private string GetAreaProfessionId()
    {
        if (_selectedArea?.Properties.TryGetValue("professionId", out var professionId) == true)
            return professionId;
        return "";
    }

    private static bool IsProfessionCapableKind(string kind)
        => string.Equals(kind, AreaZoneKinds.Profession, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxWidth)
    {
        if (EditorTheme.Small.MeasureString(value).X <= maxWidth)
            return value;

        while (value.Length > 1 && EditorTheme.Small.MeasureString(value + "...").X > maxWidth)
            value = value[..^1];
        return value + "...";
    }

    private static void DrawLabel(SpriteBatch spriteBatch, string text, int x, int y)
    {
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, text, new Vector2(x, y + 2), EditorTheme.TextDim);
    }

    private void DrawKindDropdownField(SpriteBatch spriteBatch, Rectangle rect, bool open)
    {
        var color = ResolveColor(KindValues[_kindIndex]);
        EditorTheme.FillRect(spriteBatch, rect, open ? EditorTheme.BgDeep : EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, rect, open ? EditorTheme.Accent : EditorTheme.Border);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(rect.X + 1, rect.Y + 1, 4, rect.Height - 2), color);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, KindValues[_kindIndex],
            new Vector2(rect.X + 10, rect.Y + 2), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v",
            new Vector2(rect.Right - 15, rect.Y + 1), open ? EditorTheme.Text : EditorTheme.TextMuted);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "< >",
            new Vector2(rect.Right - 43, rect.Y + 4), EditorTheme.TextMuted);
    }

    private void DrawKindDropdown(SpriteBatch spriteBatch)
    {
        var rect = GetKindDropdownRect();
        EditorTheme.DrawShadow(spriteBatch, rect, 5);
        EditorTheme.DrawPanel(spriteBatch, rect, EditorTheme.Panel, EditorTheme.AccentDim);

        var y = rect.Y + 2;
        for (var i = 0; i < KindValues.Length; i++)
        {
            var row = new Rectangle(rect.X + 2, y, rect.Width - 4, KindRowH - 1);
            var selected = i == _kindIndex;
            var color = ResolveColor(KindValues[i]);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(row.X, row.Y, 4, row.Height), color);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, KindValues[i],
                new Vector2(row.X + 10, row.Y + 3), selected ? EditorTheme.Text : EditorTheme.TextDim);
            y += KindRowH;
        }
    }

    private void DrawProfessionDropdownField(SpriteBatch spriteBatch, Rectangle rect, bool open)
    {
        var isProfessionKind = IsProfessionCapableKind(KindValues[_kindIndex]);
        var professionId = GetAreaProfessionId();
        var profession = _professionCatalog.Get(professionId);
        var text = string.IsNullOrWhiteSpace(professionId)
            ? "No profession"
            : profession == null
                ? $"Missing: {professionId}"
                : $"{profession.Name} ({profession.Id})";

        EditorTheme.FillRect(spriteBatch, rect, open ? EditorTheme.BgDeep : EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, rect, open ? EditorTheme.Accent : isProfessionKind ? EditorTheme.BorderSoft : EditorTheme.Border);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(rect.X + 1, rect.Y + 1, 4, rect.Height - 2),
            isProfessionKind ? EditorTheme.Success : EditorTheme.TextDisabled);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(text, rect.Width - 32),
            new Vector2(rect.X + 10, rect.Y + 2), isProfessionKind ? EditorTheme.Text : EditorTheme.TextMuted);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v",
            new Vector2(rect.Right - 15, rect.Y + 1), open ? EditorTheme.Text : EditorTheme.TextMuted);
    }

    private void DrawProfessionDropdown(SpriteBatch spriteBatch)
    {
        var rect = GetProfessionDropdownRect();
        EditorTheme.DrawShadow(spriteBatch, rect, 5);
        EditorTheme.DrawPanel(spriteBatch, rect, EditorTheme.Panel, EditorTheme.AccentDim);

        var rows = new List<(string Id, string Label, string Hint)>
        {
            ("", "No profession", "clear professionId")
        };
        rows.AddRange(_professionCatalog.Professions
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Id, p.Name, $"{p.Id} | skill:{p.PrimarySkill}" + (p.IsTrader ? $" | tags:{p.TradeTags.Count}" : ""))));

        var selectedId = GetAreaProfessionId();
        var y = rect.Y + 2;
        foreach (var rowData in rows)
        {
            if (y + KindRowH > rect.Bottom - 2)
                break;

            var row = new Rectangle(rect.X + 2, y, rect.Width - 4, KindRowH - 1);
            var selected = string.Equals(rowData.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(row.X, row.Y, 4, row.Height),
                string.IsNullOrWhiteSpace(rowData.Id) ? EditorTheme.TextMuted : EditorTheme.Success);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(rowData.Label, row.Width - 16),
                new Vector2(row.X + 10, row.Y + 3), selected ? EditorTheme.Text : EditorTheme.TextDim);
            y += KindRowH;
        }
    }

    private static void DrawInputField(SpriteBatch spriteBatch, Rectangle rect, string text, bool active)
    {
        EditorTheme.FillRect(spriteBatch, rect, active ? EditorTheme.BgDeep : EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, rect, active ? EditorTheme.Accent : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, text + (active ? "│" : ""),
            new Vector2(rect.X + 6, rect.Y + 2), EditorTheme.Text);
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
