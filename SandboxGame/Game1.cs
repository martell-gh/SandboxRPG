using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.World;
using SandboxGame.Game;
using SandboxGame.Settings;
using SandboxGame.Systems;
using SandboxGame.UI;

namespace SandboxGame;

public class Game1 : GameEngine
{
    private ConsoleCommands _commands = null!;
    private MapEntitySpawner _mapEntitySpawner = null!;
    private MenuSystem _menu = null!;
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

        var font = Content.Load<SpriteFont>("DefaultFont");
        DevConsole.SetFont(font, GraphicsDevice);
        DevConsole.Log("Game started! Type 'help'.");

        InteractionSystem.SetFont(font);
        PopupTextSystem.SetFont(font);
        UIManager.SetFont(font);

        Window.TextInput += (_, e) => UIManager.OnTextInput(e.Character);

        Prototypes.LoadFromDirectory(GamePaths.Prototypes);

        _mapManager = new MapManager(GamePaths.Maps, Prototypes);
        ServiceLocator.Register(_mapManager);
        _mapEntitySpawner = new MapEntitySpawner(Prototypes, EntityFactory, World, EventBus);

        World.AddSystem(new PlayerMovementSystem());
        World.AddSystem(new MetabolismUI());
        _commands = new ConsoleCommands(this, _mapManager, TileMapRenderer);

        // Menu system
        _menu = new MenuSystem(_settings, Input);
        _menu.SetGraphics(GraphicsDevice, font);
        _menu.OnStartGame += HandleStartGame;
        _menu.OnLoadRiver += HandleLoadRiver;
        _menu.OnExitGame += Exit;
        _menu.OnResumeGame += () => { };
        _menu.OnReturnToMainMenu += () => { };
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
        // Пока ничего не делает — будет реализовано позже
    }

    private void HandleLoadRiver()
    {
        if (!_gameInitialized)
            _gameInitialized = true;

        _commands.LoadMap("river");
        _menu.CloseMenu();
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
