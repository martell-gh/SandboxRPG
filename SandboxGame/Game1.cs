using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Npc;
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
    private NpcRosterSpawner _npcRosterSpawner = null!;
    private TriggerCheckSystem _triggerCheck = null!;
    private MenuSystem _menu = null!;
    private SaveGameManager _saveGame = null!;
    private GameSettings _settings = null!;
    private MapManager _mapManager = null!;
    private GodModeSystem _godMode = null!;
    private SpriteFont _hudFont = null!;
    private bool _gameInitialized;
    private KeyboardState _prevKb;
    private PlayerCharacterDraft _currentCharacterDraft = new();

    protected override void LoadContent()
    {
        base.LoadContent();

        // Settings
        _settings = GameSettings.Load();
        DevConsole.DevMode = _settings.DevMode;

        var localization = new LocalizationManager();
        localization.Load(ContentPaths.AbsoluteLocalizationRoot);
        localization.SetLanguage(_settings.LocalizationId);
        ServiceLocator.Register(localization);

        ServiceLocator.Register<IKeyBindingSource>(_settings);
        ServiceLocator.Register<IUiScaleSource>(_settings);
        ApplyResolution(_settings.ScreenWidth, _settings.ScreenHeight);

        var font = Content.Load<SpriteFont>("DefaultFont");
        _hudFont = font;
        DevConsole.SetFont(font, GraphicsDevice);
        DevConsole.Log("Game started! Type 'help'.");

        InteractionSystem.SetFont(font);
        TradeSystem.SetFont(font);
        PopupTextSystem.SetFont(font);
        SpeechBubbleSystem.SetFont(font);
        UIManager.SetFont(font);

        // Load UI theme
        var themePath = Path.Combine(GamePaths.UIWindows, "Theme.xml");
        UIManager.LoadTheme(themePath);

        Window.TextInput += (_, e) =>
        {
            UIManager.OnTextInput(e.Character);
            _menu?.OnTextInput(e.Character);
        };

        Prototypes.LoadFromDirectory(GamePaths.Prototypes);

        Calendar = Calendar.LoadFromFile(Path.Combine(GamePaths.Data, "calendar.json"));
        ServiceLocator.Register(Calendar);

        var scheduleTemplates = ScheduleTemplates.LoadFromFile(Path.Combine(GamePaths.Data, "schedule_templates.json"));
        ServiceLocator.Register(scheduleTemplates);

        var professionCatalog = ProfessionCatalog.Load(Path.Combine(GamePaths.Data, "professions.json"));
        ServiceLocator.Register(professionCatalog);

        _mapManager = new MapManager(GamePaths.Maps, Prototypes);
        ServiceLocator.Register(_mapManager);

        LocationGraph.Rebuild(_mapManager);

        WorldRegistry.RebuildFromMaps(_mapManager, Prototypes);
        WorldRegistry.RehydrateDynamicState();

        _saveGame = new SaveGameManager(World, _mapManager, Prototypes, Assets, Clock);
        _saveGame.RegisterSaveObject(Calendar);
        _saveGame.RegisterSaveObject(WorldRegistry);
        _saveGame.RegisterSaveObject(WorldPopulation);
        _saveGame.RegisterSaveObject(WorldCatchupSystem);
        _saveGame.RegisterSaveObject(InnRentalSystem);
        ServiceLocator.Register(_saveGame);
        ServiceLocator.Register<IMapStateSource>(_saveGame);
        ServiceLocator.Register<IWorldStateTracker>(_saveGame);
        _mapEntitySpawner = new MapEntitySpawner(Prototypes, EntityFactory, World, EventBus);
        _npcRosterSpawner = new NpcRosterSpawner(Prototypes, EntityFactory, World, _mapManager, WorldRegistry, EventBus, GamePaths.Maps);

        World.AddSystem(new PlayerMovementSystem());
        World.AddSystem(new MetabolismUI());
        World.AddSystem(new InGameMapsEditorSystem());
        _godMode = new GodModeSystem();
        _godMode.Configure(_mapManager, LoadMapWithSessionPersistence, font);
        ServiceLocator.Register<IGodModeService>(_godMode);
        World.AddSystem(_godMode);

        _commands = new ConsoleCommands(this, _mapManager, TileMapRenderer, _godMode);

        _triggerCheck = new TriggerCheckSystem();
        _triggerCheck.Initialize(_mapManager, LoadMapWithSessionPersistence, ConfirmLocationTransition);
        World.AddSystem(_triggerCheck);

        // Menu system
        _menu = new MenuSystem(_settings, Input);
        _menu.SetGraphics(GraphicsDevice, font);
        _menu.SaveSlotProvider = () => _saveGame.GetSlotSummaries();
        _menu.SaveNameSuggestionProvider = slotIndex => _saveGame.GetSuggestedSaveName(slotIndex);
        _menu.MapCatalogProvider = BuildMapCatalogForMenu;
        _menu.HairOptionsProvider = BuildHairOptionsForMenu;
        _menu.StartLocationValidator = ValidateConfiguredStartLocation;
        _menu.CharacterPreview = new CharacterPreviewRenderer(
            playerProvider: () => World.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault(),
            applyDraft: ApplyPlayerAppearance,
            startingOutfitProvider: () => _mapManager.GetWorldData().StartingOutfit,
            prototypes: Prototypes,
            assets: Assets);
        _menu.OnStartGameRequested += HandleStartGameRequested;
        _menu.OnStartGameWithMap += HandleStartGameWithMap;
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
        CreatePlayer(_currentCharacterDraft);
    }

    private void CreatePlayer(PlayerCharacterDraft draft = null)
    {
        var playerProto = Prototypes.GetEntity("player");

        if (playerProto != null)
        {
            var player = EntityFactory.CreateFromPrototype(playerProto, new Vector2(200, 200));
            if (player != null)
            {
                player.AddComponent(new PlayerTagComponent());
                ApplyPlayerAppearance(player, draft ?? _currentCharacterDraft);
            }
            DevConsole.Log("Player created from prototype.");
        }
        else
        {
            DevConsole.Log("Player prototype not found!");
        }

        World.Update(0f);
    }

    private void ApplyPlayerAppearance(Entity player, PlayerCharacterDraft draft)
    {
        _currentCharacterDraft = draft.Clone();

        var identity = player.GetComponent<IdentityComponent>() ?? player.AddComponent(new IdentityComponent());
        identity.FirstName = string.IsNullOrWhiteSpace(identity.FirstName) ? "Player" : identity.FirstName;
        identity.FactionId = _mapManager?.GetWorldData().StartingFactionId?.Trim() ?? "";
        identity.Gender = string.Equals(draft.Gender, "Female", StringComparison.OrdinalIgnoreCase)
            ? Gender.Female
            : Gender.Male;

        var age = player.GetComponent<AgeComponent>() ?? player.AddComponent(new AgeComponent());
        age.InitialAgeYears = Math.Clamp(draft.AgeYears, PlayerCharacterDraft.MinAgeYears, PlayerCharacterDraft.MaxAgeYears);
        age.Years = age.InitialAgeYears;
        age.BirthDayIndex = ResolveBirthDayIndex(age.InitialAgeYears);
        age.IsPensioner = false;

        if (player.GetComponent<InfoComponent>() == null)
            player.AddComponent(new InfoComponent());

        var sprite = player.GetComponent<SpriteComponent>();
        if (sprite != null)
            sprite.ColorHex = string.IsNullOrWhiteSpace(draft.SkinColor) ? "#F0B99DFF" : draft.SkinColor;

        var hair = player.GetComponent<HairAppearanceComponent>() ?? player.AddComponent(new HairAppearanceComponent());
        hair.StyleId = draft.HairStyleId ?? "";
        hair.ColorHex = string.IsNullOrWhiteSpace(draft.HairColor) ? "#4C311FFF" : draft.HairColor;
        hair.Visible = !string.IsNullOrWhiteSpace(hair.StyleId);

        player.GetComponent<GenderedAppearanceComponent>()?.ApplyForGender(identity.Gender);
    }

    private long ResolveBirthDayIndex(int ageYears)
    {
        var calendar = ServiceLocator.Has<Calendar>() ? ServiceLocator.Get<Calendar>() : Calendar;
        var daysPerYear = Math.Max(1, calendar.DaysPerYear);
        return Math.Max(0L, Clock.DayIndex - (long)Math.Clamp(ageYears, PlayerCharacterDraft.MinAgeYears, PlayerCharacterDraft.MaxAgeYears) * daysPerYear);
    }

    private void HandleStartGameWithMap(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
            return;

        ResetWorldState();
        _saveGame.StartNewGame();
        WorldRegistry.ClearDynamicState();
        _gameInitialized = true;

        // Если выбрана карта-стартовая из Global Settings — берём настроенный spawn,
        // иначе fallback на "default".
        var worldData = _mapManager.GetWorldData();
        var spawnId = string.Equals(mapId, worldData.StartingMapId, StringComparison.OrdinalIgnoreCase)
                      && !string.IsNullOrWhiteSpace(worldData.StartingSpawnId)
            ? worldData.StartingSpawnId
            : "default";

        LoadMapWithSessionPersistence(mapId, spawnId);
        ApplyStartingOutfitToPlayer();
        _menu.CloseMenu();
    }

    private void HandleStartGameRequested(PlayerCharacterDraft draft)
    {
        var (ok, message) = ValidateConfiguredStartLocation();
        if (!ok)
        {
            DevConsole.Log(message);
            return;
        }

        var worldData = _mapManager.GetWorldData();
        var mapId = worldData.StartingMapId.Trim();
        var spawnId = string.IsNullOrWhiteSpace(worldData.StartingSpawnId)
            ? "default"
            : worldData.StartingSpawnId.Trim();

        _currentCharacterDraft = draft.Clone();
        ResetWorldState();
        _saveGame.StartNewGame();
        WorldRegistry.ClearDynamicState();
        _gameInitialized = true;

        LoadMapWithSessionPersistence(mapId, spawnId);
        ApplyStartingOutfitToPlayer();
        _menu.CloseMenu();
    }

    private (bool Ok, string Message) ValidateConfiguredStartLocation()
    {
        var worldData = _mapManager.GetWorldData();
        var mapId = worldData.StartingMapId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(mapId))
            return (false, "Стартовая карта не выбрана. Открой MTEditor -> Global Settings и выбери Starting Map.");

        var catalog = _mapManager.GetMapCatalog();
        var map = catalog.FirstOrDefault(entry => string.Equals(entry.Id, mapId, StringComparison.OrdinalIgnoreCase));
        if (map == null)
            return (false, $"Стартовая карта '{mapId}' не найдена в Maps/.");

        return (true, $"Старт: {map.Name} ({map.Id}) @ {FirstNonEmpty(worldData.StartingSpawnId, "default")}");
    }

    private IReadOnlyList<CharacterCreatorHairOption> BuildHairOptionsForMenu(string gender)
    {
        var npcGender = string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase)
            ? Gender.Female
            : Gender.Male;

        var result = new List<CharacterCreatorHairOption>
        {
            new() { Id = "", Label = "Без волос", Gender = "Unisex" }
        };

        result.AddRange(Prototypes.GetAllEntities()
            .Where(proto => HairAppearanceComponent.IsHairStylePrototype(proto, npcGender))
            .OrderBy(proto => proto.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(proto => proto.Id, StringComparer.OrdinalIgnoreCase)
            .Select(proto =>
            {
                var style = proto.Components?["hairStyle"] as System.Text.Json.Nodes.JsonObject;
                var display = style?["displayName"]?.GetValue<string>();
                var styleGender = style?["gender"]?.GetValue<string>() ?? "Unisex";
                return new CharacterCreatorHairOption
                {
                    Id = proto.Id,
                    Label = string.IsNullOrWhiteSpace(display)
                        ? (string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name)
                        : display!,
                    Gender = styleGender
                };
            }));

        return result;
    }

    private IReadOnlyList<MapCatalogEntry> BuildMapCatalogForMenu()
    {
        var catalog = _mapManager.GetMapCatalog();
        var startingMapId = _mapManager.GetWorldData().StartingMapId ?? "";
        if (string.IsNullOrWhiteSpace(startingMapId))
            return catalog;

        // Поднимаем стартовую карту наверх, чтобы курсор по умолчанию стоял на ней.
        return catalog
            .OrderByDescending(m => string.Equals(m.Id, startingMapId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private void ApplyStartingOutfitToPlayer()
    {
        var player = World.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();
        if (player == null)
            return;

        var equipment = player.GetComponent<EquipmentComponent>();
        if (equipment == null)
            return;

        var position = player.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;
        foreach (var (slotId, prototypeId) in _mapManager.GetWorldData().StartingOutfit)
        {
            if (string.IsNullOrWhiteSpace(slotId) || string.IsNullOrWhiteSpace(prototypeId))
                continue;

            var proto = Prototypes.GetEntity(prototypeId.Trim());
            if (proto == null)
            {
                DevConsole.Log($"Starting outfit prototype not found: {prototypeId}");
                continue;
            }

            var item = EntityFactory.CreateFromPrototype(proto, position);
            if (item == null || !equipment.CanEquip(item, slotId))
            {
                if (item != null)
                    World.DestroyEntity(item);
                DevConsole.Log($"Cannot equip starting outfit: {prototypeId} -> {slotId}");
                continue;
            }

            var slot = equipment.GetSlot(slotId);
            var itemComponent = item.GetComponent<ItemComponent>();
            if (slot == null || itemComponent == null)
            {
                World.DestroyEntity(item);
                continue;
            }

            slot.Item = item;
            itemComponent.ContainedIn = player;
            item.Active = false;
        }
    }

    private void HandleLoadRiver()
    {
        ResetWorldState();
        _saveGame.StartNewGame();
        WorldRegistry.ClearDynamicState();
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

        WorldRegistry.CaptureDynamicState();
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
        WorldRegistry.RehydrateDynamicState();
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
        CreatePlayer(_currentCharacterDraft);
    }

    private void LoadMapWithSessionPersistence(string mapId, string spawnId)
        => LoadMapWithSessionPersistence(mapId, spawnId, placePlayerAtSpawn: true);

    private void LoadMapWithSessionPersistence(string mapId, string spawnId, bool placePlayerAtSpawn)
    {
        var previousMapId = _mapManager.CurrentMap?.Id;
        var loadingFromBackground = string.IsNullOrWhiteSpace(previousMapId)
            || !string.Equals(previousMapId, mapId, StringComparison.OrdinalIgnoreCase);

        if (_saveGame.HasActiveSession && _mapManager.CurrentMap != null)
            _saveGame.CaptureCurrentMapState();

        _commands.LoadMap(mapId, spawnId, placePlayerAtSpawn);
        World.FlushEntityChanges();

        if (loadingFromBackground
            && _mapManager.CurrentMap != null
            && string.Equals(_mapManager.CurrentMap.Id, mapId, StringComparison.OrdinalIgnoreCase))
        {
            ScheduleSystem.SettleCurrentMapNpcsFromBackground();
        }

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
        var worldDt = InteractionSystem.RequestsInteractionSlowdown ? dt * 0.5f : dt;
        World.Update(worldDt);
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
        DrawClockHud();
        DevConsole.Draw();
    }

    private void DrawClockHud()
    {
        if (!_gameInitialized || _hudFont == null)
            return;

        var date = Calendar.FromTotalSeconds(Clock.TotalSecondsAbsolute);
        var month = date.Month >= 1 && date.Month <= Calendar.MonthNames.Count
            ? Calendar.MonthNames[date.Month - 1]
            : date.Month.ToString();
        var text = $"{date.Day} {month} {date.Year} г., {date.Hour:00}:{date.Minute:00}";
        var size = _hudFont.MeasureString(text);
        var viewport = GraphicsDevice.Viewport;
        var pos = new Vector2(
            MathF.Max(12f, viewport.Width - size.X - 18f),
            34f);

        SpriteBatch.Begin(samplerState: SamplerState.PointClamp);
        SpriteBatch.DrawString(_hudFont, text, pos + new Vector2(2f, 2f), Color.Black * 0.75f);
        SpriteBatch.DrawString(_hudFont, text, pos, Color.White);
        SpriteBatch.End();
    }

    private void ApplyResolution(int width, int height)
        => SetWindowedResolution(width, height);
}
