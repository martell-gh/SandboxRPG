using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEditor.Tools;
using MTEditor.UI;
using MTEngine.Core;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEditor;

public class EditorGame : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;

    private Camera _camera = null!;
    private PrototypeManager _prototypes = new();
    private MapManager _mapManager = null!;
    private MapData _currentMap = new();
    private TileMap _currentTileMap = null!;
    private AssetManager _assets = null!;

    private TilePainterTool _tilePainter = null!;
    private EntityPainterTool _entityPainter = null!;
    private SpawnPointTool _spawnTool = null!;
    private TriggerZoneTool _triggerTool = null!;
    private AreaZoneTool _areaTool = null!;
    private TilePalette _palette = null!;
    private EditorHUD _hud = null!;
    private MapSelectDialog _mapSelectDialog = null!;
    private InGameMapsDialog _inGameMapsDialog = null!;
    private SaveMapDialog? _activeSaveDialog;
    private EditorHistory _history = new();

    private const int TileLayerCount = 3;

    public enum Tool { TilePainter, EntityPainter, SpawnPoint, TriggerZone, AreaZone }
    public Tool ActiveTool { get; set; } = Tool.TilePainter;
    public int ActiveTileLayer { get; set; } = 0;
    public PointerTool ActivePointerTool { get; set; } = PointerTool.Brush;
    public BrushShape ActiveBrushShape { get; set; } = BrushShape.Point;

    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private bool _isApplyingWindowChange;
    private Point _windowedSize = new(1280, 720);

    public EditorGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            IsFullScreen = false
        };
        Content.RootDirectory = ContentPaths.ContentRoot;
        IsMouseVisible = true;
        Window.Title = "MTEditor — Map Editor";
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        _camera = new Camera(GraphicsDevice);
        _camera.Zoom = 2f;
        Window.ClientSizeChanged += (_, _) => SyncBackBufferToWindow();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("DefaultFont");
        _assets = new AssetManager(Content, GraphicsDevice);

        ServiceLocator.Register(_spriteBatch);
        ServiceLocator.Register(_assets);
        ServiceLocator.Register(_prototypes);
        ServiceLocator.Register(_camera);

        _prototypes.LoadFromDirectory(ContentPaths.AbsolutePrototypesRoot);
        _mapManager = new MapManager(ContentPaths.AbsoluteMapsRoot, _prototypes);

        NewMap();

        _tilePainter = new TilePainterTool(_currentMap, _currentTileMap, _prototypes, _assets, _history);
        _entityPainter = new EntityPainterTool(_currentMap);
        _spawnTool = new SpawnPointTool(_currentMap, GraphicsDevice);
        _triggerTool = new TriggerZoneTool(_currentMap, _mapManager, GraphicsDevice, _font);
        _areaTool = new AreaZoneTool(_currentMap, GraphicsDevice, _font);
        _palette = new TilePalette(_prototypes, _assets, _font, GraphicsDevice);
        _hud = new EditorHUD(_font, GraphicsDevice);
        _mapSelectDialog = new MapSelectDialog(_font, GraphicsDevice);
        _inGameMapsDialog = new InGameMapsDialog(_font, GraphicsDevice);
    }

    private void NewMap()
    {
        _currentMap = new MapData
        {
            Id = "new_map",
            Name = "New Map",
            Width = 50,
            Height = 50,
            TileSize = 32
        };
        _currentTileMap = new TileMap(50, 50, 32, TileLayerCount);
        _history = new EditorHistory();
        ActiveTileLayer = 0;
        _camera.Position = new Vector2(
            _currentMap.Width * _currentMap.TileSize / 2f,
            _currentMap.Height * _currentMap.TileSize / 2f
        );
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();

        _activeSaveDialog?.Update(mouse, _prevMouse, keys, _prevKeys);
        if (_activeSaveDialog?.IsOpen == false) _activeSaveDialog = null;
        if (_activeSaveDialog?.IsOpen == true) goto EndUpdate;

        _mapSelectDialog.Update(mouse, _prevMouse, keys, _prevKeys);
        if (_mapSelectDialog.IsOpen) goto EndUpdate;

        if (IsPressed(keys, _prevKeys, Keys.F6))
        {
            if (_inGameMapsDialog.IsOpen)
                _inGameMapsDialog.Close();
            else
                _inGameMapsDialog.Open(_mapManager.GetMapCatalog(), HandleToggleInGameMap);
        }

        _inGameMapsDialog.Update(mouse, _prevMouse, keys, _prevKeys);
        if (_inGameMapsDialog.IsOpen) goto EndUpdate;

        // триггер тул обновляет свою UI-панель и диалог независимо от позиции курсора
        if (ActiveTool == Tool.TriggerZone)
            _triggerTool.Update(mouse, _prevMouse, keys, _prevKeys);
        if (ActiveTool == Tool.AreaZone)
            _areaTool.Update(mouse, _prevMouse, keys, _prevKeys);

        // блокируем остальной ввод если открыт диалог выбора карты в триггер-туле
        if (_triggerTool.IsDialogOpen) goto EndUpdate;

        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        var anyTyping = _spawnTool.IsTyping || _triggerTool.IsTyping || _areaTool.IsTyping;

        HandleWindowHotkeys(keys);

        // хоткеи (ctrl-комбинации всегда работают, остальные — только если не печатаем)
        if (ctrl && IsPressed(keys, _prevKeys, Keys.S)) SaveMap();
        if (ctrl && IsPressed(keys, _prevKeys, Keys.O)) LoadMap();
        if (ctrl && IsPressed(keys, _prevKeys, Keys.Z)) UndoLastAction();
        if (ctrl && IsPressed(keys, _prevKeys, Keys.Y)) RedoLastAction();
        if (ctrl && IsPressed(keys, _prevKeys, Keys.N))
        {
            NewMap();
            _tilePainter.SetMap(_currentMap, _currentTileMap);
            _tilePainter.SetHistory(_history);
            _entityPainter.SetMap(_currentMap);
            _spawnTool.SetMap(_currentMap);
            _triggerTool.SetMap(_currentMap);
            _areaTool.SetMap(_currentMap);
        }

        if (!anyTyping)
        {
            if (IsPressed(keys, _prevKeys, Keys.D1))
                ActiveTool = Tool.TilePainter;
            if (IsPressed(keys, _prevKeys, Keys.D2))
                ActiveTool = Tool.EntityPainter;
            if (IsPressed(keys, _prevKeys, Keys.D3))
                ActiveTool = Tool.SpawnPoint;
            if (IsPressed(keys, _prevKeys, Keys.D4))
                ActiveTool = Tool.TriggerZone;
            if (IsPressed(keys, _prevKeys, Keys.D5))
                ActiveTool = Tool.AreaZone;
            if (IsPressed(keys, _prevKeys, Keys.B))
                ActivePointerTool = PointerTool.Brush;
            if (IsPressed(keys, _prevKeys, Keys.V))
                ActivePointerTool = PointerTool.Mouse;

            if (IsPressed(keys, _prevKeys, Keys.Q))
                ActiveTileLayer = Math.Max(0, ActiveTileLayer - 1);
            if (IsPressed(keys, _prevKeys, Keys.E))
                ActiveTileLayer = Math.Min(_currentTileMap.LayerCount - 1, ActiveTileLayer + 1);
        }

        if (!anyTyping && ActiveTool == Tool.EntityPainter
            && ActivePointerTool == PointerTool.Mouse
            && (IsPressed(keys, _prevKeys, Keys.Delete) || IsPressed(keys, _prevKeys, Keys.Back)))
        {
            if (_entityPainter.DeleteSelection())
                _hud.ShowMessage("Deleted selected prototypes");
        }

        // камера (стрелки всегда, WASD — только если не печатаем)
        var camSpeed = 300f / _camera.Zoom;
        if (keys.IsKeyDown(Keys.Left)) _camera.Position -= new Vector2(camSpeed * dt, 0);
        if (keys.IsKeyDown(Keys.Right)) _camera.Position += new Vector2(camSpeed * dt, 0);
        if (keys.IsKeyDown(Keys.Up)) _camera.Position -= new Vector2(0, camSpeed * dt);
        if (keys.IsKeyDown(Keys.Down)) _camera.Position += new Vector2(0, camSpeed * dt);
        if (!anyTyping)
        {
            if (keys.IsKeyDown(Keys.A)) _camera.Position -= new Vector2(camSpeed * dt, 0);
            if (keys.IsKeyDown(Keys.D)) _camera.Position += new Vector2(camSpeed * dt, 0);
            if (keys.IsKeyDown(Keys.W)) _camera.Position -= new Vector2(0, camSpeed * dt);
            if (keys.IsKeyDown(Keys.S)) _camera.Position += new Vector2(0, camSpeed * dt);
        }

        // зум
        var scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        var menuCommand = _hud.Update(mouse, _prevMouse);
        if (menuCommand != EditorCommand.None)
            HandleMenuCommand(menuCommand);

        var inPalette = _palette.Bounds.Contains(mouse.X, mouse.Y);
        var inHud = _hud.TopBarBounds.Contains(mouse.Position) || _hud.BottomBarBounds.Contains(mouse.Position);

        if (inPalette)
        {
            _palette.Update(mouse, _prevMouse, scrollDelta);
            if (_palette.PendingToolSwitch.HasValue)
                ActiveTool = _palette.PendingToolSwitch.Value;
        }
        else
        {
            if (scrollDelta > 0) _camera.Zoom = Math.Min(8f, _camera.Zoom + 0.25f);
            if (scrollDelta < 0) _camera.Zoom = Math.Max(0.5f, _camera.Zoom - 0.25f);
            _palette.Update(mouse, _prevMouse, 0);
            if (_palette.PendingToolSwitch.HasValue)
                ActiveTool = _palette.PendingToolSwitch.Value;
        }

        // инструменты — не кликаем по UI
        if (!inPalette && !inHud)
        {
            var worldPos = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
            if (ActiveTool == Tool.TilePainter && ActivePointerTool == PointerTool.Brush)
                _tilePainter.Update(mouse, _prevMouse, worldPos, _palette.SelectedTileId, ActiveTileLayer, ActiveBrushShape);
            if (ActiveTool == Tool.EntityPainter)
            {
                if (ActivePointerTool == PointerTool.Brush && _palette.SelectedEntityId != null)
                    _entityPainter.UpdateBrush(mouse, _prevMouse, worldPos, _palette.SelectedEntityId, ActiveBrushShape);
                else if (ActivePointerTool == PointerTool.Mouse)
                    _entityPainter.UpdateSelection(mouse, _prevMouse, worldPos, mouse.Position, _camera, _prototypes, _assets, _palette.SelectedEntityId);
            }
            if (ActiveTool == Tool.SpawnPoint)
                _spawnTool.Update(mouse, _prevMouse, worldPos);
            if (ActiveTool == Tool.TriggerZone)
                _triggerTool.UpdateWorldInput(mouse, _prevMouse, worldPos, ActivePointerTool);
            if (ActiveTool == Tool.AreaZone)
                _areaTool.UpdateWorldInput(mouse, _prevMouse, worldPos, ActivePointerTool, keys);
        }

    EndUpdate:
        _prevKeys = keys;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.DimGray);

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix()
        );

        var viewport = GraphicsDevice.Viewport;
        var topLeft = _camera.ScreenToWorld(Vector2.Zero);
        var bottomRight = _camera.ScreenToWorld(new Vector2(viewport.Width, viewport.Height));
        var visibleArea = new Rectangle(
            (int)topLeft.X - 16, (int)topLeft.Y - 16,
            (int)(bottomRight.X - topLeft.X) + 32,
            (int)(bottomRight.Y - topLeft.Y) + 32
        );

        _currentTileMap.DrawWithPrototypes(_spriteBatch, visibleArea, _prototypes, _assets);
        DrawPlacedEntities(visibleArea);
        DrawGrid();
        DrawBrushPreview();
        DrawActiveLayerBadge();
        _spawnTool.Draw(_spriteBatch, _assets, _font);
        if (ActiveTool == Tool.TriggerZone)
            _triggerTool.Draw(_spriteBatch, _assets, _font);
        if (ActiveTool == Tool.AreaZone)
            _areaTool.Draw(_spriteBatch);

        _spriteBatch.End();

        _spriteBatch.Begin();
        if (ActiveTool == Tool.EntityPainter && ActivePointerTool == PointerTool.Mouse)
        {
            _entityPainter.DrawSelectionOverlay(_spriteBatch, _camera, _prototypes, _assets);
            _entityPainter.DrawPropertyPanel(_spriteBatch, _font, GraphicsDevice, _prototypes, _assets);
        }
        _palette.Draw(_spriteBatch);
        _hud.Draw(_spriteBatch, ActiveTool, ActivePointerTool, ActiveBrushShape, _currentMap, _history, ActiveTileLayer, _palette);
        if (ActiveTool == Tool.SpawnPoint)
            _spawnTool.DrawUI(_spriteBatch, _font);
        if (ActiveTool == Tool.TriggerZone)
            _triggerTool.DrawUI(_spriteBatch, _font);
        if (ActiveTool == Tool.AreaZone)
            _areaTool.DrawUI(_spriteBatch);
        _inGameMapsDialog.Draw(_spriteBatch);
        _mapSelectDialog.Draw(_spriteBatch);
        _activeSaveDialog?.Draw(_spriteBatch);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawGrid()
    {
        var tileSize = _currentMap.TileSize;
        var pixel = _assets.GetColorTexture("#ffffff");
        var gridColor = Color.White * 0.08f;

        var viewport = GraphicsDevice.Viewport;
        var topLeft = _camera.ScreenToWorld(Vector2.Zero);
        var bottomRight = _camera.ScreenToWorld(new Vector2(viewport.Width, viewport.Height));

        int startX = Math.Max(0, (int)(topLeft.X / tileSize));
        int startY = Math.Max(0, (int)(topLeft.Y / tileSize));
        int endX = Math.Min(_currentMap.Width, (int)(bottomRight.X / tileSize) + 1);
        int endY = Math.Min(_currentMap.Height, (int)(bottomRight.Y / tileSize) + 1);

        for (int x = startX; x <= endX; x++)
            _spriteBatch.Draw(pixel, new Rectangle(x * tileSize, startY * tileSize, 1, (endY - startY) * tileSize), gridColor);
        for (int y = startY; y <= endY; y++)
            _spriteBatch.Draw(pixel, new Rectangle(startX * tileSize, y * tileSize, (endX - startX) * tileSize, 1), gridColor);
    }

    private void DrawBrushPreview()
    {
        if (ActivePointerTool != PointerTool.Brush)
            return;

        if (_activeSaveDialog?.IsOpen == true || _mapSelectDialog.IsOpen)
            return;

        var mouse = Mouse.GetState();
        if (_palette.Bounds.Contains(mouse.X, mouse.Y) || _hud.TopBarBounds.Contains(mouse.Position) || _hud.BottomBarBounds.Contains(mouse.Position))
            return;

        var worldPos = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        if (ActiveTool == Tool.TilePainter && _palette.SelectedTileId != null)
        {
            var proto = _prototypes.GetTile(_palette.SelectedTileId);
            if (proto == null)
                return;
            var texture = proto.Sprite?.FullPath != null
                ? _assets.LoadFromFile(proto.Sprite.FullPath)
                : proto.Animations?.TexturePath != null ? _assets.LoadFromFile(proto.Animations.TexturePath) : null;
            var src = proto.Animations?.GetClip("idle")?.Frames.FirstOrDefault().SourceRect
                ?? (proto.Sprite != null ? new Rectangle(proto.Sprite.SrcX, proto.Sprite.SrcY, proto.Sprite.Width, proto.Sprite.Height) : null);

            foreach (var point in _tilePainter.GetPreviewPoints(worldPos, ActiveBrushShape))
            {
                var dest = new Rectangle(point.X * _currentMap.TileSize, point.Y * _currentMap.TileSize, _currentMap.TileSize, _currentMap.TileSize);
                if (texture != null)
                    _spriteBatch.Draw(texture, dest, src, Color.White * 0.45f);
                else
                    _spriteBatch.Draw(_assets.GetColorTexture(proto.Color), dest, Color.White * 0.35f);
            }
        }
        else if (ActiveTool == Tool.EntityPainter && _palette.SelectedEntityId != null)
        {
            var proto = _prototypes.GetEntity(_palette.SelectedEntityId);
            if (proto == null)
                return;

            var texture = proto.SpritePath != null ? _assets.LoadFromFile(proto.SpritePath) : null;
            if (texture != null && proto.PreviewSourceRect != null)
            {
                var src = proto.PreviewSourceRect.Value;
                foreach (var previewPos in _entityPainter.GetPreviewPositions(worldPos, ActiveBrushShape))
                {
                    _spriteBatch.Draw(
                        texture,
                        previewPos,
                        src,
                        Color.White * 0.55f,
                        0f,
                        new Vector2(src.Width / 2f, src.Height / 2f),
                        1f,
                        SpriteEffects.None,
                        0f);
                }
            }
            else
            {
                foreach (var previewPos in _entityPainter.GetPreviewPositions(worldPos, ActiveBrushShape))
                {
                    var rect = new Rectangle(
                        (int)MathF.Round(previewPos.X - _currentMap.TileSize / 2f),
                        (int)MathF.Round(previewPos.Y - _currentMap.TileSize / 2f),
                        _currentMap.TileSize,
                        _currentMap.TileSize);
                    _spriteBatch.Draw(_assets.GetColorTexture(proto.PreviewColor), rect, Color.White * 0.35f);
                }
            }
        }
    }

    private void SaveMap()
    {
        _activeSaveDialog = new SaveMapDialog(_font, GraphicsDevice);
        _activeSaveDialog.Open(_currentMap.Id, _currentMap.Name, (mapId, mapName) =>
        {
            _currentMap.Id = mapId;
            _currentMap.Name = mapName;

            _currentMap.Tiles.Clear();
            for (int layer = 0; layer < _currentTileMap.LayerCount; layer++)
                for (int x = 0; x < _currentMap.Width; x++)
                    for (int y = 0; y < _currentMap.Height; y++)
                    {
                        var tile = _currentTileMap.GetTile(x, y, layer);
                        if (tile.Type == TileType.Empty || tile.ProtoId == null) continue;
                        _currentMap.Tiles.Add(new TileData { X = x, Y = y, ProtoId = tile.ProtoId, Layer = layer });
                    }

            var (valid, error) = _currentMap.Validate();
            if (!valid) { _hud.ShowMessage($"Error: {error}"); return; }

            var result = _mapManager.SaveMap(_currentMap);
            _hud.ShowMessage(result ? $"Saved: {_currentMap.Id}" : "Save failed!");
        });
    }

    private void LoadMap()
    {
        var maps = _mapManager.GetAvailableMaps();
        _mapSelectDialog.Open(maps, mapId =>
        {
            var (tileMap, _) = _mapManager.LoadMap(mapId);
            if (tileMap == null) { _hud.ShowMessage($"Failed: {mapId}"); return; }

            _currentMap = _mapManager.CurrentMap!;
            _currentTileMap = tileMap;
            _history = new EditorHistory();
            _tilePainter.SetMap(_currentMap, _currentTileMap);
            _tilePainter.SetHistory(_history);
            _entityPainter.SetMap(_currentMap);
            _spawnTool.SetMap(_currentMap);
            _triggerTool.SetMap(_currentMap);
            _areaTool.SetMap(_currentMap);
            ActiveTileLayer = Math.Min(ActiveTileLayer, _currentTileMap.LayerCount - 1);
            _hud.ShowMessage($"Loaded: {_currentMap.Name}");
        });
    }

    private void DrawPlacedEntities(Rectangle visibleArea)
    {
        foreach (var entity in _currentMap.Entities)
        {
            var worldPos = GetEntityWorldPosition(entity);

            if (!visibleArea.Contains(worldPos))
                continue;

            var proto = _prototypes.GetEntity(entity.ProtoId);
            if (proto == null)
                continue;

            var texture = proto.SpritePath != null
                ? _assets.LoadFromFile(proto.SpritePath)
                : null;

            if (texture != null && proto.PreviewSourceRect != null)
            {
                var src = proto.PreviewSourceRect.Value;
                _spriteBatch.Draw(
                    texture,
                    worldPos,
                    src,
                    Color.White,
                    0f,
                    new Vector2(src.Width / 2f, src.Height / 2f),
                    1f,
                    SpriteEffects.None,
                    0f
                );
            }
            else
            {
                var rect = new Rectangle(
                    (int)MathF.Round(worldPos.X - _currentMap.TileSize / 2f),
                    (int)MathF.Round(worldPos.Y - _currentMap.TileSize / 2f),
                    _currentMap.TileSize,
                    _currentMap.TileSize);
                _spriteBatch.Draw(_assets.GetColorTexture(proto.PreviewColor), rect, Color.White * 0.8f);
            }
        }
    }

    public Vector2 GetEntityWorldPosition(MapEntityData entity)
    {
        if (entity.WorldSpace)
            return new Vector2(entity.X, entity.Y);

        if (entity.X >= 0 && entity.Y >= 0 && entity.X <= _currentMap.Width && entity.Y <= _currentMap.Height)
        {
            return new Vector2(
                (entity.X + 0.5f) * _currentMap.TileSize,
                (entity.Y + 0.5f) * _currentMap.TileSize);
        }

        return new Vector2(entity.X, entity.Y);
    }

    private void DrawActiveLayerBadge()
    {
        var rect = new Rectangle(6, 6, 90, 22);
        var pixel = _assets.GetColorTexture("#ffffff");
        var screenPos = _camera.ScreenToWorld(new Vector2(rect.X, rect.Y));
        var size = new Vector2(rect.Width / _camera.Zoom, rect.Height / _camera.Zoom);
        var worldRect = new Rectangle((int)screenPos.X, (int)screenPos.Y, (int)size.X, (int)size.Y);

        _spriteBatch.Draw(pixel, worldRect, Color.Black * 0.55f);
        _spriteBatch.DrawString(_font, $"Layer {ActiveTileLayer}", screenPos + new Vector2(4, 3), Color.Cyan);
    }

    private static bool IsPressed(KeyboardState cur, KeyboardState prev, Keys key)
        => cur.IsKeyDown(key) && prev.IsKeyUp(key);

    private void HandleMenuCommand(EditorCommand command)
    {
        switch (command)
        {
            case EditorCommand.NewMap:
                NewMap();
                _tilePainter.SetMap(_currentMap, _currentTileMap);
                _tilePainter.SetHistory(_history);
                _entityPainter.SetMap(_currentMap);
                _spawnTool.SetMap(_currentMap);
                _triggerTool.SetMap(_currentMap);
            _areaTool.SetMap(_currentMap);
                _hud.ShowMessage("Created: new map");
                break;
            case EditorCommand.LoadMap:
                LoadMap();
                break;
            case EditorCommand.SaveMap:
                SaveMap();
                break;
            case EditorCommand.InGameMaps:
                if (_inGameMapsDialog.IsOpen)
                    _inGameMapsDialog.Close();
                else
                    _inGameMapsDialog.Open(_mapManager.GetMapCatalog(), HandleToggleInGameMap);
                break;
            case EditorCommand.Undo:
                UndoLastAction();
                break;
            case EditorCommand.Redo:
                RedoLastAction();
                break;
            case EditorCommand.ToggleFullscreen:
                ToggleFullscreen();
                break;
            case EditorCommand.ToolTiles:
                ActiveTool = Tool.TilePainter;
                break;
            case EditorCommand.ToolPrototypes:
                ActiveTool = Tool.EntityPainter;
                break;
            case EditorCommand.ToolSpawns:
                ActiveTool = Tool.SpawnPoint;
                break;
            case EditorCommand.ToolTriggers:
                ActiveTool = Tool.TriggerZone;
                break;
            case EditorCommand.ToolAreas:
                ActiveTool = Tool.AreaZone;
                break;
            case EditorCommand.PointerBrush:
                ActivePointerTool = PointerTool.Brush;
                break;
            case EditorCommand.PointerMouse:
                ActivePointerTool = PointerTool.Mouse;
                break;
            case EditorCommand.ShapePoint:
                ActiveBrushShape = BrushShape.Point;
                break;
            case EditorCommand.ShapeLine:
                ActiveBrushShape = BrushShape.Line;
                break;
            case EditorCommand.ShapeFilledRectangle:
                ActiveBrushShape = BrushShape.FilledRectangle;
                break;
            case EditorCommand.ShapeHollowRectangle:
                ActiveBrushShape = BrushShape.HollowRectangle;
                break;
        }
    }

    private void HandleWindowHotkeys(KeyboardState keys)
    {
        var altDown = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
        if (IsPressed(keys, _prevKeys, Keys.F11) || (altDown && IsPressed(keys, _prevKeys, Keys.Enter)))
            ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_graphics.IsFullScreen)
        {
            _windowedSize = new Point(Window.ClientBounds.Width, Window.ClientBounds.Height);
            _graphics.IsFullScreen = true;
        }
        else
        {
            _graphics.IsFullScreen = false;
            _graphics.PreferredBackBufferWidth = Math.Max(960, _windowedSize.X);
            _graphics.PreferredBackBufferHeight = Math.Max(640, _windowedSize.Y);
        }

        ApplyWindowChanges();
    }

    private void SyncBackBufferToWindow()
    {
        if (_isApplyingWindowChange || _graphics.IsFullScreen)
            return;

        var width = Math.Max(960, Window.ClientBounds.Width);
        var height = Math.Max(640, Window.ClientBounds.Height);
        if (_graphics.PreferredBackBufferWidth == width && _graphics.PreferredBackBufferHeight == height)
            return;

        _windowedSize = new Point(width, height);
        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        ApplyWindowChanges();
    }

    private void ApplyWindowChanges()
    {
        try
        {
            _isApplyingWindowChange = true;
            _graphics.ApplyChanges();
        }
        finally
        {
            _isApplyingWindowChange = false;
        }
    }

    private void UndoLastAction()
    {
        if (ActiveTool == Tool.EntityPainter)
        {
            if (_entityPainter.TryUndo())
                return;

            _history.Undo(_currentTileMap);
            return;
        }

        if (_history.UndoCount > 0)
            _history.Undo(_currentTileMap);
        else
            _entityPainter.TryUndo();
    }

    private void RedoLastAction()
    {
        if (ActiveTool == Tool.EntityPainter)
        {
            if (_entityPainter.TryRedo())
                return;

            _history.Redo(_currentTileMap);
            return;
        }

        if (_history.RedoCount > 0)
            _history.Redo(_currentTileMap);
        else
            _entityPainter.TryRedo();
    }

    private void HandleToggleInGameMap(string mapId, bool inGame)
    {
        if (_mapManager.SetMapInGameFlag(mapId, inGame))
        {
            _inGameMapsDialog.Refresh(_mapManager.GetMapCatalog());
            _hud.ShowMessage($"Map '{mapId}' inGame = {inGame}");
        }
        else
        {
            _hud.ShowMessage($"Failed to update map '{mapId}'");
        }
    }
}
