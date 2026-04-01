using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.World;
using SandboxGame.Game;
using SandboxGame.Systems;

namespace SandboxGame;

public class Game1 : GameEngine
{
    private ConsoleCommands _commands = null!;

    protected override void LoadContent()
    {
        base.LoadContent();

        var font = Content.Load<SpriteFont>("DefaultFont");
        DevConsole.SetFont(font, GraphicsDevice);
        DevConsole.Log("Game started! Type 'help'.");

        InteractionSystem.SetFont(font);

        Prototypes.LoadFromDirectory(GamePaths.Tiles);

        var mapManager = new MapManager(GamePaths.Maps, Prototypes);
        ServiceLocator.Register(mapManager);

        World.AddSystem(new PlayerMovementSystem());
        _commands = new ConsoleCommands(this, mapManager, TileMapRenderer);

        // Игрок из прототипа
        var playerProto = EntityPrototype.LoadFromFile(
            Path.Combine(GamePaths.Entities, "Player/proto.json"));

        if (playerProto != null)
        {
            var player = EntityFactory.CreateFromPrototype(playerProto, new Vector2(200, 200));
            if (player != null)
            {
                // копируем компоненты из прототипа
                player.AddComponent(new PlayerTagComponent());

                // Небольшой ореол вокруг игрока
                /*player.AddComponent(new LightComponent
                {
                    Color = new Color(255, 240, 200),
                    Radius = 80f,
                    Intensity = 0.7f
                });*/
            }
            DevConsole.Log("Player created from prototype.");
        }
        else
        {
            DevConsole.Log("Player prototype not found!");
        }

        World.Update(0f);

        var maps = mapManager.GetAvailableMaps();
        if (maps.Count > 0) _commands.LoadMap(maps[0]);
        else DevConsole.Log("No maps! Create one in MTEditor.");
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        DevConsole.Update();
        Input.Update();
        World.Update(dt);
    }

    protected override void Draw(GameTime gameTime)
    {
        // GameEngine.Draw() делает всю работу по RT + освещению
        base.Draw(gameTime);
        DevConsole.Draw(); // поверх всего
    }
}