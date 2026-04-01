using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.World;
using ECSWorld = MTEngine.ECS.World;

namespace MTEngine.Core;

public class GameEngine : Game
{
    public static GameEngine Instance { get; private set; } = null!;
    public EntityFactory EntityFactory { get; private set; } = null!;

    public GraphicsDeviceManager Graphics { get; }
    public SpriteBatch SpriteBatch { get; private set; } = null!;
    public Camera Camera { get; private set; } = null!;
    public AssetManager Assets { get; private set; } = null!;
    public PrototypeManager Prototypes { get; private set; } = new();

    public ECSWorld World { get; } = new();
    public EventBus EventBus { get; } = new();
    public InputManager Input { get; } = new();

    public TileMapRenderer TileMapRenderer { get; private set; } = new();
    public CollisionSystem CollisionSystem { get; private set; } = new();

    public GameEngine()
    {
        Instance = this;
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            IsFullScreen = false
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        Camera = new Camera(GraphicsDevice);

        ServiceLocator.Register(World);
        ServiceLocator.Register(EventBus);
        ServiceLocator.Register(Input);
        ServiceLocator.Register(Camera);

        World.AddSystem(TileMapRenderer);
        World.AddSystem(CollisionSystem);
        World.AddSystem(new Renderer());

        base.Initialize();
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        Assets = new AssetManager(Content, GraphicsDevice);
        EntityFactory = new EntityFactory(Assets, World);

        ServiceLocator.Register(EntityFactory);
        ServiceLocator.Register(SpriteBatch);
        ServiceLocator.Register(Assets);
        ServiceLocator.Register(Prototypes);
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Input.Update();
        World.Update(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        World.Draw();
        base.Draw(gameTime);
    }
}