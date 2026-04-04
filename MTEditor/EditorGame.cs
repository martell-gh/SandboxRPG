using System;
using System.IO;
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
    private TilePalette _palette = null!;
    private EditorHUD _hud = null!;
    private MapSelectDialog _mapSelectDialog = null!;
    private SaveMapDialog? _activeSaveDialog;
    private EditorHistory _history = new();

    private const int TileLayerCount = 3;

    public enum Tool { TilePainter, EntityPainter, SpawnPoint }
    public Tool ActiveTool { get; set; } = Tool.TilePainter;
    public int ActiveTileLayer { get; set; } = 0;

    private KeyboardState _prevKeys;
    private MouseState _prevMouse;

    public EditorGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720
        };
        Content.RootDirectory = ContentPaths.ContentRoot;
        IsMouseVisible = true;
        Window.Title = "MTEditor — Map Editor";
    }

    protected override void Initialize()
    {
        _camera = new Camera(GraphicsDevice);
        _camera.Zoom = 2f;
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
        _palette = new TilePalette(_prototypes, _assets, _font, GraphicsDevice);
        _hud = new EditorHUD(_font, GraphicsDevice);
        _mapSelectDialog = new MapSelectDialog(_font, GraphicsDevice);
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

        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

        // хоткеи
        if (ctrl && IsPressed(keys, _prevKeys, Keys.S)) SaveMap();
        if (ctrl && IsPressed(keys, _prevKeys, Keys.O)) LoadMap();
        if (ctrl && IsPressed(keys, _prevKeys, Keys.Z)) _history.Undo(_currentTileMap);
        if (ctrl && IsPressed(keys, _prevKeys, Keys.Y)) _history.Redo(_currentTileMap);
        if (ctrl && IsPressed(keys, _prevKeys, Keys.N))
        {
            NewMap();
            _tilePainter.SetMap(_currentMap, _currentTileMap);
            _tilePainter.SetHistory(_history);
            _entityPainter.SetMap(_currentMap);
            _spawnTool.SetMap(_currentMap);
        }

        if (IsPressed(keys, _prevKeys, Keys.D1))
        {
            ActiveTool = Tool.TilePainter;
            _palette.Mode = PaletteMode.Tiles;
        }
        if (IsPressed(keys, _prevKeys, Keys.D2))
        {
            ActiveTool = Tool.EntityPainter;
            _palette.Mode = PaletteMode.Entities;
        }
        if (IsPressed(keys, _prevKeys, Keys.D3))
            ActiveTool = Tool.SpawnPoint;

        if (IsPressed(keys, _prevKeys, Keys.Q))
            ActiveTileLayer = Math.Max(0, ActiveTileLayer - 1);
        if (IsPressed(keys, _prevKeys, Keys.E))
            ActiveTileLayer = Math.Min(_currentTileMap.LayerCount - 1, ActiveTileLayer + 1);

        // камера
        var camSpeed = 300f / _camera.Zoom;
        if (keys.IsKeyDown(Keys.Left)) _camera.Position -= new Vector2(camSpeed * dt, 0);
        if (keys.IsKeyDown(Keys.Right)) _camera.Position += new Vector2(camSpeed * dt, 0);
        if (keys.IsKeyDown(Keys.Up)) _camera.Position -= new Vector2(0, camSpeed * dt);
        if (keys.IsKeyDown(Keys.Down)) _camera.Position += new Vector2(0, camSpeed * dt);

        // зум
        var scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        var inPalette = _palette.Bounds.Contains(mouse.X, mouse.Y);
        var inHud = _hud.Bounds.Contains(mouse.X, mouse.Y);

        if (inPalette)
        {
            _palette.Update(mouse, _prevMouse, scrollDelta);
        }
        else
        {
            if (scrollDelta > 0) _camera.Zoom = Math.Min(8f, _camera.Zoom + 0.25f);
            if (scrollDelta < 0) _camera.Zoom = Math.Max(0.5f, _camera.Zoom - 0.25f);
            _palette.Update(mouse, _prevMouse, 0);
        }

        // инструменты — не кликаем по UI
        if (!inPalette && !inHud)
        {
            var worldPos = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
            if (ActiveTool == Tool.TilePainter)
                _tilePainter.Update(mouse, _prevMouse, worldPos, _palette.SelectedTileId, ActiveTileLayer);
            if (ActiveTool == Tool.EntityPainter && _palette.SelectedEntityId != null)
                _entityPainter.Update(mouse, _prevMouse, worldPos, _palette.SelectedEntityId);
            if (ActiveTool == Tool.SpawnPoint)
                _spawnTool.Update(mouse, _prevMouse, worldPos);
        }

        _hud.Update(mouse, _prevMouse);

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
        DrawActiveLayerBadge();
        _spawnTool.Draw(_spriteBatch, _assets, _font);

        _spriteBatch.End();

        _spriteBatch.Begin();
        _palette.Draw(_spriteBatch);
        _hud.Draw(_spriteBatch, ActiveTool, _currentMap, _history, ActiveTileLayer, _palette);
        if (ActiveTool == Tool.SpawnPoint)
            _spawnTool.DrawUI(_spriteBatch, _font);
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
            ActiveTileLayer = Math.Min(ActiveTileLayer, _currentTileMap.LayerCount - 1);
            _hud.ShowMessage($"Loaded: {_currentMap.Name}");
        });
    }

    private void DrawPlacedEntities(Rectangle visibleArea)
    {
        foreach (var entity in _currentMap.Entities)
        {
            var worldPos = new Vector2(
                (entity.X + 0.5f) * _currentMap.TileSize,
                (entity.Y + 0.5f) * _currentMap.TileSize
            );

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
                var rect = new Rectangle(entity.X * _currentMap.TileSize, entity.Y * _currentMap.TileSize, _currentMap.TileSize, _currentMap.TileSize);
                _spriteBatch.Draw(_assets.GetColorTexture(proto.PreviewColor), rect, Color.White * 0.8f);
            }
        }
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
}
