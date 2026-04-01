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
    private SpawnPointTool _spawnTool = null!;
    private TilePalette _palette = null!;
    private EditorHUD _hud = null!;
    private MapSelectDialog _mapSelectDialog = null!;
    private SaveMapDialog? _activeSaveDialog;
    private EditorHistory _history = new();

    public enum Tool { TilePainter, SpawnPoint }
    public Tool ActiveTool { get; set; } = Tool.TilePainter;

    private KeyboardState _prevKeys;
    private MouseState _prevMouse;

    public EditorGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720
        };
        Content.RootDirectory = "SandboxGame/Content";
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

        _prototypes.LoadFromDirectory("SandboxGame/Content/Tiles");
        _mapManager = new MapManager("SandboxGame/Maps", _prototypes);

        NewMap();

        _tilePainter = new TilePainterTool(_currentMap, _currentTileMap, _prototypes, _assets, _history);
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
    _currentTileMap = new TileMap(50, 50, 32);
    _history = new EditorHistory();
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
            _spawnTool.SetMap(_currentMap);
        }

        // инструменты
        if (IsPressed(keys, _prevKeys, Keys.D1)) ActiveTool = Tool.TilePainter;
        if (IsPressed(keys, _prevKeys, Keys.D2)) ActiveTool = Tool.SpawnPoint;

        // камера
        var camSpeed = 300f / _camera.Zoom;
        if (keys.IsKeyDown(Keys.Left)) _camera.Position -= new Vector2(camSpeed * dt, 0);
        if (keys.IsKeyDown(Keys.Right)) _camera.Position += new Vector2(camSpeed * dt, 0);
        if (keys.IsKeyDown(Keys.Up)) _camera.Position -= new Vector2(0, camSpeed * dt);
        if (keys.IsKeyDown(Keys.Down)) _camera.Position += new Vector2(0, camSpeed * dt);

        // зум
        var scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scrollDelta > 0) _camera.Zoom = Math.Min(8f, _camera.Zoom + 0.25f);
        if (scrollDelta < 0) _camera.Zoom = Math.Max(0.5f, _camera.Zoom - 0.25f);

        // инструменты — не кликаем по UI
        var inPalette = _palette.Bounds.Contains(mouse.X, mouse.Y);
        var inHud = _hud.Bounds.Contains(mouse.X, mouse.Y);

        if (!inPalette && !inHud)
        {
            var worldPos = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
            if (ActiveTool == Tool.TilePainter)
                _tilePainter.Update(mouse, _prevMouse, worldPos, _palette.SelectedTileId);
            if (ActiveTool == Tool.SpawnPoint)
                _spawnTool.Update(mouse, _prevMouse, worldPos);
        }

        _palette.Update(mouse, _prevMouse);
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
        DrawGrid();
        _spawnTool.Draw(_spriteBatch, _assets, _font);

        _spriteBatch.End();

        _spriteBatch.Begin();
        _palette.Draw(_spriteBatch);
        _hud.Draw(_spriteBatch, ActiveTool, _currentMap, _history);
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
            for (int x = 0; x < _currentMap.Width; x++)
                for (int y = 0; y < _currentMap.Height; y++)
                {
                    var tile = _currentTileMap.GetTile(x, y);
                    if (tile.Type == TileType.Empty || tile.ProtoId == null) continue;
                    _currentMap.Tiles.Add(new TileData { X = x, Y = y, ProtoId = tile.ProtoId });
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
            _spawnTool.SetMap(_currentMap);
            _hud.ShowMessage($"Loaded: {_currentMap.Name}");
        });
    }

    private static bool IsPressed(KeyboardState cur, KeyboardState prev, Keys key)
        => cur.IsKeyDown(key) && prev.IsKeyUp(key);
}