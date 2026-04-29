#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEditor.UI;

public sealed class PrototypeEditorPanel
{
    private enum FocusField
    {
        None,
        Id,
        Name,
        Category,
        Base,
        NewComponent,
        Json,
        ComponentField
    }

    private enum ComponentEditMode
    {
        Fields,
        Raw
    }

    private sealed class FieldDescriptor
    {
        public required string JsonKey { get; init; }
        public required Type ClrType { get; init; }
        public required string Label { get; init; }
    }

    private sealed class PrototypeDocument
    {
        public required string FilePath { get; init; }
        public required JsonObject Node { get; set; }

        public string Id => ReadString(Node, "id");
        public string Name => ReadString(Node, "name");
        public string Category => ReadString(Node, "category");
        public string BaseId => ReadString(Node, "base");
        public bool Abstract => ReadBool(Node, "abstract");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly GraphicsDevice _graphics;
    private readonly string _rootDirectory;
    private readonly List<PrototypeDocument> _documents = new();
    private readonly List<string> _registeredComponents;

    private Rectangle _bounds;
    private Rectangle _headerRect;
    private Rectangle _listRect;
    private Rectangle _detailRect;
    private Rectangle _idFieldRect;
    private Rectangle _nameFieldRect;
    private Rectangle _categoryFieldRect;
    private Rectangle _baseFieldRect;
    private Rectangle _abstractButtonRect;
    private Rectangle _componentRect;
    private Rectangle _componentListRect;
    private Rectangle _newComponentFieldRect;
    private Rectangle _addComponentButtonRect;
    private Rectangle _removeComponentButtonRect;
    private Rectangle _componentPickerRect;
    private Rectangle _jsonPanelRect;
    private Rectangle _jsonContentRect;
    private Rectangle _rootJsonButtonRect;
    private Rectangle _formatJsonButtonRect;
    private Rectangle _modeToggleRect;
    private Rectangle _saveButtonRect;
    private Rectangle _newButtonRect;
    private Rectangle _reloadButtonRect;
    private Rectangle _deleteButtonRect;

    private string? _selectedPath;
    private JsonObject _draft = new();
    private string _draftId = "";
    private string _draftName = "";
    private string _draftCategory = "entity";
    private string _draftBase = "";
    private bool _draftAbstract;
    private string _newComponentId = "info";
    private string? _selectedComponentId;
    private bool _editingRootJson;
    private List<string> _jsonLines = new() { "{}" };
    private int _jsonLineIndex;
    private int _jsonCaret;
    private int _jsonScroll;
    private int _listScroll;
    private int _componentScroll;
    private int _componentPickerScroll;
    private bool _componentPickerOpen;
    private ComponentEditMode _editMode = ComponentEditMode.Fields;
    private List<FieldDescriptor> _fieldDescriptors = new();
    private string? _focusedFieldKey;
    private int _focusedSubIndex = -1;
    private string _fieldBuffer = "";
    private int _fieldsScroll;
    private const int FieldRowHeight = 30;
    private readonly List<(FieldDescriptor Descriptor, Rectangle InputRect, int SubIndex)> _fieldHitRects = new();
    private string _error = "";
    private bool _dirty;
    private FocusField _focus;
    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;
    private string? _deleteConfirmPath;

    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;
    private const int RowHeight = 34;
    private const int ComponentRowHeight = 24;
    private const int JsonLineHeight = 17;

    public bool IsTyping => _focus != FocusField.None;

    public PrototypeEditorPanel(GraphicsDevice graphics, string rootDirectory)
    {
        _graphics = graphics;
        _rootDirectory = rootDirectory;
        ComponentRegistry.EnsureInitialized();
        _registeredComponents = ComponentRegistry.GetAll().Keys
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReloadFromDisk();
    }

    public bool Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys, Action<string> showMessage)
    {
        RebuildLayout();

        var changed = false;
        var scrollDelta = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scrollDelta != 0)
            HandleScroll(mouse.Position, scrollDelta);

        var clicked = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
        if (clicked)
            changed = HandleClick(mouse.Position, showMessage);

        if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
            return changed;

        if (_focus != FocusField.None)
        {
            if (IsPressed(keys, prevKeys, Keys.Enter) && _focus != FocusField.Json)
                changed |= SaveCurrent(showMessage);

            HandleTextInput(keys, prevKeys);
        }

