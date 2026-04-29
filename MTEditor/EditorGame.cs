using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEditor.Tools;
using MTEditor.UI;
using MTEngine.Core;
using MTEngine.Npc;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEditor;

public class EditorGame : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    private Camera _camera = null!;
    private PrototypeManager _prototypes = new();
    private MapManager _mapManager = null!;
    private MapData _currentMap = new();
    private TileMap _currentTileMap = null!;
    private WorldData _worldData = new();
    private ProfessionCatalog _professionCatalog = new();
    private AssetManager _assets = null!;

    private TilePainterTool _tilePainter = null!;
    private EntityPainterTool _entityPainter = null!;
    private SpawnPointTool _spawnTool = null!;
    private TriggerZoneTool _triggerTool = null!;
    private AreaZoneTool _areaTool = null!;
    private TilePalette _palette = null!;
    private EditorHUD _hud = null!;
    private MapLocationPanel _mapLocationPanel = null!;
    private FactionEditorPanel _factionEditorPanel = null!;
    private CityEditorPanel _cityEditorPanel = null!;
    private ProfessionEditorPanel _professionEditorPanel = null!;
    private NpcEditorPanel _npcEditorPanel = null!;
    private PrototypeEditorPanel _prototypeEditorPanel = null!;
    private GlobalSettingsPanel _globalSettingsPanel = null!;
    private MapSelectDialog _mapSelectDialog = null!;
    private InGameMapsDialog _inGameMapsDialog = null!;
    private ResizeMapDialog _resizeMapDialog = null!;
    private SaveMapDialog? _activeSaveDialog;
    private readonly EditorActionTracker _operationTracker;
    private EditorHistory _history;
    private ResizeMapHistory _resizeHistory;

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
        _operationTracker = new EditorActionTracker();
        _history = new EditorHistory(_operationTracker);
        _resizeHistory = new ResizeMapHistory(_operationTracker);

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            IsFullScreen = false,
            // Borderless fullscreen — avoids the SDL2/Cocoa NSWindowStyleMask crash
            // that hits when the hardware mode switch fires on macOS.
            HardwareModeSwitch = false
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
        _assets = new AssetManager(Content, GraphicsDevice);

        // UE-style theme + crisp TTF fonts for the entire editor UI.
        EditorTheme.Initialize(GraphicsDevice);

        ServiceLocator.Register(_spriteBatch);
        ServiceLocator.Register(_assets);
        ServiceLocator.Register(_prototypes);
        ServiceLocator.Register(_camera);

        var localization = new LocalizationManager();
        localization.Load(ContentPaths.AbsoluteLocalizationRoot);
        localization.SetLanguage(LocalizationManager.RussianId);
        ServiceLocator.Register(localization);

        _prototypes.LoadFromDirectory(ContentPaths.AbsolutePrototypesRoot);
        _mapManager = new MapManager(ContentPaths.AbsoluteMapsRoot, _prototypes);
        _worldData = _mapManager.GetWorldData();
        _professionCatalog = ProfessionCatalog.Load(GetProfessionsPath());

        NewMap();

        _tilePainter = new TilePainterTool(_currentMap, _currentTileMap, _prototypes, _assets, _history);
        _entityPainter = new EntityPainterTool(_currentMap, _operationTracker);
        _spawnTool = new SpawnPointTool(_currentMap, GraphicsDevice);
        _triggerTool = new TriggerZoneTool(_currentMap, _mapManager, GraphicsDevice);
        _areaTool = new AreaZoneTool(_currentMap, GraphicsDevice, _professionCatalog, _prototypes);
        _palette = new TilePalette(_prototypes, _assets, GraphicsDevice);
        _hud = new EditorHUD(GraphicsDevice);
        _mapLocationPanel = new MapLocationPanel(GraphicsDevice, _prototypes);
        _factionEditorPanel = new FactionEditorPanel(GraphicsDevice);
        _cityEditorPanel = new CityEditorPanel(GraphicsDevice);
        _professionEditorPanel = new ProfessionEditorPanel(GraphicsDevice, _prototypes);
        _npcEditorPanel = new NpcEditorPanel(
            GraphicsDevice,
            ContentPaths.AbsoluteMapsRoot,
            ContentPaths.AbsolutePrototypesRoot,
            ContentPaths.AbsoluteDataRoot,
            _prototypes,
            _assets);
        _prototypeEditorPanel = new PrototypeEditorPanel(GraphicsDevice, ContentPaths.AbsolutePrototypesRoot);
        _globalSettingsPanel = new GlobalSettingsPanel(GraphicsDevice, _prototypes);
        _factionEditorPanel.SyncSelection(_worldData);
        _cityEditorPanel.SyncSelection(_worldData);
        _professionEditorPanel.SyncSelection(_professionCatalog);
        _mapSelectDialog = new MapSelectDialog(GraphicsDevice);
        _inGameMapsDialog = new InGameMapsDialog(GraphicsDevice);
        _resizeMapDialog = new ResizeMapDialog(GraphicsDevice);
        SyncBackBufferToWindow();
    }

    private void NewMap()
    {
        _currentMap = new MapData
        {
            Id = "new_map",
            Name = "New Map",
            Width = 50,
            Height = 50,
            TileSize = 32,
            LocationKind = LocationKinds.Wilds
        };
        _currentTileMap = new TileMap(50, 50, 32, TileLayerCount);
        _operationTracker.Reset();
        _history.Clear();
        _resizeHistory.Clear();
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

        var saveDialogWasOpen = _activeSaveDialog?.IsOpen == true;
        _activeSaveDialog?.Update(mouse, _prevMouse, keys, _prevKeys);
        if (_activeSaveDialog?.IsOpen == false) _activeSaveDialog = null;
        if (saveDialogWasOpen || _activeSaveDialog?.IsOpen == true) goto EndUpdate;

        var resizeDialogWasOpen = _resizeMapDialog.IsOpen;
        _resizeMapDialog.Update(mouse, _prevMouse, keys, _prevKeys);
        if (resizeDialogWasOpen || _resizeMapDialog.IsOpen) goto EndUpdate;

        var mapSelectDialogWasOpen = _mapSelectDialog.IsOpen;
        _mapSelectDialog.Update(mouse, _prevMouse, keys, _prevKeys);
        if (mapSelectDialogWasOpen || _mapSelectDialog.IsOpen) goto EndUpdate;

        var activeTab = _hud.ActiveTabKind;

        if (activeTab == EditorWorkspaceTabKind.Map && IsPressed(keys, _prevKeys, Keys.F6))
        {
            if (_inGameMapsDialog.IsOpen)
                _inGameMapsDialog.Close();
            else
                _inGameMapsDialog.Open(_mapManager.GetMapCatalog(), HandleToggleInGameMap);
        }

        var inGameMapsDialogWasOpen = _inGameMapsDialog.IsOpen;
        _inGameMapsDialog.Update(mouse, _prevMouse, keys, _prevKeys);
        if (inGameMapsDialogWasOpen || _inGameMapsDialog.IsOpen) goto EndUpdate;

        var menuCommand = _hud.Update(mouse, _prevMouse, _palette.Bounds);
        if (menuCommand != EditorCommand.None)
            HandleMenuCommand(menuCommand);
        activeTab = _hud.ActiveTabKind;
        var clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        var hudConsumedClick = clicked && (_hud.IsAnyMenuOpen || _hud.ContainsInteractive(mouse.Position));

        var triggerDialogWasOpen = activeTab == EditorWorkspaceTabKind.Map
            && ActiveTool == Tool.TriggerZone
            && _triggerTool.IsDialogOpen;
        if (activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.TriggerZone)
            _triggerTool.Update(mouse, _prevMouse, keys, _prevKeys);
        if (activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.AreaZone)
            _areaTool.Update(mouse, _prevMouse, keys, _prevKeys);

        if (triggerDialogWasOpen || activeTab == EditorWorkspaceTabKind.Map && _triggerTool.IsDialogOpen)
            goto EndUpdate;

        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        var panelTyping = activeTab switch
        {
            EditorWorkspaceTabKind.Factions => _factionEditorPanel.IsTyping,
            EditorWorkspaceTabKind.Cities => _cityEditorPanel.IsTyping,
            EditorWorkspaceTabKind.Professions => _professionEditorPanel.IsTyping,
            EditorWorkspaceTabKind.Npcs => _npcEditorPanel.IsTyping,
            EditorWorkspaceTabKind.Prototypes => _prototypeEditorPanel.IsTyping,
            EditorWorkspaceTabKind.GlobalSettings => _globalSettingsPanel.IsTyping,
            _ => false
        };
        var anyTyping = _spawnTool.IsTyping || _triggerTool.IsTyping || _areaTool.IsTyping || panelTyping;

        HandleWindowHotkeys(keys);

        if (ctrl && IsPressed(keys, _prevKeys, Keys.W))
            HandleMenuCommand(EditorCommand.CloseCurrentTab);

        // хоткеи вкладки
        if (activeTab == EditorWorkspaceTabKind.Map)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S)) SaveMap();
            if (ctrl && IsPressed(keys, _prevKeys, Keys.O)) LoadMap();
            if (ctrl && IsPressed(keys, _prevKeys, Keys.R)) OpenResizeMapDialog();
            if (ctrl && IsPressed(keys, _prevKeys, Keys.Z)) UndoLastAction();
            if (ctrl && IsPressed(keys, _prevKeys, Keys.Y)) RedoLastAction();
            if (ctrl && IsPressed(keys, _prevKeys, Keys.N))
            {
                NewMap();
                SyncToolsToCurrentMap();
            }
        }
        else if (activeTab == EditorWorkspaceTabKind.Factions)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S))
                HandleMenuCommand(EditorCommand.SaveFaction);
        }
        else if (activeTab == EditorWorkspaceTabKind.Cities)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S))
                HandleMenuCommand(EditorCommand.SaveCity);
        }
        else if (activeTab == EditorWorkspaceTabKind.Professions)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S))
                HandleMenuCommand(EditorCommand.SaveProfession);
        }
        else if (activeTab == EditorWorkspaceTabKind.Npcs)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S))
                HandleMenuCommand(EditorCommand.SaveNpcRoster);
        }
        else if (activeTab == EditorWorkspaceTabKind.Prototypes)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S))
                HandleMenuCommand(EditorCommand.SavePrototype);
        }
        else if (activeTab == EditorWorkspaceTabKind.GlobalSettings)
        {
            if (ctrl && IsPressed(keys, _prevKeys, Keys.S))
                HandleMenuCommand(EditorCommand.SaveGlobalSettings);
        }

        if (!anyTyping && activeTab == EditorWorkspaceTabKind.Map)
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

            if (ActiveTool == Tool.EntityPainter
                && IsPressed(keys, _prevKeys, Keys.Escape)
                && _palette.SelectedEntityId != null)
            {
                _palette.ClearSelectedEntity();
                _hud.ShowMessage("Cleared active prototype");
            }
        }

        if (!anyTyping && activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.EntityPainter
            && ActivePointerTool == PointerTool.Mouse
            && (IsPressed(keys, _prevKeys, Keys.Delete) || IsPressed(keys, _prevKeys, Keys.Back)))
        {
            if (_entityPainter.DeleteSelection())
                _hud.ShowMessage("Deleted selected prototypes");
        }

        if (activeTab == EditorWorkspaceTabKind.Factions)
        {
            if (hudConsumedClick)
                goto EndUpdate;

            var factionChange = _factionEditorPanel.Update(mouse, _prevMouse, keys, _prevKeys, _worldData, _hud.ShowMessage);
            if (factionChange != null)
                ApplyFactionEditorChange(factionChange);
            goto EndUpdate;
        }

        if (activeTab == EditorWorkspaceTabKind.Cities)
        {
            if (hudConsumedClick)
                goto EndUpdate;

            var cityChange = _cityEditorPanel.Update(mouse, _prevMouse, keys, _prevKeys, _worldData, _hud.ShowMessage);
            if (cityChange != null)
                ApplyCityEditorChange(cityChange);
            goto EndUpdate;
        }

        if (activeTab == EditorWorkspaceTabKind.Professions)
        {
            if (hudConsumedClick)
                goto EndUpdate;

            var professionChange = _professionEditorPanel.Update(mouse, _prevMouse, keys, _prevKeys, _professionCatalog, _hud.ShowMessage);
            if (professionChange != null)
                ApplyProfessionEditorChange(professionChange);
            goto EndUpdate;
        }

        if (activeTab == EditorWorkspaceTabKind.Prototypes)
        {
            if (hudConsumedClick)
                goto EndUpdate;

            if (_prototypeEditorPanel.Update(mouse, _prevMouse, keys, _prevKeys, _hud.ShowMessage))
                ReloadPrototypeManager();
            goto EndUpdate;
        }

        if (activeTab == EditorWorkspaceTabKind.Npcs)
        {
            if (hudConsumedClick)
                goto EndUpdate;

            _npcEditorPanel.Update(mouse, _prevMouse, keys, _prevKeys, _worldData, _hud.ShowMessage);
            goto EndUpdate;
        }

        if (activeTab == EditorWorkspaceTabKind.GlobalSettings)
        {
            if (hudConsumedClick)
                goto EndUpdate;

            if (_globalSettingsPanel.Update(mouse, _prevMouse, keys, _prevKeys, _worldData, _mapManager.GetMapCatalog(), _hud.ShowMessage))
                PersistGlobalSettings();
            goto EndUpdate;
        }

        var locationPanelChanged = _mapLocationPanel.Update(mouse, _prevMouse, _hud.MapToolbarBounds, _palette.Bounds, _currentMap, _worldData);

        // камера (стрелки почти всегда; AreaZone panel забирает их для kind-переключателя)
        var camSpeed = 300f / _camera.Zoom;
        var blockArrowCamera = ActiveTool == Tool.AreaZone && _areaTool.IsInputBlocking;
        if (!blockArrowCamera && keys.IsKeyDown(Keys.Left)) _camera.Position -= new Vector2(camSpeed * dt, 0);
        if (!blockArrowCamera && keys.IsKeyDown(Keys.Right)) _camera.Position += new Vector2(camSpeed * dt, 0);
        if (!blockArrowCamera && keys.IsKeyDown(Keys.Up)) _camera.Position -= new Vector2(0, camSpeed * dt);
        if (!blockArrowCamera && keys.IsKeyDown(Keys.Down)) _camera.Position += new Vector2(0, camSpeed * dt);
        if (!anyTyping)
        {
            if (keys.IsKeyDown(Keys.A)) _camera.Position -= new Vector2(camSpeed * dt, 0);
            if (keys.IsKeyDown(Keys.D)) _camera.Position += new Vector2(camSpeed * dt, 0);
            if (keys.IsKeyDown(Keys.W)) _camera.Position -= new Vector2(0, camSpeed * dt);
            if (keys.IsKeyDown(Keys.S)) _camera.Position += new Vector2(0, camSpeed * dt);
        }

        var scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        var inPalette = _palette.Bounds.Contains(mouse.X, mouse.Y);
        var inHud = _hud.ContainsInteractive(mouse.Position)
            || (clicked && _hud.IsAnyMenuOpen)
            || _mapLocationPanel.Bounds.Contains(mouse.Position);

        if (inPalette)
        {
            _palette.Update(mouse, _prevMouse, scrollDelta);
            if (_palette.PendingToolSwitch.HasValue)
                ActiveTool = _palette.PendingToolSwitch.Value;
        }
        else
        {
            if (!inHud)
            {
                if (scrollDelta > 0) _camera.Zoom = Math.Min(8f, _camera.Zoom + 0.25f);
                if (scrollDelta < 0) _camera.Zoom = Math.Max(0.5f, _camera.Zoom - 0.25f);
            }
            _palette.Update(mouse, _prevMouse, 0);
            if (_palette.PendingToolSwitch.HasValue)
                ActiveTool = _palette.PendingToolSwitch.Value;
        }

        // инструменты — не кликаем по UI
        if (!locationPanelChanged && !inPalette && !inHud)
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
            if (ActiveTool == Tool.AreaZone && !_areaTool.IsInputBlocking)
                _areaTool.UpdateWorldInput(mouse, _prevMouse, worldPos, ActivePointerTool, keys);
        }

    EndUpdate:
        _prevKeys = keys;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var activeTab = _hud.ActiveTabKind;
        GraphicsDevice.Clear(activeTab == EditorWorkspaceTabKind.Map ? Color.DimGray : EditorTheme.BgDeep);

        if (activeTab == EditorWorkspaceTabKind.Map)
        {
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
            if (ActiveTool == Tool.SpawnPoint)
                _spawnTool.Draw(_spriteBatch, _assets);
            if (ActiveTool == Tool.TriggerZone)
                _triggerTool.Draw(_spriteBatch, _assets);
            if (ActiveTool == Tool.AreaZone)
                _areaTool.Draw(_spriteBatch);

            _spriteBatch.End();
        }

        _spriteBatch.Begin();
        if (activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.EntityPainter && ActivePointerTool == PointerTool.Mouse)
        {
            _entityPainter.DrawSelectionOverlay(_spriteBatch, _camera, _prototypes, _assets);
            _entityPainter.DrawPropertyPanel(_spriteBatch, GraphicsDevice, _prototypes, _assets);
        }
        if (activeTab == EditorWorkspaceTabKind.Map)
            _palette.Draw(_spriteBatch, ActiveTool);
        if (activeTab == EditorWorkspaceTabKind.Map)
        {
            _mapLocationPanel.Draw(_spriteBatch, _hud.MapToolbarBounds, _palette.Bounds, _currentMap, _worldData);
            DrawActiveLayerBadge();
        }
        else if (activeTab == EditorWorkspaceTabKind.Factions)
        {
            _factionEditorPanel.Draw(_spriteBatch, _worldData);
        }
        else if (activeTab == EditorWorkspaceTabKind.Cities)
        {
            _cityEditorPanel.Draw(_spriteBatch, _worldData);
        }
        else if (activeTab == EditorWorkspaceTabKind.Professions)
        {
            _professionEditorPanel.Draw(_spriteBatch, _professionCatalog);
        }
        else if (activeTab == EditorWorkspaceTabKind.Npcs)
        {
            _npcEditorPanel.Draw(_spriteBatch, _worldData);
        }
        else if (activeTab == EditorWorkspaceTabKind.Prototypes)
        {
            _prototypeEditorPanel.Draw(_spriteBatch);
        }
        else if (activeTab == EditorWorkspaceTabKind.GlobalSettings)
        {
            _globalSettingsPanel.Draw(_spriteBatch, _worldData, _mapManager.GetMapCatalog());
        }

        if (activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.SpawnPoint)
            _spawnTool.DrawUI(_spriteBatch);
        if (activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.TriggerZone)
            _triggerTool.DrawUI(_spriteBatch);
        if (activeTab == EditorWorkspaceTabKind.Map && ActiveTool == Tool.AreaZone)
            _areaTool.DrawUI(_spriteBatch);

        // HUD stays above workspace content so dropdowns and tabs always win visually.
        _hud.Draw(_spriteBatch, ActiveTool, ActivePointerTool, ActiveBrushShape, _currentMap, _history, ActiveTileLayer, _palette, GetFactionLabel(), GetCityLabel());

        _inGameMapsDialog.Draw(_spriteBatch);
        _mapSelectDialog.Draw(_spriteBatch);
        _activeSaveDialog?.Draw(_spriteBatch);
        _resizeMapDialog.Draw(_spriteBatch);
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
        if (_palette.Bounds.Contains(mouse.X, mouse.Y)
            || _hud.TopBarBounds.Contains(mouse.Position)
            || _hud.BottomBarBounds.Contains(mouse.Position)
            || _hud.MapToolbarBounds.Contains(mouse.Position)
            || _mapLocationPanel.Bounds.Contains(mouse.Position))
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
        _activeSaveDialog = new SaveMapDialog(GraphicsDevice);
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

            var autoFixCount = _areaTool.RepairRequiredAreaMarkers();
            var areaErrors = _areaTool.GetBlockingValidationErrors();
            if (areaErrors.Count > 0)
            {
                ActiveTool = Tool.AreaZone;
                _hud.ShowMessage($"Area error: {areaErrors[0]}");
                return;
            }

            var (valid, error) = _currentMap.Validate();
            if (!valid) { _hud.ShowMessage($"Error: {error}"); return; }

            var result = _mapManager.SaveMap(_currentMap);
            _hud.ShowMessage(result
                ? autoFixCount > 0 ? $"Saved: {_currentMap.Id} (area auto-fixes: {autoFixCount})" : $"Saved: {_currentMap.Id}"
                : "Save failed!");
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
            _operationTracker.Reset();
            _history.Clear();
            _resizeHistory.Clear();
            SyncToolsToCurrentMap();
            ActiveTileLayer = Math.Min(ActiveTileLayer, _currentTileMap.LayerCount - 1);
            _hud.ShowMessage($"Loaded: {_currentMap.Name}");
        });
    }

    private void OpenResizeMapDialog()
    {
        _resizeMapDialog.Open(_currentMap.Width, _currentMap.Height, ResizeCurrentMap);
    }

    private void ResizeCurrentMap(int newWidth, int newHeight)
    {
        if (newWidth == _currentMap.Width && newHeight == _currentMap.Height)
        {
            _hud.ShowMessage("Map size is already the same");
            return;
        }

        var before = EditorMapSnapshot.Capture(_currentMap, _currentTileMap);
        var after = BuildResizedSnapshot(newWidth, newHeight);
        ApplyMapSnapshot(after);
        _resizeHistory.Record(before, EditorMapSnapshot.Capture(_currentMap, _currentTileMap));
        _hud.ShowMessage($"Resized map to {newWidth}x{newHeight}");
    }

    private EditorMapSnapshot BuildResizedSnapshot(int newWidth, int newHeight)
    {
        var resizedTileMap = new TileMap(newWidth, newHeight, _currentTileMap.TileSize, _currentTileMap.LayerCount);
        var copyWidth = Math.Min(_currentTileMap.Width, newWidth);
        var copyHeight = Math.Min(_currentTileMap.Height, newHeight);

        for (var layer = 0; layer < _currentTileMap.LayerCount; layer++)
        {
            for (var x = 0; x < copyWidth; x++)
            {
                for (var y = 0; y < copyHeight; y++)
                    resizedTileMap.SetTile(x, y, _currentTileMap.GetTile(x, y, layer).Clone(), layer);
            }
        }

        var resizedMap = EditorMapSnapshot.CloneMap(_currentMap, resizedTileMap);
        resizedMap.Width = newWidth;
        resizedMap.Height = newHeight;
        resizedMap.SpawnPoints = _currentMap.SpawnPoints
            .Where(spawn => spawn.X >= 0 && spawn.X < newWidth && spawn.Y >= 0 && spawn.Y < newHeight)
            .Select(spawn => new SpawnPoint
            {
                Id = spawn.Id,
                X = spawn.X,
                Y = spawn.Y
            })
            .ToList();
        resizedMap.Entities = _currentMap.Entities
            .Where(entity => IsEntityInsideMap(entity, newWidth, newHeight, _currentMap.TileSize))
            .Select(CloneEntity)
            .ToList();
        resizedMap.Triggers = _currentMap.Triggers
            .Select(trigger => CropTrigger(trigger, newWidth, newHeight))
            .Where(trigger => trigger.Tiles.Count > 0)
            .ToList();
        resizedMap.Areas = _currentMap.Areas
            .Select(area => CropArea(area, newWidth, newHeight))
            .Where(area => area.Tiles.Count > 0)
            .ToList();
        resizedMap.Tiles = BuildTileData(resizedTileMap);

        return new EditorMapSnapshot
        {
            Map = resizedMap,
            TileMap = resizedTileMap
        };
    }

    private void ApplyMapSnapshot(EditorMapSnapshot snapshot)
    {
        _currentMap = EditorMapSnapshot.CloneMap(snapshot.Map, snapshot.TileMap);
        _currentTileMap = EditorMapSnapshot.CloneTileMap(snapshot.TileMap);
        ActiveTileLayer = Math.Min(ActiveTileLayer, _currentTileMap.LayerCount - 1);
        ClampCameraToCurrentMap();
        SyncToolsToCurrentMap();
    }

    private void SyncToolsToCurrentMap()
    {
        if (_tilePainter is not null)
        {
            _tilePainter.SetMap(_currentMap, _currentTileMap);
            _tilePainter.SetHistory(_history);
        }

        if (_entityPainter is not null)
            _entityPainter.SetMap(_currentMap);

        if (_spawnTool is not null)
            _spawnTool.SetMap(_currentMap);

        if (_triggerTool is not null)
            _triggerTool.SetMap(_currentMap);

        if (_areaTool is not null)
        {
            _areaTool.SetMap(_currentMap);
            _areaTool.SetProfessionCatalog(_professionCatalog);
        }
    }

    private void ClampCameraToCurrentMap()
    {
        var maxX = Math.Max(_currentMap.TileSize * 0.5f, _currentMap.Width * _currentMap.TileSize);
        var maxY = Math.Max(_currentMap.TileSize * 0.5f, _currentMap.Height * _currentMap.TileSize);
        _camera.Position = new Vector2(
            Math.Clamp(_camera.Position.X, 0f, maxX),
            Math.Clamp(_camera.Position.Y, 0f, maxY));
    }

    private static List<TileData> BuildTileData(TileMap tileMap)
    {
        var result = new List<TileData>();
        for (var layer = 0; layer < tileMap.LayerCount; layer++)
        {
            for (var x = 0; x < tileMap.Width; x++)
            {
                for (var y = 0; y < tileMap.Height; y++)
                {
                    var tile = tileMap.GetTile(x, y, layer);
                    if (tile.Type == TileType.Empty || string.IsNullOrWhiteSpace(tile.ProtoId))
                        continue;

                    result.Add(new TileData
                    {
                        X = x,
                        Y = y,
                        Layer = layer,
                        ProtoId = tile.ProtoId
                    });
                }
            }
        }

        return result;
    }

    private static MapEntityData CloneEntity(MapEntityData entity)
    {
        return new MapEntityData
        {
            X = entity.X,
            Y = entity.Y,
            ProtoId = entity.ProtoId,
            WorldSpace = entity.WorldSpace,
            ComponentOverrides = entity.ComponentOverrides.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.DeepClone().AsObject(),
                StringComparer.OrdinalIgnoreCase),
            ContainedEntities = entity.ContainedEntities.Select(CloneEntity).ToList()
        };
    }

    private static TriggerZoneData CropTrigger(TriggerZoneData trigger, int width, int height)
    {
        return new TriggerZoneData
        {
            Id = trigger.Id,
            Action = new TriggerActionData
            {
                Type = trigger.Action.Type,
                TargetMapId = trigger.Action.TargetMapId,
                SpawnPointId = trigger.Action.SpawnPointId
            },
            Tiles = trigger.Tiles
                .Where(tile => tile.X >= 0 && tile.X < width && tile.Y >= 0 && tile.Y < height)
                .Select(tile => new TriggerTile
                {
                    X = tile.X,
                    Y = tile.Y
                })
                .ToList()
        };
    }

    private static AreaZoneData CropArea(AreaZoneData area, int width, int height)
    {
        return new AreaZoneData
        {
            Id = area.Id,
            Kind = area.Kind,
            Properties = area.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            Tiles = area.Tiles
                .Where(tile => tile.X >= 0 && tile.X < width && tile.Y >= 0 && tile.Y < height)
                .Select(tile => new TriggerTile
                {
                    X = tile.X,
                    Y = tile.Y
                })
                .ToList(),
            Points = area.Points
                .Where(point => point.X >= 0 && point.X < width && point.Y >= 0 && point.Y < height)
                .Select(point => new AreaPointData
                {
                    Id = point.Id,
                    X = point.X,
                    Y = point.Y
                })
                .ToList()
        };
    }

    private static bool IsEntityInsideMap(MapEntityData entity, int width, int height, int tileSize)
    {
        if (entity.WorldSpace)
            return entity.X >= 0f && entity.X < width * tileSize && entity.Y >= 0f && entity.Y < height * tileSize;

        return entity.X >= 0f && entity.X < width && entity.Y >= 0f && entity.Y < height;
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
        // Tiny badge anchored to the bottom-left of the visible palette area.
        var text = $"LAYER {ActiveTileLayer}";
        var size = EditorTheme.Small.MeasureString(text);
        var rect = new Rectangle(
            _hud.TopBarBounds.Height + 8,
            _hud.TopBarBounds.Height + 8,
            (int)size.X + 16,
            18);
        rect.X = GraphicsDevice.Viewport.Width - rect.Width - 12;
        rect.Y = _hud.TopBarBounds.Height + 8;
        EditorTheme.FillRect(_spriteBatch, rect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(_spriteBatch, rect, EditorTheme.Accent);
        EditorTheme.DrawText(_spriteBatch, EditorTheme.Small, text,
            new Vector2(rect.X + 8, rect.Y + (rect.Height - size.Y) / 2f - 1),
            EditorTheme.Text);
    }

    private string GetFactionLabel()
    {
        if (string.IsNullOrWhiteSpace(_currentMap.FactionId))
            return "None";

        var faction = _worldData.GetFaction(_currentMap.FactionId);
        return faction == null ? $"Missing: {_currentMap.FactionId}" : LocalizationManager.T(faction.Name);
    }

    private string GetCityLabel()
    {
        if (string.IsNullOrWhiteSpace(_currentMap.CityId))
            return "Standalone";

        var city = _worldData.GetCity(_currentMap.CityId);
        return city == null ? $"Missing: {_currentMap.CityId}" : city.Name;
    }

    private void ApplyFactionEditorChange(FactionEditorChange change)
    {
        if (!_mapManager.SaveWorldData(_worldData))
        {
            _hud.ShowMessage("Failed to save world data");
            ReloadWorldDataFromDisk();
            return;
        }

        if (!string.IsNullOrWhiteSpace(change.RenamedFromId) && !string.IsNullOrWhiteSpace(change.RenamedToId))
        {
            if (string.Equals(_currentMap.FactionId, change.RenamedFromId, StringComparison.OrdinalIgnoreCase))
                _currentMap.FactionId = change.RenamedToId;
            _mapManager.ReplaceFactionReferences(change.RenamedFromId, change.RenamedToId);
        }

        if (!string.IsNullOrWhiteSpace(change.DeletedFactionId))
        {
            if (string.Equals(_currentMap.FactionId, change.DeletedFactionId, StringComparison.OrdinalIgnoreCase))
                _currentMap.FactionId = null;
            _mapManager.ReplaceFactionReferences(change.DeletedFactionId, null);
        }

        if (!change.SkipReload)
            ReloadWorldDataFromDisk();
    }

    private void ApplyCityEditorChange(CityEditorChange change)
    {
        if (!_mapManager.SaveWorldData(_worldData))
        {
            _hud.ShowMessage("Failed to save world data");
            ReloadWorldDataFromDisk();
            return;
        }

        if (!string.IsNullOrWhiteSpace(change.RenamedFromId) && !string.IsNullOrWhiteSpace(change.RenamedToId))
        {
            if (string.Equals(_currentMap.CityId, change.RenamedFromId, StringComparison.OrdinalIgnoreCase))
                _currentMap.CityId = change.RenamedToId;
            _mapManager.ReplaceCityReferences(change.RenamedFromId, change.RenamedToId);
        }

        if (!string.IsNullOrWhiteSpace(change.DeletedCityId))
        {
            if (string.Equals(_currentMap.CityId, change.DeletedCityId, StringComparison.OrdinalIgnoreCase))
                _currentMap.CityId = null;
            _mapManager.ReplaceCityReferences(change.DeletedCityId, null);
        }

        ReloadWorldDataFromDisk();
    }

    private void ReloadWorldDataFromDisk()
    {
        _worldData = _mapManager.GetWorldData();
        _factionEditorPanel.SyncSelection(_worldData);
        _cityEditorPanel.SyncSelection(_worldData);
    }

    private void ApplyProfessionEditorChange(ProfessionEditorChange change)
    {
        SaveProfessionCatalog();

        if (!string.IsNullOrWhiteSpace(change.RenamedFromId) && !string.IsNullOrWhiteSpace(change.RenamedToId))
        {
            ReplaceProfessionReferences(_currentMap, change.RenamedFromId, change.RenamedToId);
            _mapManager.ReplaceProfessionReferences(change.RenamedFromId, change.RenamedToId);
        }

        if (!string.IsNullOrWhiteSpace(change.DeletedProfessionId))
        {
            ReplaceProfessionReferences(_currentMap, change.DeletedProfessionId, null);
            _mapManager.ReplaceProfessionReferences(change.DeletedProfessionId, null);
        }

        _professionEditorPanel.SyncSelection(_professionCatalog);
        _areaTool.SetProfessionCatalog(_professionCatalog);
    }

    private void ReloadProfessionCatalog()
    {
        _professionCatalog = ProfessionCatalog.Load(GetProfessionsPath());
        _professionEditorPanel.SyncSelection(_professionCatalog);
        _areaTool.SetProfessionCatalog(_professionCatalog);
    }

    private void SaveProfessionCatalog()
    {
        _professionCatalog.Save(GetProfessionsPath());
        _areaTool.SetProfessionCatalog(_professionCatalog);
    }

    private static void ReplaceProfessionReferences(MapData map, string oldProfessionId, string? newProfessionId)
    {
        foreach (var area in map.Areas)
        {
            if (!area.Properties.TryGetValue("professionId", out var current)
                || !string.Equals(current, oldProfessionId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(newProfessionId))
                area.Properties.Remove("professionId");
            else
                area.Properties["professionId"] = newProfessionId;
        }
    }

    private static string GetProfessionsPath()
        => Path.Combine(ContentPaths.AbsoluteDataRoot, "professions.json");

    private static bool IsPressed(KeyboardState cur, KeyboardState prev, Keys key)
        => cur.IsKeyDown(key) && prev.IsKeyUp(key);

    private void HandleMenuCommand(EditorCommand command)
    {
        switch (command)
        {
            case EditorCommand.OpenMapTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.Map);
                break;
            case EditorCommand.OpenFactionsTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.Factions);
                break;
            case EditorCommand.OpenCitiesTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.Cities);
                break;
            case EditorCommand.OpenProfessionsTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.Professions);
                break;
            case EditorCommand.OpenNpcsTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.Npcs);
                break;
            case EditorCommand.OpenPrototypesTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.Prototypes);
                break;
            case EditorCommand.OpenGlobalSettingsTab:
                _hud.EnsureTab(EditorWorkspaceTabKind.GlobalSettings);
                _globalSettingsPanel.SyncFromWorldData(_worldData);
                break;
            case EditorCommand.CloseCurrentTab:
                if (!_hud.TryCloseCurrentTab())
                    _hud.ShowMessage("Current tab cannot be closed");
                break;
            case EditorCommand.NewMap:
                NewMap();
                SyncToolsToCurrentMap();
                _hud.ShowMessage("Created: new map");
                break;
            case EditorCommand.LoadMap:
                LoadMap();
                break;
            case EditorCommand.SaveMap:
                SaveMap();
                break;
            case EditorCommand.ResizeMap:
                OpenResizeMapDialog();
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
            case EditorCommand.NewFaction:
            {
                var change = _factionEditorPanel.CreateNew(_worldData, _hud.ShowMessage);
                if (change != null)
                    ApplyFactionEditorChange(change);
                break;
            }
            case EditorCommand.SaveFaction:
            {
                var change = _factionEditorPanel.SaveCurrent(_worldData, _hud.ShowMessage);
                if (change != null)
                    ApplyFactionEditorChange(change);
                break;
            }
            case EditorCommand.DeleteFaction:
            {
                var change = _factionEditorPanel.DeleteCurrent(_worldData, _hud.ShowMessage);
                if (change != null)
                    ApplyFactionEditorChange(change);
                break;
            }
            case EditorCommand.NewCity:
            {
                var change = _cityEditorPanel.CreateNew(_worldData, _hud.ShowMessage);
                if (change != null)
                    ApplyCityEditorChange(change);
                break;
            }
            case EditorCommand.SaveCity:
            {
                var change = _cityEditorPanel.SaveCurrent(_worldData, _hud.ShowMessage);
                if (change != null)
                    ApplyCityEditorChange(change);
                break;
            }
            case EditorCommand.DeleteCity:
            {
                var change = _cityEditorPanel.DeleteCurrent(_worldData, _hud.ShowMessage);
                if (change != null)
                    ApplyCityEditorChange(change);
                break;
            }
            case EditorCommand.NewProfession:
            {
                var change = _professionEditorPanel.CreateNew(_professionCatalog, _hud.ShowMessage);
                if (change != null)
                    ApplyProfessionEditorChange(change);
                break;
            }
            case EditorCommand.SaveProfession:
            {
                var change = _professionEditorPanel.SaveCurrent(_professionCatalog, _hud.ShowMessage);
                if (change != null)
                    ApplyProfessionEditorChange(change);
                break;
            }
            case EditorCommand.DeleteProfession:
            {
                var change = _professionEditorPanel.DeleteCurrent(_professionCatalog, _hud.ShowMessage);
                if (change != null)
                    ApplyProfessionEditorChange(change);
                break;
            }
            case EditorCommand.ReloadProfessions:
                ReloadProfessionCatalog();
                _hud.ShowMessage("Reloaded professions");
                break;
            case EditorCommand.NewNpc:
                _npcEditorPanel.CreateNew(_hud.ShowMessage);
                break;
            case EditorCommand.SaveNpcRoster:
                _npcEditorPanel.SaveCurrent(_hud.ShowMessage);
                break;
            case EditorCommand.DeleteNpc:
                _npcEditorPanel.DeleteCurrent(_hud.ShowMessage);
                break;
            case EditorCommand.ReloadNpcs:
                _npcEditorPanel.ReloadFromDisk();
                _hud.ShowMessage("Reloaded NPC rosters");
                break;
            case EditorCommand.SaveNpcTemplate:
                if (_npcEditorPanel.SaveSelectedAsTemplate(_hud.ShowMessage))
                    ReloadPrototypeManager();
                break;
            case EditorCommand.NewPrototype:
                if (_prototypeEditorPanel.CreateNew(_hud.ShowMessage))
                    ReloadPrototypeManager();
                break;
            case EditorCommand.SavePrototype:
                if (_prototypeEditorPanel.SaveCurrent(_hud.ShowMessage))
                    ReloadPrototypeManager();
                break;
            case EditorCommand.DeletePrototype:
                if (_prototypeEditorPanel.DeleteCurrent(_hud.ShowMessage))
                    ReloadPrototypeManager();
                break;
            case EditorCommand.ReloadPrototypes:
                _prototypeEditorPanel.ReloadFromDisk();
                ReloadPrototypeManager();
                _hud.ShowMessage("Reloaded prototypes");
                break;
            case EditorCommand.SaveGlobalSettings:
                if (_globalSettingsPanel.SaveCurrent(_worldData, _hud.ShowMessage))
                    PersistGlobalSettings();
                break;
        }
    }

    private void PersistGlobalSettings()
    {
        if (!_mapManager.SaveWorldData(_worldData))
        {
            _hud.ShowMessage("Failed to save world data");
            ReloadWorldDataFromDisk();
            _globalSettingsPanel.SyncFromWorldData(_worldData);
            return;
        }

        ReloadWorldDataFromDisk();
        _globalSettingsPanel.SyncFromWorldData(_worldData);
    }

    private void ReloadPrototypeManager()
    {
        _prototypes.LoadFromDirectory(ContentPaths.AbsolutePrototypesRoot);
        _palette.Refresh();
    }

    private void HandleWindowHotkeys(KeyboardState keys)
    {
    }

    private const int MinEditorWidth = 960;
    private const int MinEditorHeight = 640;

    private void SyncBackBufferToWindow()
    {
        if (_isApplyingWindowChange || _graphics.IsFullScreen)
            return;

        var width = Math.Max(MinEditorWidth, Window.ClientBounds.Width);
        var height = Math.Max(MinEditorHeight, Window.ClientBounds.Height);
        if (_graphics.PreferredBackBufferWidth == width && _graphics.PreferredBackBufferHeight == height)
        {
            _windowedSize = new Point(width, height);
            return;
        }

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
        var latestOrder = Math.Max(_history.UndoOrder, Math.Max(_entityPainter.UndoOrder, _resizeHistory.UndoOrder));
        if (latestOrder == long.MinValue)
            return;

        if (latestOrder == _resizeHistory.UndoOrder && _resizeHistory.TryUndo(out var resizeSnapshot) && resizeSnapshot != null)
        {
            ApplyMapSnapshot(resizeSnapshot);
            _hud.ShowMessage($"Undo resize: {_currentMap.Width}x{_currentMap.Height}");
            return;
        }

        if (latestOrder == _entityPainter.UndoOrder)
        {
            _entityPainter.TryUndo();
            return;
        }

        _history.Undo(_currentTileMap);
    }

    private void RedoLastAction()
    {
        var latestOrder = Math.Max(_history.RedoOrder, Math.Max(_entityPainter.RedoOrder, _resizeHistory.RedoOrder));
        if (latestOrder == long.MinValue)
            return;

        if (latestOrder == _resizeHistory.RedoOrder && _resizeHistory.TryRedo(out var resizeSnapshot) && resizeSnapshot != null)
        {
            ApplyMapSnapshot(resizeSnapshot);
            _hud.ShowMessage($"Redo resize: {_currentMap.Width}x{_currentMap.Height}");
            return;
        }

        if (latestOrder == _entityPainter.RedoOrder)
        {
            _entityPainter.TryRedo();
            return;
        }

        _history.Redo(_currentTileMap);
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
