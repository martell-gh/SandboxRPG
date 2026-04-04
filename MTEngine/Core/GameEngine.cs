using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Metabolism;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.UI;
using MTEngine.World;
using ECSWorld = MTEngine.ECS.World;

namespace MTEngine.Core;

public class GameEngine : Game
{
    public static GameEngine Instance { get; private set; } = null!;

    public GraphicsDeviceManager Graphics { get; }
    public SpriteBatch SpriteBatch { get; private set; } = null!;
    public Camera Camera { get; private set; } = null!;
    public AssetManager Assets { get; private set; } = null!;
    public PrototypeManager Prototypes { get; private set; } = new();
    public GameClock Clock { get; private set; } = new(8f);
    public EntityFactory EntityFactory { get; private set; } = null!;

    public ECSWorld World { get; } = new();
    public EventBus EventBus { get; } = new();
    public InputManager Input { get; } = new();

    public TileMapRenderer TileMapRenderer { get; private set; } = new();
    public CollisionSystem CollisionSystem { get; private set; } = new();
    public LightingSystem LightingSystem { get; private set; } = new();
    public DayNightSystem DayNightSystem { get; private set; } = new();
    public InteractionSystem InteractionSystem { get; private set; } = new();
    public PopupTextSystem PopupTextSystem { get; private set; } = new();
    public UIManager UIManager { get; private set; } = new();
    public HealthSystem HealthSystem { get; private set; } = new();
    public MetabolismSystem MetabolismSystem { get; private set; } = new();
    public SubstanceDebugSystem SubstanceDebugSystem { get; private set; } = new();
    public SubstanceWorkbenchSystem SubstanceWorkbenchSystem { get; private set; } = new();

    private RenderTarget2D? _sceneRT;
    private bool _isApplyingWindowChange;
    private Point _windowedSize = new(1280, 720);

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
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        Camera = new Camera(GraphicsDevice);
        Window.ClientSizeChanged += (_, _) => SyncBackBufferToWindow();

        ServiceLocator.Register(World);
        ServiceLocator.Register(EventBus);
        ServiceLocator.Register(Input);
        ServiceLocator.Register(Camera);
        ServiceLocator.Register(GraphicsDevice);
        ServiceLocator.Register(Clock);
        ServiceLocator.Register(PopupTextSystem);
        ServiceLocator.Register(UIManager);

        World.AddSystem(TileMapRenderer);
        World.AddSystem(CollisionSystem);
        World.AddSystem(DayNightSystem);
        World.AddSystem(new Renderer());
        World.AddSystem(LightingSystem);
        World.AddSystem(InteractionSystem);
        World.AddSystem(PopupTextSystem);
        World.AddSystem(HealthSystem);
        World.AddSystem(MetabolismSystem);
        World.AddSystem(SubstanceDebugSystem);
        World.AddSystem(SubstanceWorkbenchSystem);
        World.AddSystem(UIManager);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        Assets = new AssetManager(Content, GraphicsDevice);
        EntityFactory = new EntityFactory(Assets, World);

        ServiceLocator.Register(SpriteBatch);
        ServiceLocator.Register(Assets);
        ServiceLocator.Register(Prototypes);
        ServiceLocator.Register(EntityFactory);
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Input.Update();
        HandleWindowHotkeys();
        World.Update(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        EnsureSceneRT();

        // ── Шаг 1: Сцена → SceneRT ─────────────────────────────────
        GraphicsDevice.SetRenderTarget(_sceneRT);
        GraphicsDevice.Clear(Color.Black);
        World.DrawScene();

        // ── Шаг 2: Lightmap → LightRT (пока мы ещё НЕ на backbuffer) ──
        // Это критично — если сначала рисовать на backbuffer, а потом
        // переключиться на lightRT, backbuffer потеряет содержимое.
        LightingSystem.BuildLightMap(GraphicsDevice);

        // ── Шаг 3: Всё на backbuffer за один проход ─────────────────
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        // Рисуем сцену
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp);
        SpriteBatch.Draw(_sceneRT!, GraphicsDevice.Viewport.Bounds, Color.White);
        SpriteBatch.End();

        // Накладываем освещение поверх (multiply)
        LightingSystem.ApplyLightMap(GraphicsDevice);

        // ── Шаг 4: UI поверх (без освещения) ────────────────────────
        World.DrawOverlay();

        base.Draw(gameTime);
    }

    private void EnsureSceneRT()
    {
        var vp = GraphicsDevice.Viewport;
        if (_sceneRT != null && _sceneRT.Width == vp.Width && _sceneRT.Height == vp.Height)
            return;

        _sceneRT?.Dispose();
        _sceneRT = new RenderTarget2D(GraphicsDevice, vp.Width, vp.Height);
    }

    private void HandleWindowHotkeys()
    {
        var altDown = Input.IsDown(Keys.LeftAlt) || Input.IsDown(Keys.RightAlt);
        if (Input.IsPressed(Keys.F11) || (altDown && Input.IsPressed(Keys.Enter)))
            ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!Graphics.IsFullScreen)
        {
            _windowedSize = new Point(Window.ClientBounds.Width, Window.ClientBounds.Height);
            Graphics.IsFullScreen = true;
        }
        else
        {
            Graphics.IsFullScreen = false;
            Graphics.PreferredBackBufferWidth = Math.Max(640, _windowedSize.X);
            Graphics.PreferredBackBufferHeight = Math.Max(360, _windowedSize.Y);
        }

        ApplyWindowChanges();
    }

    private void SyncBackBufferToWindow()
    {
        if (_isApplyingWindowChange || Graphics.IsFullScreen)
            return;

        var width = Math.Max(640, Window.ClientBounds.Width);
        var height = Math.Max(360, Window.ClientBounds.Height);
        if (Graphics.PreferredBackBufferWidth == width && Graphics.PreferredBackBufferHeight == height)
            return;

        _windowedSize = new Point(width, height);
        Graphics.PreferredBackBufferWidth = width;
        Graphics.PreferredBackBufferHeight = height;
        ApplyWindowChanges();
    }

    private void ApplyWindowChanges()
    {
        try
        {
            _isApplyingWindowChange = true;
            Graphics.ApplyChanges();
        }
        finally
        {
            _isApplyingWindowChange = false;
        }
    }
}