        return changed;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        RebuildLayout();

        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);
        EditorTheme.FillRect(spriteBatch, _headerRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(_headerRect.X, _headerRect.Y, 3, _headerRect.Height), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "PROTOTYPE EDITOR",
            new Vector2(_headerRect.X + 12, _headerRect.Y + 8), EditorTheme.Text);

        DrawHeaderButtons(spriteBatch);
        DrawPrototypeList(spriteBatch);
        DrawDetails(spriteBatch);
        DrawComponentPicker(spriteBatch);
    }

    public void ReloadFromDisk()
    {
        _documents.Clear();
        Directory.CreateDirectory(_rootDirectory);

        foreach (var file in Directory.GetFiles(_rootDirectory, "proto.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                if (node == null)
                    continue;

                _documents.Add(new PrototypeDocument
                {
                    FilePath = file,
                    Node = node
                });
            }
            catch
            {
                // Bad documents are skipped here; full JSON editing can still be done externally.
            }
        }

        if (_selectedPath == null || _documents.All(doc => !SamePath(doc.FilePath, _selectedPath)))
            _selectedPath = _documents.FirstOrDefault()?.FilePath;

        LoadSelectedDocument();
        _error = "";
    }

    public bool CreateNew(Action<string> showMessage)
    {
        Directory.CreateDirectory(_rootDirectory);
        var id = GenerateUniqueId("new_entity");
        var folderName = ToPascalFolder(id);
        var directory = Path.Combine(_rootDirectory, "Generated", folderName);
        var suffix = 2;
        while (Directory.Exists(directory))
        {
            directory = Path.Combine(_rootDirectory, "Generated", $"{folderName}{suffix}");
            suffix++;
        }

        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "proto.json");
        var node = new JsonObject
        {
            ["id"] = id,
            ["name"] = "New Entity",
            ["category"] = "entity",
            ["base"] = "base_entity",
            ["components"] = new JsonObject()
        };
        File.WriteAllText(filePath, FormatJson(node));
        _selectedPath = filePath;
        _deleteConfirmPath = null;
        ReloadFromDisk();
        showMessage($"Created prototype '{id}'");
        return true;
    }

    public bool SaveCurrent(Action<string> showMessage)
    {
        if (_selectedPath == null)
        {
            showMessage("Select a prototype first");
            return false;
        }

        if (!TryCommitJsonEditor(showMessage))
            return false;

        ApplyFieldsToDraft();

        var id = ReadString(_draft, "id").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            showMessage("Prototype id cannot be empty");
            return false;
        }

        if (HasDuplicateId(id, _selectedPath))
        {
            showMessage($"Prototype id '{id}' already exists");
            return false;
        }

        try
        {
            File.WriteAllText(_selectedPath, FormatJson(_draft));
            _dirty = false;
            _deleteConfirmPath = null;
            showMessage($"Saved prototype '{id}'");
            ReloadFromDisk();
            SelectByPath(_selectedPath);
            return true;
        }
        catch (Exception e)
        {
            showMessage($"Save failed: {e.Message}");
            return false;
        }
    }

    public bool DeleteCurrent(Action<string> showMessage)
    {
        if (_selectedPath == null)
        {
            showMessage("Select a prototype first");
            return false;
        }

        if (!SamePath(_deleteConfirmPath, _selectedPath))
        {
            _deleteConfirmPath = _selectedPath;
            showMessage("Use Delete Prototype again to confirm");
            return false;
        }

        try
        {
            var deletedPath = _selectedPath;
            File.Delete(deletedPath);
            _deleteConfirmPath = null;
            _selectedPath = null;
            ReloadFromDisk();
            showMessage("Deleted prototype file");
            return true;
        }
        catch (Exception e)
        {
            showMessage($"Delete failed: {e.Message}");
            return false;
        }
    }

    private bool HandleClick(Point point, Action<string> showMessage)
    {
        _deleteConfirmPath = null;

        if (_newButtonRect.Contains(point))
            return CreateNew(showMessage);
        if (_saveButtonRect.Contains(point))
            return SaveCurrent(showMessage);
        if (_reloadButtonRect.Contains(point))
        {
            ReloadFromDisk();
            showMessage("Reloaded prototypes from disk");
            return true;
        }
        if (_deleteButtonRect.Contains(point))
            return DeleteCurrent(showMessage);

        if (!_bounds.Contains(point))
        {
            _focus = FocusField.None;
            return false;
        }

        if (TryHandlePrototypeSelection(point, showMessage))
            return false;

        if (_selectedPath == null)
        {
            _focus = FocusField.None;
            return false;
        }

        var fieldsActive = IsFieldsMode();

        if (_idFieldRect.Contains(point)) { CommitFieldBuffer(); _focus = FocusField.Id; }
        else if (_nameFieldRect.Contains(point)) { CommitFieldBuffer(); _focus = FocusField.Name; }
        else if (_categoryFieldRect.Contains(point)) { CommitFieldBuffer(); _focus = FocusField.Category; }
        else if (_baseFieldRect.Contains(point)) { CommitFieldBuffer(); _focus = FocusField.Base; }
        else if (_newComponentFieldRect.Contains(point)) { CommitFieldBuffer(); _focus = FocusField.NewComponent; }
        else if (!fieldsActive && _jsonContentRect.Contains(point))
        {
            _focus = FocusField.Json;
            SelectJsonLine(point);
        }
        else if (fieldsActive && _jsonContentRect.Contains(point))
        {
            HandleFieldsClick(point);
        }
        else _focus = FocusField.None;

        if (_modeToggleRect.Contains(point) && !_editingRootJson && _selectedComponentId != null)
        {
            ToggleEditMode();
            return false;
        }

        if (_abstractButtonRect.Contains(point))
        {
            _draftAbstract = !_draftAbstract;
            _dirty = true;
            return false;
        }

        if (_rootJsonButtonRect.Contains(point))
        {
            if (TryCommitJsonEditor(showMessage))
                SelectRootJson();
            return false;
        }

        if (_formatJsonButtonRect.Contains(point))
        {
            if (TryCommitJsonEditor(showMessage))
                ReloadJsonEditorFromDraft();
            return false;
        }

        if (_addComponentButtonRect.Contains(point))
        {
            ToggleComponentPicker();
            return false;
        }

        if (_componentPickerOpen && _componentPickerRect.Contains(point))
        {
            HandleComponentPickerClick(point, showMessage);
            return false;
        }

        if (_componentPickerOpen)
            _componentPickerOpen = false;

        if (_removeComponentButtonRect.Contains(point))
        {
            RemoveSelectedComponent(showMessage);
            return false;
        }

        TryHandleComponentSelection(point, showMessage);
        return false;
    }

    private void HandleScroll(Point point, int scrollDelta)
    {
        var direction = Math.Sign(scrollDelta);
        if (_componentPickerOpen && _componentPickerRect.Contains(point))
        {
            _componentPickerScroll = Math.Max(0, _componentPickerScroll - direction);
            return;
        }

        if (_listRect.Contains(point))
        {
            _listScroll = Math.Max(0, _listScroll - direction);
            return;
        }

        if (_componentListRect.Contains(point))
        {
            _componentScroll = Math.Max(0, _componentScroll - direction);
            return;
        }

        if (_jsonContentRect.Contains(point))
        {
            if (IsFieldsMode())
                _fieldsScroll = Math.Max(0, _fieldsScroll - direction);
            else
                _jsonScroll = Math.Max(0, _jsonScroll - direction);
        }
    }

    private bool TryHandlePrototypeSelection(Point point, Action<string> showMessage)
    {
        if (!_listRect.Contains(point))
            return false;

        var y = _listRect.Y + 30;
        foreach (var doc in GetSortedDocuments().Skip(_listScroll))
        {
            if (y + RowHeight > _listRect.Bottom - 8)
                break;

            var row = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, RowHeight - 2);
            if (row.Contains(point))
            {
                if (!TryCommitJsonEditor(showMessage))
                    return true;

                _selectedPath = doc.FilePath;
                LoadSelectedDocument();
                return true;
            }

            y += RowHeight;
        }

        return true;
    }

    private void TryHandleComponentSelection(Point point, Action<string> showMessage)
    {
        if (!_componentListRect.Contains(point))
            return;

        var y = _componentListRect.Y + 6;
        var rootRect = new Rectangle(_componentListRect.X + 6, y, _componentListRect.Width - 12, ComponentRowHeight - 2);
        if (rootRect.Contains(point))
        {
            if (TryCommitJsonEditor(showMessage))
                SelectRootJson();
            return;
        }

        y += ComponentRowHeight;
        foreach (var componentId in GetComponentIds().Skip(_componentScroll))
        {
            if (y + ComponentRowHeight > _componentListRect.Bottom - 6)
                break;

            var row = new Rectangle(_componentListRect.X + 6, y, _componentListRect.Width - 12, ComponentRowHeight - 2);
            if (row.Contains(point))
            {
                if (TryCommitJsonEditor(showMessage))
                    SelectComponent(componentId);
                return;
            }

            y += ComponentRowHeight;
        }
    }

    private void AddComponent(Action<string> showMessage)
        => AddComponentById(_newComponentId, showMessage);

    private void AddComponentById(string componentId, Action<string> showMessage)
    {
        if (_selectedPath == null)
            return;

        if (!TryCommitJsonEditor(showMessage))
            return;

        componentId = (componentId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(componentId))
        {
            showMessage("Component id cannot be empty");
            return;
        }

        var components = GetOrCreateComponents();
        if (components.ContainsKey(componentId))
        {
            showMessage($"Component '{componentId}' already exists");
            SelectComponent(componentId);
            return;
        }

        components[componentId] = new JsonObject();
        _dirty = true;
        SelectComponent(componentId);
        showMessage($"Added component '{componentId}'");
    }

    private void ToggleComponentPicker()
    {
        _componentPickerOpen = !_componentPickerOpen;
        _componentPickerScroll = 0;
        if (_componentPickerOpen)
            _focus = FocusField.NewComponent;
    }

    private void HandleComponentPickerClick(Point point, Action<string> showMessage)
    {
        var rows = GetPickerRows();
        var visibleRows = Math.Max(1, (_componentPickerRect.Height - 12) / ComponentRowHeight);
        var startIdx = _componentPickerScroll;

        for (var i = 0; i < visibleRows; i++)
        {
            var idx = startIdx + i;
            if (idx >= rows.Count)
                break;

            var rowRect = new Rectangle(
                _componentPickerRect.X + 6,
                _componentPickerRect.Y + 6 + i * ComponentRowHeight,
                _componentPickerRect.Width - 12,
                ComponentRowHeight - 2);

            if (rowRect.Contains(point))
            {
                AddComponentById(rows[idx], showMessage);
                _componentPickerOpen = false;
                _newComponentId = "";
                return;
            }
        }
    }

    private List<string> GetPickerRows()
    {
        var existing = GetComponentIds()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filter = (_newComponentId ?? string.Empty).Trim();

        return _registeredComponents
            .Where(id => !existing.Contains(id))
            .Where(id => string.IsNullOrEmpty(filter) || id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RemoveSelectedComponent(Action<string> showMessage)
    {
        if (_selectedComponentId == null || _editingRootJson)
        {
            showMessage("Select a component to remove");
            return;
        }

        var components = GetComponents();
        if (components == null || !components.Remove(_selectedComponentId))
            return;

        var removed = _selectedComponentId;
        _selectedComponentId = null;
        _dirty = true;
        SelectRootJson();
        showMessage($"Removed component '{removed}'");
    }

    private void HandleTextInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (_focus == FocusField.None)
        {
            ResetDeleteRepeat();
            return;
        }

        if (IsPressed(keys, prevKeys, Keys.Escape))
        {
            _focus = FocusField.None;
            ResetDeleteRepeat();
            return;
        }

        if (_focus == FocusField.Json)
        {
            HandleJsonInput(keys, prevKeys);
            return;
        }

        if (_focus == FocusField.ComponentField)
        {
            HandleComponentFieldInput(keys, prevKeys);
            return;
        }

        if (IsPressed(keys, prevKeys, Keys.Tab))
        {
            _focus = _focus switch
            {
                FocusField.Id => FocusField.Name,
                FocusField.Name => FocusField.Category,
                FocusField.Category => FocusField.Base,
                FocusField.Base => FocusField.NewComponent,
                _ => FocusField.Id
            };
            return;
        }

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static key => key))
        {
            if (key is Keys.Back or Keys.Delete)
            {
                if (ShouldRepeatKey(keys, prevKeys, key))
                    DeleteFromFocusedField();

                deleteHandled = true;
                continue;
            }

            if (prevKeys.IsKeyDown(key))
                continue;

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (ch == '\0')
                continue;

            InsertIntoFocusedField(ch);
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private void HandleJsonInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (_jsonLines.Count == 0)
            _jsonLines.Add("");

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static key => key))
        {
            if (key == Keys.Left && IsPressed(keys, prevKeys, key))
            {
                _jsonCaret = Math.Max(0, _jsonCaret - 1);
                continue;
            }

            if (key == Keys.Right && IsPressed(keys, prevKeys, key))
            {
                _jsonCaret = Math.Min(CurrentJsonLine().Length, _jsonCaret + 1);
                continue;
            }

            if (key == Keys.Up && IsPressed(keys, prevKeys, key))
            {
                _jsonLineIndex = Math.Max(0, _jsonLineIndex - 1);
                _jsonCaret = Math.Min(CurrentJsonLine().Length, _jsonCaret);
                EnsureJsonCaretVisible();
                continue;
            }

            if (key == Keys.Down && IsPressed(keys, prevKeys, key))
            {
                _jsonLineIndex = Math.Min(_jsonLines.Count - 1, _jsonLineIndex + 1);
                _jsonCaret = Math.Min(CurrentJsonLine().Length, _jsonCaret);
                EnsureJsonCaretVisible();
                continue;
            }

            if (key == Keys.Home && IsPressed(keys, prevKeys, key))
            {
                _jsonCaret = 0;
                continue;
            }

            if (key == Keys.End && IsPressed(keys, prevKeys, key))
            {
                _jsonCaret = CurrentJsonLine().Length;
                continue;
            }

            if (key == Keys.Enter && IsPressed(keys, prevKeys, key))
            {
                SplitJsonLine();
                continue;
            }

            if (key == Keys.Tab && IsPressed(keys, prevKeys, key))
            {
                InsertJsonText("  ");
                continue;
            }

            if (key is Keys.Back or Keys.Delete)
            {
                if (ShouldRepeatKey(keys, prevKeys, key))
                    DeleteFromJson(key == Keys.Back);

                deleteHandled = true;
                continue;
            }

            if (prevKeys.IsKeyDown(key))
                continue;

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (ch == '\0')
                continue;

            InsertJsonText(ch.ToString());
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private void HandleComponentFieldInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (_focusedFieldKey == null)
        {
            ResetDeleteRepeat();
            return;
        }

        var desc = _fieldDescriptors.FirstOrDefault(d => string.Equals(d.JsonKey, _focusedFieldKey, StringComparison.OrdinalIgnoreCase));
        if (desc == null)
        {
            ResetDeleteRepeat();
            return;
        }

        var t = Nullable.GetUnderlyingType(desc.ClrType) ?? desc.ClrType;
        var allowSpace = t == typeof(string);
        var isNumeric = t == typeof(int) || t == typeof(long) || t == typeof(byte)
                        || t == typeof(float) || t == typeof(double)
                        || t == typeof(Microsoft.Xna.Framework.Vector2);

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static key => key))
        {
            if (key is Keys.Back or Keys.Delete)
            {
                if (ShouldRepeatKey(keys, prevKeys, key))
                {
                    if (_fieldBuffer.Length > 0)
                        _fieldBuffer = _fieldBuffer[..^1];
                }
                deleteHandled = true;
                continue;
            }

            if (prevKeys.IsKeyDown(key))
                continue;

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (ch == '\0')
                continue;

            if (char.IsWhiteSpace(ch) && !allowSpace)
                continue;

            if (isNumeric && !(char.IsDigit(ch) || ch == '-' || ch == '.' || ch == '+'))
                continue;

            _fieldBuffer += ch;
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private void InsertIntoFocusedField(char ch)
    {
        var allowSpace = _focus == FocusField.Name;
        if (char.IsWhiteSpace(ch) && !allowSpace)
            return;

        if (_focus != FocusField.Name && !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.'))
            return;

        switch (_focus)
        {
            case FocusField.Id:
                _draftId += ch;
                break;
            case FocusField.Name:
                _draftName += ch;
                break;
            case FocusField.Category:
                _draftCategory += ch;
                break;
            case FocusField.Base:
                _draftBase += ch;
                break;
            case FocusField.NewComponent:
                _newComponentId += ch;
                break;
        }

        _dirty = true;
    }

    private void DeleteFromFocusedField()
    {
        switch (_focus)
        {
            case FocusField.Id when _draftId.Length > 0:
                _draftId = _draftId[..^1];
                break;
            case FocusField.Name when _draftName.Length > 0:
                _draftName = _draftName[..^1];
                break;
            case FocusField.Category when _draftCategory.Length > 0:
                _draftCategory = _draftCategory[..^1];
                break;
            case FocusField.Base when _draftBase.Length > 0:
                _draftBase = _draftBase[..^1];
                break;
            case FocusField.NewComponent when _newComponentId.Length > 0:
                _newComponentId = _newComponentId[..^1];
                break;
        }

        _dirty = true;
    }

    private void InsertJsonText(string text)
    {
        var line = CurrentJsonLine();
        _jsonLines[_jsonLineIndex] = line.Insert(_jsonCaret, text);
        _jsonCaret += text.Length;
        _dirty = true;
    }

    private void SplitJsonLine()
    {
        var line = CurrentJsonLine();
        var left = line[.._jsonCaret];
        var right = line[_jsonCaret..];
        _jsonLines[_jsonLineIndex] = left;
        _jsonLines.Insert(_jsonLineIndex + 1, right);
        _jsonLineIndex++;
        _jsonCaret = 0;
        _dirty = true;
        EnsureJsonCaretVisible();
    }

    private void DeleteFromJson(bool backspace)
    {
        if (backspace)
        {
            if (_jsonCaret > 0)
            {
                var line = CurrentJsonLine();
                _jsonLines[_jsonLineIndex] = line.Remove(_jsonCaret - 1, 1);
                _jsonCaret--;
            }
            else if (_jsonLineIndex > 0)
            {
                var previousLength = _jsonLines[_jsonLineIndex - 1].Length;
                _jsonLines[_jsonLineIndex - 1] += _jsonLines[_jsonLineIndex];
                _jsonLines.RemoveAt(_jsonLineIndex);
                _jsonLineIndex--;
                _jsonCaret = previousLength;
            }
        }
        else
        {
            var line = CurrentJsonLine();
            if (_jsonCaret < line.Length)
            {
                _jsonLines[_jsonLineIndex] = line.Remove(_jsonCaret, 1);
            }
            else if (_jsonLineIndex < _jsonLines.Count - 1)
            {
                _jsonLines[_jsonLineIndex] += _jsonLines[_jsonLineIndex + 1];
                _jsonLines.RemoveAt(_jsonLineIndex + 1);
            }
        }

        _dirty = true;
        EnsureJsonCaretVisible();
    }

    private void SelectJsonLine(Point point)
    {
        var index = _jsonScroll + Math.Max(0, (point.Y - _jsonContentRect.Y - 4) / JsonLineHeight);
        _jsonLineIndex = Math.Clamp(index, 0, Math.Max(0, _jsonLines.Count - 1));
        var text = CurrentJsonLine();
        var relativeX = Math.Max(0, point.X - _jsonContentRect.X - 42);
        var caret = 0;
        while (caret < text.Length && EditorTheme.Small.MeasureString(text[..caret]).X < relativeX)
            caret++;
        _jsonCaret = Math.Clamp(caret, 0, text.Length);
    }

    private string CurrentJsonLine()
    {
        if (_jsonLines.Count == 0)
            _jsonLines.Add("");

        _jsonLineIndex = Math.Clamp(_jsonLineIndex, 0, _jsonLines.Count - 1);
        return _jsonLines[_jsonLineIndex];
    }

    private bool ShouldRepeatKey(KeyboardState keys, KeyboardState prevKeys, Keys key)
    {
        var isDown = keys.IsKeyDown(key);
        if (!isDown)
        {
            if (_heldDeleteKey == key)
                ResetDeleteRepeat();
            return false;
        }

        var now = Environment.TickCount64;
        if (prevKeys.IsKeyUp(key) || _heldDeleteKey != key)
        {
            _heldDeleteKey = key;
            _nextDeleteRepeatAt = now + DeleteRepeatInitialDelayMs;
            return true;
        }

        if (now < _nextDeleteRepeatAt)
            return false;

        _nextDeleteRepeatAt = now + DeleteRepeatIntervalMs;
        return true;
    }

    private void ResetDeleteRepeat()
    {
        _heldDeleteKey = null;
        _nextDeleteRepeatAt = 0;
    }

    private void DrawHeaderButtons(SpriteBatch spriteBatch)
    {
        var mouse = Mouse.GetState().Position;
        DrawButton(spriteBatch, _newButtonRect, "New", _newButtonRect.Contains(mouse), false);
        DrawButton(spriteBatch, _saveButtonRect, _dirty ? "Save*" : "Save", _saveButtonRect.Contains(mouse), _dirty);
        DrawButton(spriteBatch, _reloadButtonRect, "Reload", _reloadButtonRect.Contains(mouse), false);
        DrawButton(spriteBatch, _deleteButtonRect, "Delete", _deleteButtonRect.Contains(mouse), SamePath(_deleteConfirmPath, _selectedPath), _selectedPath != null);
    }

    private void DrawPrototypeList(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _listRect, EditorTheme.PanelAlt, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Prototypes: {_documents.Count}",
            new Vector2(_listRect.X + 10, _listRect.Y + 8), EditorTheme.TextDim);

        var y = _listRect.Y + 30;
        foreach (var doc in GetSortedDocuments().Skip(_listScroll))
        {
            if (y + RowHeight > _listRect.Bottom - 8)
                break;

            var row = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, RowHeight - 2);
            var selected = SamePath(doc.FilePath, _selectedPath);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.Accent : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, row, selected ? EditorTheme.AccentDim : EditorTheme.Border);

            var title = string.IsNullOrWhiteSpace(doc.Name) ? doc.Id : doc.Name;
            if (doc.Abstract)
                title += " (abstract)";
            var color = selected ? Color.White : doc.Abstract ? EditorTheme.Warning : EditorTheme.Text;
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                Truncate(title, EditorTheme.Small, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 5), color);

            var subtitle = $"{doc.Id}  [{doc.Category}]";
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                Truncate(subtitle, EditorTheme.Tiny, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 18), selected ? new Color(225, 235, 255) : EditorTheme.TextMuted);
            y += RowHeight;
        }
    }

    private void DrawDetails(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _detailRect, EditorTheme.PanelAlt, EditorTheme.Border);

        if (_selectedPath == null)
        {
            DrawEmptyState(spriteBatch);
            return;
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(RelativePath(_selectedPath), EditorTheme.Tiny, _detailRect.Width - 24),
            new Vector2(_detailRect.X + 12, _detailRect.Y + 10), EditorTheme.TextMuted);

        DrawLabeledField(spriteBatch, "ID", _idFieldRect, _draftId, _focus == FocusField.Id);
        DrawLabeledField(spriteBatch, "Name", _nameFieldRect, _draftName, _focus == FocusField.Name);
        DrawLabeledField(spriteBatch, "Category", _categoryFieldRect, _draftCategory, _focus == FocusField.Category);
        DrawLabeledField(spriteBatch, "Base", _baseFieldRect, _draftBase, _focus == FocusField.Base);

        DrawButton(spriteBatch, _abstractButtonRect, _draftAbstract ? "Abstract: Yes" : "Abstract: No",
            _abstractButtonRect.Contains(Mouse.GetState().Position), _draftAbstract);

        DrawComponentPanel(spriteBatch);
        DrawJsonPanel(spriteBatch);

        if (!string.IsNullOrWhiteSpace(_error))
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(_error, EditorTheme.Small, _detailRect.Width - 24),
                new Vector2(_detailRect.X + 12, _detailRect.Bottom - 20), EditorTheme.Error);
    }

    private void DrawComponentPanel(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _componentRect, EditorTheme.BgDeep, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Components",
            new Vector2(_componentRect.X + 10, _componentRect.Y + 8), EditorTheme.TextDim);

        var filterPlaceholder = _componentPickerOpen && string.IsNullOrEmpty(_newComponentId);
        DrawField(spriteBatch, _newComponentFieldRect,
            filterPlaceholder ? "filter..." : _newComponentId,
            _focus == FocusField.NewComponent);
        DrawButton(spriteBatch, _addComponentButtonRect, _componentPickerOpen ? "Close" : "Add...",
            _addComponentButtonRect.Contains(Mouse.GetState().Position), _componentPickerOpen);
        DrawButton(spriteBatch, _removeComponentButtonRect, "Remove", _removeComponentButtonRect.Contains(Mouse.GetState().Position), false, _selectedComponentId != null && !_editingRootJson);

        var y = _componentListRect.Y + 6;
        DrawComponentRow(spriteBatch, new Rectangle(_componentListRect.X + 6, y, _componentListRect.Width - 12, ComponentRowHeight - 2),
            "Root JSON", _editingRootJson, EditorTheme.Warning);
        y += ComponentRowHeight;

        foreach (var componentId in GetComponentIds().Skip(_componentScroll))
        {
            if (y + ComponentRowHeight > _componentListRect.Bottom - 6)
                break;

            var selected = !_editingRootJson && string.Equals(componentId, _selectedComponentId, StringComparison.OrdinalIgnoreCase);
            DrawComponentRow(spriteBatch, new Rectangle(_componentListRect.X + 6, y, _componentListRect.Width - 12, ComponentRowHeight - 2),
                componentId, selected, _registeredComponents.Contains(componentId, StringComparer.OrdinalIgnoreCase) ? EditorTheme.Text : EditorTheme.Warning);
            y += ComponentRowHeight;
        }
    }

    private void DrawComponentPicker(SpriteBatch spriteBatch)
    {
        if (!_componentPickerOpen)
            return;

        EditorTheme.DrawPanel(spriteBatch, _componentPickerRect, EditorTheme.Bg, EditorTheme.Accent);

        var rows = GetPickerRows();
        var visibleRows = Math.Max(1, (_componentPickerRect.Height - 12) / ComponentRowHeight);
        _componentPickerScroll = Math.Clamp(_componentPickerScroll, 0, Math.Max(0, rows.Count - visibleRows));

        if (rows.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "No matching components",
                new Vector2(_componentPickerRect.X + 12, _componentPickerRect.Y + 12), EditorTheme.TextMuted);
            return;
        }

        var mouse = Mouse.GetState().Position;
        for (var i = 0; i < visibleRows; i++)
        {
            var idx = _componentPickerScroll + i;
            if (idx >= rows.Count)
                break;

            var rect = new Rectangle(
                _componentPickerRect.X + 6,
                _componentPickerRect.Y + 6 + i * ComponentRowHeight,
                _componentPickerRect.Width - 12,
                ComponentRowHeight - 2);

            var hovered = rect.Contains(mouse);
            EditorTheme.FillRect(spriteBatch, rect, hovered ? EditorTheme.AccentDim : EditorTheme.Panel);
            EditorTheme.DrawBorder(spriteBatch, rect, hovered ? EditorTheme.Accent : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                Truncate(rows[idx], EditorTheme.Small, rect.Width - 12),
                new Vector2(rect.X + 7, rect.Y + 5), hovered ? Color.White : EditorTheme.Text);
        }
    }

    private void DrawComponentRow(SpriteBatch spriteBatch, Rectangle row, string label, bool selected, Color textColor)
    {
        EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.Accent : EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, row, selected ? EditorTheme.AccentDim : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(label, EditorTheme.Small, row.Width - 12),
            new Vector2(row.X + 7, row.Y + 5), selected ? Color.White : textColor);
    }

    private bool IsFieldsMode()
        => !_editingRootJson && _selectedComponentId != null && _editMode == ComponentEditMode.Fields;

    private void ToggleEditMode()
    {
        if (_editMode == ComponentEditMode.Fields)
        {
            _editMode = ComponentEditMode.Raw;
            ReloadJsonEditorFromDraft();
        }
        else
        {
            CommitFieldBuffer();
            _editMode = ComponentEditMode.Fields;
            ClearFieldFocus();
        }
    }

    private JsonObject? GetSelectedComponentJson()
    {
        if (_editingRootJson || _selectedComponentId == null)
            return null;

        var components = GetComponents();
        if (components == null)
            return null;

        if (components[_selectedComponentId] is JsonObject obj)
            return obj;

        var fresh = new JsonObject();
        components[_selectedComponentId] = fresh;
        return fresh;
    }

    private void DrawJsonPanel(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _jsonPanelRect, EditorTheme.BgDeep, EditorTheme.Border);
        var title = _editingRootJson
            ? "Root JSON"
            : _selectedComponentId == null ? "Component JSON" : $"Component: {_selectedComponentId}";
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(title, EditorTheme.Small, _jsonPanelRect.Width - 280),
            new Vector2(_jsonPanelRect.X + 10, _jsonPanelRect.Y + 8), EditorTheme.TextDim);

        var modeAvailable = !_editingRootJson && _selectedComponentId != null;
        var modeLabel = IsFieldsMode() ? "Mode: Fields" : "Mode: Raw";
        DrawButton(spriteBatch, _modeToggleRect, modeLabel, _modeToggleRect.Contains(Mouse.GetState().Position), IsFieldsMode(), modeAvailable);

        DrawButton(spriteBatch, _rootJsonButtonRect, "Root", _rootJsonButtonRect.Contains(Mouse.GetState().Position), _editingRootJson);
        DrawButton(spriteBatch, _formatJsonButtonRect, "Format", _formatJsonButtonRect.Contains(Mouse.GetState().Position), false, !IsFieldsMode());

        if (IsFieldsMode())
        {
            DrawFieldsContent(spriteBatch);
            return;
        }

        EditorTheme.FillRect(spriteBatch, _jsonContentRect, EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, _jsonContentRect, _focus == FocusField.Json ? EditorTheme.Accent : EditorTheme.Border);

        var visibleRows = Math.Max(1, (_jsonContentRect.Height - 8) / JsonLineHeight);
        _jsonScroll = Math.Clamp(_jsonScroll, 0, Math.Max(0, _jsonLines.Count - visibleRows));

        var y = _jsonContentRect.Y + 4;
        for (var i = 0; i < visibleRows; i++)
        {
            var lineIndex = _jsonScroll + i;
            if (lineIndex >= _jsonLines.Count)
                break;

            var active = _focus == FocusField.Json && lineIndex == _jsonLineIndex;
            var rowRect = new Rectangle(_jsonContentRect.X + 4, y - 1, _jsonContentRect.Width - 8, JsonLineHeight);
            if (active)
                EditorTheme.FillRect(spriteBatch, rowRect, EditorTheme.PanelAlt);

            var numberText = (lineIndex + 1).ToString();
            var numberSize = EditorTheme.Tiny.MeasureString(numberText);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, numberText,
                new Vector2(_jsonContentRect.X + 34 - numberSize.X, y + 2), EditorTheme.TextMuted);

            var text = _jsonLines[lineIndex];
            if (active)
                text = text.Insert(Math.Clamp(_jsonCaret, 0, text.Length), "|");
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                Truncate(text, EditorTheme.Small, _jsonContentRect.Width - 52),
                new Vector2(_jsonContentRect.X + 42, y), active ? EditorTheme.Text : EditorTheme.TextDim);
            y += JsonLineHeight;
        }
    }

    private void DrawFieldsContent(SpriteBatch spriteBatch)
    {
        EditorTheme.FillRect(spriteBatch, _jsonContentRect, EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, _jsonContentRect, EditorTheme.Border);
        _fieldHitRects.Clear();

        var component = GetSelectedComponentJson();
        if (component == null)
            return;

        if (_fieldDescriptors.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                $"Component '{_selectedComponentId}' is not registered or has no [DataField] members.",
                new Vector2(_jsonContentRect.X + 10, _jsonContentRect.Y + 10), EditorTheme.TextMuted);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                "Switch to Mode: Raw to edit JSON directly.",
                new Vector2(_jsonContentRect.X + 10, _jsonContentRect.Y + 32), EditorTheme.TextMuted);
            return;
        }

        var visibleRows = Math.Max(1, (_jsonContentRect.Height - 12) / FieldRowHeight);
        _fieldsScroll = Math.Clamp(_fieldsScroll, 0, Math.Max(0, _fieldDescriptors.Count - visibleRows));

        var labelW = (int)(_jsonContentRect.Width * 0.32f);
        var inputX = _jsonContentRect.X + 16 + labelW + 8;
        var inputW = _jsonContentRect.Right - inputX - 12;
        var y = _jsonContentRect.Y + 6;

        for (var i = 0; i < visibleRows; i++)
        {
            var idx = _fieldsScroll + i;
            if (idx >= _fieldDescriptors.Count)
                break;

            var desc = _fieldDescriptors[idx];

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                Truncate(desc.Label, EditorTheme.Small, labelW),
                new Vector2(_jsonContentRect.X + 14, y + 7), EditorTheme.TextDim);

            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                Truncate(GetTypeShortLabel(desc.ClrType), EditorTheme.Tiny, labelW),
                new Vector2(_jsonContentRect.X + 14, y + 22), EditorTheme.TextMuted);

            var inputRect = new Rectangle(inputX, y + 2, inputW, FieldRowHeight - 8);
            DrawFieldInput(spriteBatch, desc, inputRect, component);
            y += FieldRowHeight;
        }
    }

    private void DrawFieldInput(SpriteBatch spriteBatch, FieldDescriptor desc, Rectangle rect, JsonObject component)
    {
        var t = Nullable.GetUnderlyingType(desc.ClrType) ?? desc.ClrType;
        var node = component[desc.JsonKey];
        var mouse = Mouse.GetState().Position;
        var focused = _focus == FocusField.ComponentField && _focusedFieldKey == desc.JsonKey;

        if (t == typeof(bool))
        {
            var val = TryReadBool(node);
            var btn = new Rectangle(rect.X, rect.Y, 80, rect.Height);
            DrawButton(spriteBatch, btn, val ? "true" : "false", btn.Contains(mouse), val);
            _fieldHitRects.Add((desc, btn, -1));
        }
        else if (t.IsEnum)
        {
            var name = TryReadEnumName(node, t);
            var btn = rect;
            DrawButton(spriteBatch, btn, name + "  >", btn.Contains(mouse), false);
            _fieldHitRects.Add((desc, btn, -1));
        }
        else if (t == typeof(Microsoft.Xna.Framework.Vector2))
        {
            var x = node is JsonObject o1 ? TryReadFloat(o1["x"]) : 0f;
            var y = node is JsonObject o2 ? TryReadFloat(o2["y"]) : 0f;
            var halfW = (rect.Width - 8) / 2;
            var rx = new Rectangle(rect.X, rect.Y, halfW, rect.Height);
            var ry = new Rectangle(rect.X + halfW + 8, rect.Y, halfW, rect.Height);
            var fx = focused && _focusedSubIndex == 0;
            var fy = focused && _focusedSubIndex == 1;
            DrawField(spriteBatch, rx, fx ? _fieldBuffer : x.ToString(CultureInfo.InvariantCulture), fx);
            DrawField(spriteBatch, ry, fy ? _fieldBuffer : y.ToString(CultureInfo.InvariantCulture), fy);
            _fieldHitRects.Add((desc, rx, 0));
            _fieldHitRects.Add((desc, ry, 1));
        }
        else if (IsTextEditableType(t))
        {
            var current = focused ? _fieldBuffer : NodeToFieldText(node, t);
            DrawField(spriteBatch, rect, current, focused);
            _fieldHitRects.Add((desc, rect, -1));
        }
        else
        {
            EditorTheme.FillRect(spriteBatch, rect, EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, rect, EditorTheme.BorderSoft);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
                Truncate("[edit in Raw mode]", EditorTheme.Tiny, rect.Width - 12),
                new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.TextMuted);
        }
    }

    private void HandleFieldsClick(Point point)
    {
        var component = GetSelectedComponentJson();
        if (component == null)
            return;

        foreach (var (desc, inputRect, subIndex) in _fieldHitRects)
        {
            if (!inputRect.Contains(point))
                continue;

            var t = Nullable.GetUnderlyingType(desc.ClrType) ?? desc.ClrType;

            if (t == typeof(bool))
            {
                var val = TryReadBool(component[desc.JsonKey]);
                component[desc.JsonKey] = JsonValue.Create(!val);
                CommitFieldBuffer();
                ClearFieldFocus();
                _focus = FocusField.None;
                _dirty = true;
                return;
            }

            if (t.IsEnum)
            {
                var current = TryReadEnumName(component[desc.JsonKey], t);
                var names = Enum.GetNames(t);
                var idx = Array.IndexOf(names, current);
                var nextIdx = (idx + 1) % names.Length;
                component[desc.JsonKey] = JsonValue.Create(names[nextIdx]);
                CommitFieldBuffer();
                ClearFieldFocus();
                _focus = FocusField.None;
                _dirty = true;
                return;
            }

            CommitFieldBuffer();
            _focus = FocusField.ComponentField;
            _focusedFieldKey = desc.JsonKey;
            _focusedSubIndex = subIndex;
            _fieldBuffer = ReadCurrentFieldText(desc, subIndex, component);
            return;
        }

        ClearFieldFocus();
        _focus = FocusField.None;
    }

    private string ReadCurrentFieldText(FieldDescriptor desc, int subIndex, JsonObject component)
    {
        var t = Nullable.GetUnderlyingType(desc.ClrType) ?? desc.ClrType;
        var node = component[desc.JsonKey];

        if (t == typeof(Microsoft.Xna.Framework.Vector2))
        {
            if (node is not JsonObject vec)
                return "0";
            var v = subIndex == 0 ? TryReadFloat(vec["x"]) : TryReadFloat(vec["y"]);
            return v.ToString(CultureInfo.InvariantCulture);
        }

        return NodeToFieldText(node, t);
    }

    private void CommitFieldBuffer()
    {
        if (_focus != FocusField.ComponentField || _focusedFieldKey == null)
            return;

        var component = GetSelectedComponentJson();
        if (component == null)
        {
            ClearFieldFocus();
            return;
        }

        var desc = _fieldDescriptors.FirstOrDefault(d => string.Equals(d.JsonKey, _focusedFieldKey, StringComparison.OrdinalIgnoreCase));
        if (desc == null)
        {
            ClearFieldFocus();
            return;
        }

        var t = Nullable.GetUnderlyingType(desc.ClrType) ?? desc.ClrType;

        try
        {
            if (t == typeof(string))
            {
                component[desc.JsonKey] = JsonValue.Create(_fieldBuffer);
            }
            else if (t == typeof(int) || t == typeof(long) || t == typeof(byte))
            {
                if (long.TryParse(_fieldBuffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
                    component[desc.JsonKey] = JsonValue.Create(num);
            }
            else if (t == typeof(float) || t == typeof(double))
            {
                if (double.TryParse(_fieldBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    component[desc.JsonKey] = JsonValue.Create(num);
            }
            else if (t == typeof(Microsoft.Xna.Framework.Vector2))
            {
                if (component[desc.JsonKey] is not JsonObject vec)
                {
                    vec = new JsonObject { ["x"] = 0f, ["y"] = 0f };
                    component[desc.JsonKey] = vec;
                }
                if (float.TryParse(_fieldBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out var fnum))
                    vec[_focusedSubIndex == 0 ? "x" : "y"] = JsonValue.Create(fnum);
            }
            _dirty = true;
        }
        catch
        {
            // ignore parse errors; buffer kept on user side
        }

        ClearFieldFocus();
    }

    private static bool IsTextEditableType(Type t)
        => t == typeof(string)
           || t == typeof(int) || t == typeof(long) || t == typeof(byte)
           || t == typeof(float) || t == typeof(double);

    private static string GetTypeShortLabel(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return "string";
        if (u == typeof(int) || u == typeof(long) || u == typeof(byte)) return "int";
        if (u == typeof(float) || u == typeof(double)) return "float";
        if (u == typeof(bool)) return "bool";
        if (u == typeof(Microsoft.Xna.Framework.Vector2)) return "vec2";
        if (u.IsEnum) return u.Name;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(u) && u != typeof(string)) return "list";
        return u.Name;
    }

    private static string NodeToFieldText(JsonNode? node, Type t)
    {
        if (node == null) return "";
        try
        {
            if (t == typeof(string)) return node.GetValue<string>();
            if (t == typeof(int) || t == typeof(long) || t == typeof(byte))
                return node.GetValue<long>().ToString(CultureInfo.InvariantCulture);
            if (t == typeof(float) || t == typeof(double))
                return node.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            // fall through to raw text
        }
        return node.ToJsonString().Trim('"');
    }

    private static bool TryReadBool(JsonNode? node)
    {
        if (node == null) return false;
        try { return node.GetValue<bool>(); }
        catch { return false; }
    }

    private static float TryReadFloat(JsonNode? node)
    {
        if (node == null) return 0f;
        try { return (float)node.GetValue<double>(); }
        catch { return 0f; }
    }

    private static string TryReadEnumName(JsonNode? node, Type enumType)
    {
        if (node == null) return Enum.GetNames(enumType).FirstOrDefault() ?? "";
        try
        {
            var s = node.GetValue<string>();
            if (Enum.GetNames(enumType).Any(n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase)))
                return Enum.GetNames(enumType).First(n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
            return s;
        }
        catch
        {
            try
            {
                var num = node.GetValue<int>();
                return Enum.GetName(enumType, num) ?? num.ToString();
            }
            catch
            {
                return Enum.GetNames(enumType).FirstOrDefault() ?? "";
            }
        }
    }

    private void DrawEmptyState(SpriteBatch spriteBatch)
    {
        var box = new Rectangle(_detailRect.X + 24, _detailRect.Y + 24, _detailRect.Width - 48, 120);
        EditorTheme.FillRect(spriteBatch, box, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, box, EditorTheme.BorderSoft);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(box.X, box.Y, 3, box.Height), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Title, "No prototype selected",
            new Vector2(box.X + 16, box.Y + 22), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, "Use Prototype -> New Prototype or select a file from the list.",
            new Vector2(box.X + 16, box.Y + 56), EditorTheme.TextDim);
    }

    private void DrawLabeledField(SpriteBatch spriteBatch, string label, Rectangle rect, string text, bool focused)
    {
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label,
            new Vector2(rect.X, rect.Y - 15), EditorTheme.TextDim);
        DrawField(spriteBatch, rect, text, focused);
    }

    private static void DrawField(SpriteBatch spriteBatch, Rectangle rect, string text, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, focused ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Accent : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body,
            Truncate(text + (focused ? "|" : ""), EditorTheme.Body, rect.Width - 12),
            new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
    }

    private static void DrawButton(SpriteBatch spriteBatch, Rectangle rect, string label, bool hovered, bool active, bool enabled = true)
        => EditorTheme.DrawButton(spriteBatch, rect, label, EditorTheme.Small, hovered, active, enabled);

    private void RebuildLayout()
    {
        var viewport = _graphics.Viewport;
        _bounds = new Rectangle(12, 68, viewport.Width - 24, viewport.Height - 102);
        _headerRect = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 30);

        var buttonY = _headerRect.Y + 4;
        _deleteButtonRect = new Rectangle(_headerRect.Right - 76, buttonY, 64, 22);
        _reloadButtonRect = new Rectangle(_deleteButtonRect.X - 72, buttonY, 66, 22);
        _saveButtonRect = new Rectangle(_reloadButtonRect.X - 64, buttonY, 58, 22);
        _newButtonRect = new Rectangle(_saveButtonRect.X - 58, buttonY, 52, 22);

        _listRect = new Rectangle(_bounds.X + 12, _bounds.Y + 42, 320, _bounds.Height - 54);
        _detailRect = new Rectangle(_listRect.Right + 12, _bounds.Y + 42, _bounds.Right - _listRect.Right - 24, _bounds.Height - 54);

        var fieldTop = _detailRect.Y + 34;
        var fieldGap = 12;
        var fieldHeight = 24;
        var columnWidth = Math.Max(160, (_detailRect.Width - 48) / 2);
        _idFieldRect = new Rectangle(_detailRect.X + 12, fieldTop, columnWidth, fieldHeight);
        _nameFieldRect = new Rectangle(_idFieldRect.Right + fieldGap, fieldTop, _detailRect.Right - _idFieldRect.Right - fieldGap - 12, fieldHeight);
        _categoryFieldRect = new Rectangle(_detailRect.X + 12, fieldTop + 46, 160, fieldHeight);
        _baseFieldRect = new Rectangle(_categoryFieldRect.Right + fieldGap, fieldTop + 46, Math.Max(160, _detailRect.Right - _categoryFieldRect.Right - fieldGap - 148), fieldHeight);
        _abstractButtonRect = new Rectangle(_detailRect.Right - 126, fieldTop + 46, 114, fieldHeight);

        _componentRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 126, 260, _detailRect.Height - 154);
        _jsonPanelRect = new Rectangle(_componentRect.Right + 12, _componentRect.Y, _detailRect.Right - _componentRect.Right - 24, _componentRect.Height);

        _newComponentFieldRect = new Rectangle(_componentRect.X + 10, _componentRect.Y + 32, _componentRect.Width - 92, 22);
        _addComponentButtonRect = new Rectangle(_newComponentFieldRect.Right + 6, _newComponentFieldRect.Y, 66, 22);
        _removeComponentButtonRect = new Rectangle(_componentRect.X + 10, _componentRect.Bottom - 42, _componentRect.Width - 20, 22);
        _componentListRect = new Rectangle(_componentRect.X + 10, _componentRect.Y + 62, _componentRect.Width - 20, _removeComponentButtonRect.Y - _componentRect.Y - 70);

        var pickerWidth = Math.Max(220, _componentRect.Width + 80);
        var pickerHeight = Math.Min(360, _bounds.Bottom - _addComponentButtonRect.Bottom - 24);
        _componentPickerRect = new Rectangle(
            _componentRect.X,
            _addComponentButtonRect.Bottom + 4,
            pickerWidth,
            Math.Max(120, pickerHeight));

        _rootJsonButtonRect = new Rectangle(_jsonPanelRect.Right - 124, _jsonPanelRect.Y + 5, 54, 22);
        _formatJsonButtonRect = new Rectangle(_jsonPanelRect.Right - 66, _jsonPanelRect.Y + 5, 56, 22);
        _modeToggleRect = new Rectangle(_rootJsonButtonRect.X - 92, _jsonPanelRect.Y + 5, 86, 22);
        _jsonContentRect = new Rectangle(_jsonPanelRect.X + 10, _jsonPanelRect.Y + 34, _jsonPanelRect.Width - 20, _jsonPanelRect.Height - 44);
    }

    private void LoadSelectedDocument()
    {
        if (_selectedPath == null)
            return;

        var doc = _documents.FirstOrDefault(document => SamePath(document.FilePath, _selectedPath));
        if (doc == null)
        {
            _selectedPath = _documents.FirstOrDefault()?.FilePath;
            doc = _selectedPath == null ? null : _documents.FirstOrDefault(document => SamePath(document.FilePath, _selectedPath));
        }

        if (doc == null)
            return;

        _draft = CloneObject(doc.Node);
        SyncFieldsFromDraft();
        _selectedComponentId = GetComponentIds().FirstOrDefault();
        if (_selectedComponentId != null)
            SelectComponent(_selectedComponentId);
        else
            SelectRootJson();
        _dirty = false;
        _focus = FocusField.None;
    }

    private void SelectByPath(string path)
    {
        _selectedPath = path;
        LoadSelectedDocument();
    }

    private void SyncFieldsFromDraft()
    {
        _draftId = ReadString(_draft, "id");
        _draftName = ReadString(_draft, "name");
        _draftCategory = string.IsNullOrWhiteSpace(ReadString(_draft, "category")) ? "entity" : ReadString(_draft, "category");
        _draftBase = ReadString(_draft, "base");
        _draftAbstract = ReadBool(_draft, "abstract");
    }

    private void ApplyFieldsToDraft()
    {
        SetString(_draft, "id", _draftId.Trim());
        SetString(_draft, "name", string.IsNullOrWhiteSpace(_draftName) ? _draftId.Trim() : _draftName.Trim());
        SetString(_draft, "category", string.IsNullOrWhiteSpace(_draftCategory) ? "entity" : _draftCategory.Trim());
        SetString(_draft, "base", _draftBase.Trim(), removeWhenEmpty: true);
        if (_draftAbstract)
            _draft["abstract"] = true;
        else
            _draft.Remove("abstract");
    }

    private void SelectRootJson()
    {
        _editingRootJson = true;
        _selectedComponentId = null;
        _fieldDescriptors.Clear();
        ClearFieldFocus();
        ReloadJsonEditorFromDraft();
    }

    private void SelectComponent(string componentId)
    {
        _editingRootJson = false;
        _selectedComponentId = componentId;
        RebuildFieldDescriptors();
        ClearFieldFocus();
        ReloadJsonEditorFromDraft();
    }

    private void RebuildFieldDescriptors()
    {
        _fieldDescriptors.Clear();
        if (_selectedComponentId == null)
            return;

        var type = ComponentRegistry.GetComponentType(_selectedComponentId);
        if (type == null)
            return;

        foreach (var (key, clrType) in EnumerateDataFields(type))
        {
            _fieldDescriptors.Add(new FieldDescriptor
            {
                JsonKey = key,
                ClrType = clrType,
                Label = key
            });
        }
    }

    private static IEnumerable<(string key, Type clrType)> EnumerateDataFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var current = type;
        while (current != null && current != typeof(object))
        {
            foreach (var prop in current.GetProperties(flags))
            {
                if (!prop.CanRead || !prop.CanWrite)
                    continue;

                var attr = prop.GetCustomAttribute<DataFieldAttribute>();
                if (attr == null)
                    continue;

                var key = attr.Name ?? prop.Name;
                if (seen.Add(key))
                    yield return (key, prop.PropertyType);
            }

            foreach (var field in current.GetFields(flags))
            {
                var attr = field.GetCustomAttribute<DataFieldAttribute>();
                if (attr == null)
                    continue;

                var key = attr.Name ?? field.Name;
                if (seen.Add(key))
                    yield return (key, field.FieldType);
            }

            current = current.BaseType;
        }
    }

    private void ClearFieldFocus()
    {
        _focusedFieldKey = null;
        _focusedSubIndex = -1;
        _fieldBuffer = "";
        if (_focus == FocusField.ComponentField)
            _focus = FocusField.None;
    }

    private void ReloadJsonEditorFromDraft()
    {
        JsonNode node;
        if (_editingRootJson)
        {
            ApplyFieldsToDraft();
            node = _draft;
        }
        else
        {
            var components = GetComponents();
            node = _selectedComponentId != null && components?[_selectedComponentId] != null
                ? components[_selectedComponentId]!
                : new JsonObject();
        }

        _jsonLines = FormatJson(node).Replace("\r\n", "\n").Split('\n').ToList();
        if (_jsonLines.Count == 0)
            _jsonLines.Add("");
        _jsonLineIndex = 0;
        _jsonCaret = 0;
        _jsonScroll = 0;
        _error = "";
    }

    private bool TryCommitJsonEditor(Action<string> showMessage)
    {
        if (_selectedPath == null)
            return true;

        if (IsFieldsMode())
        {
            CommitFieldBuffer();
            return true;
        }

        try
        {
            var text = string.Join('\n', _jsonLines);
            var parsed = JsonNode.Parse(text);
            if (parsed == null)
                throw new InvalidOperationException("JSON is empty");

            if (_editingRootJson)
            {
                if (parsed is not JsonObject root)
                    throw new InvalidOperationException("Root JSON must be an object");

                _draft = CloneObject(root);
                SyncFieldsFromDraft();
            }
            else if (_selectedComponentId != null)
            {
                if (parsed is not JsonObject component)
                    throw new InvalidOperationException("Component JSON must be an object");

                GetOrCreateComponents()[_selectedComponentId] = CloneNode(component);
            }

            _error = "";
            return true;
        }
        catch (Exception e)
        {
            _error = $"JSON error: {e.Message}";
            showMessage(_error);
            _focus = FocusField.Json;
            return false;
        }
    }

    private JsonObject? GetComponents()
        => _draft["components"] as JsonObject;

    private JsonObject GetOrCreateComponents()
    {
        if (_draft["components"] is JsonObject components)
            return components;

        components = new JsonObject();
        _draft["components"] = components;
        return components;
    }

    private List<string> GetComponentIds()
    {
        var components = GetComponents();
        if (components == null)
            return new List<string>();

        return components.Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<PrototypeDocument> GetSortedDocuments()
        => _documents
            .OrderBy(document => document.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase);

    private bool HasDuplicateId(string id, string currentPath)
        => _documents.Any(doc =>
            !SamePath(doc.FilePath, currentPath)
            && string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase));

    private string GenerateUniqueId(string seed)
    {
        var candidate = seed;
        var index = 2;
        while (_documents.Any(doc => string.Equals(doc.Id, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{seed}_{index}";
            index++;
        }

        return candidate;
    }

    private void EnsureJsonCaretVisible()
    {
        var visibleRows = Math.Max(1, (_jsonContentRect.Height - 8) / JsonLineHeight);
        if (_jsonLineIndex < _jsonScroll)
            _jsonScroll = _jsonLineIndex;
        else if (_jsonLineIndex >= _jsonScroll + visibleRows)
            _jsonScroll = _jsonLineIndex - visibleRows + 1;
    }

    private string RelativePath(string path)
        => Path.GetRelativePath(_rootDirectory, path);

    private static string FormatJson(JsonNode node)
        => node.ToJsonString(JsonOptions) + Environment.NewLine;

    private static JsonObject CloneObject(JsonObject source)
        => CloneNode(source).AsObject();

    private static JsonNode CloneNode(JsonNode source)
        => JsonNode.Parse(source.ToJsonString()) ?? new JsonObject();

    private static string ReadString(JsonObject node, string key)
    {
        try
        {
            return node[key]?.GetValue<string>() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool ReadBool(JsonObject node, string key)
    {
        try
        {
            return node[key]?.GetValue<bool>() ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static void SetString(JsonObject node, string key, string value, bool removeWhenEmpty = false)
    {
        if (removeWhenEmpty && string.IsNullOrWhiteSpace(value))
            node.Remove(key);
        else
            node[key] = value;
    }

    private static bool SamePath(string? left, string? right)
    {
        if (left == null || right == null)
            return false;

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string ToPascalFolder(string id)
    {
        var parts = id.Split(new[] { '_', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var value = string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return string.IsNullOrWhiteSpace(value) ? "Prototype" : value;
    }

    private static string Truncate(string text, FontStashSharp.SpriteFontBase font, int maxWidth)
    {
        if (maxWidth <= 0 || font.MeasureString(text).X <= maxWidth)
            return text;

        const string suffix = "...";
        var result = text;
        while (result.Length > 0 && font.MeasureString(result + suffix).X > maxWidth)
            result = result[..^1];
        return result + suffix;
    }

    private static bool IsPressed(KeyboardState keys, KeyboardState prevKeys, Keys key)
        => keys.IsKeyDown(key) && prevKeys.IsKeyUp(key);

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            var digit = (char)('0' + (key - Keys.D0));
            if (!shift)
                return digit;

            return digit switch
            {
                '1' => '!',
                '2' => '@',
                '3' => '#',
                '4' => '$',
                '5' => '%',
                '6' => '^',
                '7' => '&',
                '8' => '*',
                '9' => '(',
                '0' => ')',
                _ => digit
            };
        }

        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPlus => shift ? '+' : '=',
            Keys.OemOpenBrackets => shift ? '{' : '[',
            Keys.OemCloseBrackets => shift ? '}' : ']',
            Keys.OemPipe => shift ? '|' : '\\',
            Keys.OemSemicolon => shift ? ':' : ';',
            Keys.OemQuotes => shift ? '"' : '\'',
            Keys.OemComma => shift ? '<' : ',',
            Keys.OemPeriod => shift ? '>' : '.',
            Keys.OemQuestion => shift ? '?' : '/',
            _ => '\0'
        };
    }
}
