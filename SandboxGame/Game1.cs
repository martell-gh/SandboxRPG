using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.World;
using SandboxGame.Game;
using SandboxGame.Save;
using SandboxGame.Settings;
using SandboxGame.Systems;
using SandboxGame.UI;

namespace SandboxGame;

public class Game1 : GameEngine
{
    private ConsoleCommands _commands = null!;
    private MapEntitySpawner _mapEntitySpawner = null!;
    private TriggerCheckSystem _triggerCheck = null!;
    private MenuSystem _menu = null!;
    private SaveGameManager _saveGame = null!;
    private GameSettings _settings = null!;
    private MapManager _mapManager = null!;
    private bool _gameInitialized;
    private KeyboardState _prevKb;

    protected override void LoadContent()
    {
        base.LoadContent();

        // Settings
        _settings = GameSettings.Load();
        DevConsole.DevMode = _settings.DevMode;
        ServiceLocator.Register<IKeyBindingSource>(_settings);
        ServiceLocator.Register<IUiScaleSource>(_settings);

        var font = Content.Load<SpriteFont>("DefaultFont");
        DevConsole.SetFont(font, GraphicsDevice);
        DevConsole.Log("Game started! Type 'help'.");

        InteractionSystem.SetFont(font);
        PopupTextSystem.SetFont(font);
        UIManager.SetFont(font);

        Window.TextInput += (_, e) =>
        {
            UIManager.OnTextInput(e.Character);
            _menu?.OnTextInput(e.Character);
        };

        Prototypes.LoadFromDirectory(GamePaths.Prototypes);

        _mapManager = new MapManager(GamePaths.Maps, Prototypes);
        ServiceLocator.Register(_mapManager);
        _saveGame = new SaveGameManager(World, _mapManager, Prototypes, Assets, Clock);
        ServiceLocator.Register(_saveGame);
        ServiceLocator.Register<IMapStateSource>(_saveGame);
        ServiceLocator.Register<IWorldStateTracker>(_saveGame);
        _mapEntitySpawner = new MapEntitySpawner(Prototypes, EntityFactory, World, EventBus);

        World.AddSystem(new PlayerMovementSystem());
        World.AddSystem(new MetabolismUI());
        World.AddSystem(new InGameMapsEditorSystem());

        _commands = new ConsoleCommands(this, _mapManager, TileMapRenderer);

        _triggerCheck = new TriggerCheckSystem();
        _triggerCheck.Initialize(_mapManager, LoadMapWithSessionPersistence, ConfirmLocationTransition);
        World.AddSystem(_triggerCheck);

        // Menu system
        _menu = new MenuSystem(_settings, Input);
        _menu.SetGraphics(GraphicsDevice, font);
        _menu.SaveSlotProvider = () => _saveGame.GetSlotSummaries();
        _menu.SaveNameSuggestionProvider = slotIndex => _saveGame.GetSuggestedSaveName(slotIndex);
        _menu.OnStartGame += HandleStartGame;
        _menu.OnLoadRiver += HandleLoadRiver;
        _menu.OnExitGame += HandleExitRequested;
        _menu.OnResumeGame += () => { };
        _menu.OnReturnToMainMenu += HandleReturnToMainMenu;
        _menu.OnSaveSlotConfirmed += HandleSaveSlotConfirmed;
        _menu.OnRenameSlotRequested += HandleRenameSlotRequested;
        _menu.OnDeleteSlotRequested += HandleDeleteSlotRequested;
        _menu.OnLoadSlotSelected += HandleLoadSlotSelected;
        _menu.OpenMainMenu();

        // Create player (but don't load a map yet)
        CreatePlayer();
    }

    private void CreatePlayer()
    {
        var playerProto = EntityPrototype.LoadFromFile(
            Path.Combine(GamePaths.Entities, "Player", "proto.json"));

        if (playerProto != null)
        {
            var player = EntityFactory.CreateFromPrototype(playerProto, new Vector2(200, 200));
            if (player != null)
                player.AddComponent(new PlayerTagComponent());
            DevConsole.Log("Player created from prototype.");
        }
        else
        {
            DevConsole.Log("Player prototype not found!");
        }

        World.Update(0f);
    }

    private void HandleStartGame()
    {
        ResetWorldState();
        _saveGame.StartNewGame();
        _gameInitialized = true;
        LoadMapWithSessionPersistence("river_startloc", "default");
        _menu.CloseMenu();
    }

    private void HandleLoadRiver()
    {
        ResetWorldState();
        _saveGame.StartNewGame();
        _gameInitialized = true;
        LoadMapWithSessionPersistence("river", "default");
        _menu.CloseMenu();
    }

    private void HandleReturnToMainMenu()
    {
        if (_gameInitialized && _saveGame.HasUnsavedChanges)
        {
            _menu.OpenConfirmation(
                "Несохранённые изменения",
                "Вернуться в главное меню и потерять текущий несохранённый прогресс?",
                ForceReturnToMainMenu,
                OpenPreviousMenuAfterCancelledLoad);
            return;
        }

        ForceReturnToMainMenu();
    }

    private void ForceReturnToMainMenu()
    {
        if (_saveGame.HasActiveSession)
            _saveGame.CaptureCurrentMapState();
        _gameInitialized = false;
        _menu.OpenMainMenu();
    }

    private void HandleSaveSlotConfirmed(int slotIndex, string saveName)
    {
        if (!_gameInitialized)
            return;

        _saveGame.SaveToSlot(slotIndex, saveName);
        _menu.ReturnToSaveSlots(slotIndex);
    }

    private void HandleLoadSlotSelected(int slotIndex)
    {
        void PerformLoad()
        {
            if (!_saveGame.LoadFromSlot(slotIndex))
                return;

            RestoreLoadedSession();
            _menu.CloseMenu();
        }

        if (_gameInitialized && _saveGame.HasUnsavedChanges)
        {
            _menu.OpenConfirmation(
                "Несохранённые изменения",
                "Загрузить слот и потерять текущий несохранённый прогресс?",
                PerformLoad,
                OpenPreviousMenuAfterCancelledLoad);
            return;
        }

        PerformLoad();
    }

    private void HandleRenameSlotRequested(int slotIndex, string newName)
    {
        if (!_saveGame.RenameSlot(slotIndex, newName))
            return;

        if (_menu.GameState == GameState.MainMenu)
            _menu.ReturnToLoadSlots(slotIndex);
        else
            _menu.ReturnToSaveSlots(slotIndex);
    }

    private void HandleDeleteSlotRequested(int slotIndex)
    {
        var returnToLoad = _menu.CurrentScreen == MenuScreen.LoadSlots || _menu.GameState == GameState.MainMenu;
        _menu.OpenConfirmation(
            "Удаление",
            $"Удалить сохранение из слота {slotIndex}?",
            () =>
            {
                if (_saveGame.DeleteSlot(slotIndex))
                {
                    if (returnToLoad)
                        _menu.ReturnToLoadSlots(slotIndex);
                    else
                        _menu.ReturnToSaveSlots(slotIndex);
                }
            },
            () =>
            {
                if (returnToLoad)
                    _menu.ReturnToLoadSlots(slotIndex);
                else
                    _menu.ReturnToSaveSlots(slotIndex);
            });
    }

    private void RestoreLoadedSession()
    {
        if (!_saveGame.HasActiveSession || string.IsNullOrWhiteSpace(_saveGame.ActiveSession?.CurrentMapId))
            return;

        _saveGame.RestorePlayerEntities();
        _saveGame.ApplyClockState();
        _commands.LoadMap(_saveGame.ActiveSession!.CurrentMapId, "default", placePlayerAtSpawn: false);
        _gameInitialized = true;
        CenterCameraOnPlayer();
    }

    private void ResetWorldState()
    {
        foreach (var entity in World.GetEntities().ToList())
            World.DestroyEntity(entity);
        World.Update(0f);
        _mapManager.ClearCurrentMap();
        CreatePlayer();
    }

    private void LoadMapWithSessionPersistence(string mapId, string spawnId)
    {
        if (_saveGame.HasActiveSession && _mapManager.CurrentMap != null)
            _saveGame.CaptureCurrentMapState();

        _commands.LoadMap(mapId, spawnId);

        if (_mapManager.CurrentMap != null)
            _saveGame.EnsureMapInstance(_mapManager.CurrentMap);
        if (_saveGame.HasActiveSession)
            _saveGame.MarkDirty();
    }

    private void ConfirmLocationTransition(TriggerZoneData trigger, Vector2 returnPosition, Action onConfirm, Action onCancel)
    {
        var targetMap = trigger.Action.TargetMapId ?? "unknown";
        _menu.OpenModalConfirmation(
            "Переход",
            $"Перейти в локацию {targetMap}?",
            onConfirm,
            onCancel);
    }

    private void CenterCameraOnPlayer()
    {
        var player = World.GetEntitiesWith<TransformComponent, PlayerTagComponent>().FirstOrDefault();
        var transform = player?.GetComponent<TransformComponent>();
        if (transform != null)
            Camera.Position = transform.Position;
    }

    private void HandleExitRequested()
    {
        if (_gameInitialized && _saveGame.HasUnsavedChanges)
        {
            _menu.OpenConfirmation(
                "Несохранённые изменения",
                "Выйти из игры и потерять текущий несохранённый прогресс?",
                Exit,
                OpenPreviousMenuAfterCancelledLoad);
            return;
        }

        Exit();
    }

    private void OpenPreviousMenuAfterCancelledLoad()
    {
        if (_menu.GameState == GameState.MainMenu)
            _menu.OpenMainMenu();
        else if (_menu.GameState == GameState.Paused)
            _menu.OpenPause();
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        DevConsole.Update();
        Input.Update();

        var kb = Keyboard.GetState();

        // Menu handles its own input
        if (_menu.CurrentScreen != MenuScreen.None)
        {
            _menu.Update();
            _prevKb = kb;
            return;
        }

        // Pause toggle (only while playing, no console/UI open)
        if (_menu.GameState == GameState.Playing && !DevConsole.IsOpen && !UIManager.AnyWindowOpen)
        {
            if (kb.IsKeyDown(_settings.GetKey("Pause")) && !_prevKb.IsKeyDown(_settings.GetKey("Pause")))
            {
                _menu.OpenPause();
                _prevKb = kb;
                return;
            }
        }

        _prevKb = kb;

        // Normal game update
        World.Update(dt);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_menu.CurrentScreen != MenuScreen.None)
        {
            // If game is initialized, draw the scene behind the menu
            if (_gameInitialized)
                base.Draw(gameTime);
            else
            {
                GraphicsDevice.Clear(Color.Black);
            }

            _menu.Draw(SpriteBatch);
            DevConsole.Draw();
            return;
        }

        base.Draw(gameTime);
        DevConsole.Draw();
    }
}
