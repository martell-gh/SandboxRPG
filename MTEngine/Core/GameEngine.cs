using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Combat;
using MTEngine.Crafting;
using MTEngine.Metabolism;
using MTEngine.Npc;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.UI;
using MTEngine.World;
using MTEngine.Wounds;
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
    public Calendar Calendar { get; protected set; } = new();
    public EntityFactory EntityFactory { get; private set; } = null!;

    public ECSWorld World { get; } = new();
    public EventBus EventBus { get; } = new();
    public InputManager Input { get; } = new();

    public TileMapRenderer TileMapRenderer { get; private set; } = new();
    public CollisionSystem CollisionSystem { get; private set; } = new();
    public LightingSystem LightingSystem { get; private set; } = new();
    public VisibilityOcclusionSystem VisibilityOcclusionSystem { get; private set; } = new();
    public DayNightSystem DayNightSystem { get; private set; } = new();
    public SleepSystem SleepSystem { get; private set; } = new();
    public InteractionSystem InteractionSystem { get; private set; } = new();
    public PopupTextSystem PopupTextSystem { get; private set; } = new();
    public SpeechBubbleSystem SpeechBubbleSystem { get; private set; } = new();
    public UIManager UIManager { get; private set; } = new();
    public HealthSystem HealthSystem { get; private set; } = new();
    public HealthOverlaySystem HealthOverlaySystem { get; private set; } = new();
    public MetabolismSystem MetabolismSystem { get; private set; } = new();
    public SubstanceDebugSystem SubstanceDebugSystem { get; private set; } = new();
    public SubstanceWorkbenchSystem SubstanceWorkbenchSystem { get; private set; } = new();
    public WoundSystem WoundSystem { get; private set; } = new();
    public CombatSystem CombatSystem { get; private set; } = new();
    public SkillUiSystem SkillUiSystem { get; private set; } = new();
    public CraftingSystem CraftingSystem { get; private set; } = new();
    public AgingSystem AgingSystem { get; private set; } = new();
    public MatchmakingSystem MatchmakingSystem { get; private set; } = new();
    public RelationshipTickSystem RelationshipTickSystem { get; private set; } = new();
    public PlayerCohabitationSystem PlayerCohabitationSystem { get; private set; } = new();
    public KinSyncSystem KinSyncSystem { get; private set; } = new();
    public RevengeSystem RevengeSystem { get; private set; } = new();
    public AvengerSystem AvengerSystem { get; private set; } = new();
    public ScheduleSystem ScheduleSystem { get; private set; } = new();
    public PregnancyPlanningSystem PregnancyPlanningSystem { get; private set; } = new();
    public BirthSystem BirthSystem { get; private set; } = new();
    public ChildGrowthSystem ChildGrowthSystem { get; private set; } = new();
    public JobMarketSystem JobMarketSystem { get; private set; } = new();
    public ProfessionTickSystem ProfessionTickSystem { get; private set; } = new();
    public WorldCatchupSystem WorldCatchupSystem { get; private set; } = new();
    public ShopRestockSystem ShopRestockSystem { get; private set; } = new();
    public TradeSystem TradeSystem { get; private set; } = new();
    public InnRentalSystem InnRentalSystem { get; private set; } = new();
    public SimulationLodSystem SimulationLodSystem { get; private set; } = new();
    public HomeIntrusionSystem HomeIntrusionSystem { get; private set; } = new();
    public CombatThreatSystem CombatThreatSystem { get; private set; } = new();
    public NpcCombatReactionSystem NpcCombatReactionSystem { get; private set; } = new();
    public NpcHealingSystem NpcHealingSystem { get; private set; } = new();
    public NpcLocationTravelSystem NpcLocationTravelSystem { get; private set; } = new();
    public NpcMovementSystem NpcMovementSystem { get; private set; } = new();
    public WorldRegistry WorldRegistry { get; private set; } = new();
    public WorldPopulationStore WorldPopulation { get; private set; } = new();
    public LocationGraph LocationGraph { get; private set; } = new();

    private RenderTarget2D? _sceneRT;
    private bool _isApplyingWindowChange;
    private Point _windowedSize = new(1280, 720);

    public const int MinResolutionWidth = 640;
    public const int MinResolutionHeight = 360;

    public GameEngine()
    {
        Instance = this;
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            IsFullScreen = false,
            // Borderless fullscreen — avoids the SDL2/Cocoa NSWindowStyleMask
            // crash that hits when the hardware mode switch fires on macOS.
            HardwareModeSwitch = false
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
        ServiceLocator.Register(Calendar);
        ServiceLocator.Register(WorldRegistry);
        ServiceLocator.Register(WorldPopulation);
        ServiceLocator.Register(LocationGraph);
        ServiceLocator.Register(SimulationLodSystem);
        ServiceLocator.Register(SleepSystem);
        ServiceLocator.Register(PopupTextSystem);
        ServiceLocator.Register(SpeechBubbleSystem);
        ServiceLocator.Register(UIManager);

        World.AddSystem(TileMapRenderer);
        World.AddSystem(CollisionSystem);
        World.AddSystem(DayNightSystem);
        World.AddSystem(SleepSystem);
        World.AddSystem(new Renderer());
        World.AddSystem(VisibilityOcclusionSystem);
        World.AddSystem(LightingSystem);
        World.AddSystem(InteractionSystem);
        World.AddSystem(TradeSystem);
        World.AddSystem(PopupTextSystem);
        World.AddSystem(SpeechBubbleSystem);
        World.AddSystem(WoundSystem);
        World.AddSystem(CombatSystem);
        World.AddSystem(new RangedCombatSystem());
        World.AddSystem(SkillUiSystem);
        World.AddSystem(CraftingSystem);
        World.AddSystem(HealthSystem);
        World.AddSystem(HealthOverlaySystem);
        World.AddSystem(MetabolismSystem);
        World.AddSystem(SubstanceDebugSystem);
        World.AddSystem(SubstanceWorkbenchSystem);
        World.AddSystem(AgingSystem);
        World.AddSystem(MatchmakingSystem);
        World.AddSystem(RelationshipTickSystem);
        World.AddSystem(PlayerCohabitationSystem);
        World.AddSystem(KinSyncSystem);
        World.AddSystem(RevengeSystem);
        World.AddSystem(PregnancyPlanningSystem);
        World.AddSystem(BirthSystem);
        World.AddSystem(ChildGrowthSystem);
        World.AddSystem(JobMarketSystem);
        World.AddSystem(ProfessionTickSystem);
        World.AddSystem(WorldCatchupSystem);
        World.AddSystem(ShopRestockSystem);
        World.AddSystem(InnRentalSystem);
        World.AddSystem(SimulationLodSystem);
        World.AddSystem(ScheduleSystem);
        World.AddSystem(HomeIntrusionSystem);
        World.AddSystem(CombatThreatSystem);
        World.AddSystem(NpcCombatReactionSystem);
        World.AddSystem(AvengerSystem);
        World.AddSystem(NpcHealingSystem);
        World.AddSystem(NpcLocationTravelSystem);
        World.AddSystem(NpcMovementSystem);
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
        var fullscreenKey = ServiceLocator.Has<IKeyBindingSource>()
            ? ServiceLocator.Get<IKeyBindingSource>().GetKey("Fullscreen")
            : Keys.F11;
        if (Input.IsPressed(fullscreenKey) || (altDown && Input.IsPressed(Keys.Enter)))
            ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!Graphics.IsFullScreen)
        {
            _windowedSize = GetWindowClientSize();
            Graphics.HardwareModeSwitch = false;
            var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            Graphics.PreferredBackBufferWidth = Math.Max(MinResolutionWidth, display.Width);
            Graphics.PreferredBackBufferHeight = Math.Max(MinResolutionHeight, display.Height);
            Graphics.IsFullScreen = true;
        }
        else
        {
            Graphics.IsFullScreen = false;
            Graphics.PreferredBackBufferWidth = Math.Max(MinResolutionWidth, _windowedSize.X);
            Graphics.PreferredBackBufferHeight = Math.Max(MinResolutionHeight, _windowedSize.Y);
        }

        ApplyWindowChanges();
    }

    private void SyncBackBufferToWindow()
    {
        if (_isApplyingWindowChange || Graphics.IsFullScreen)
            return;

        var size = GetWindowClientSize();
        var width = Math.Max(MinResolutionWidth, size.X);
        var height = Math.Max(MinResolutionHeight, size.Y);
        if (Graphics.PreferredBackBufferWidth == width && Graphics.PreferredBackBufferHeight == height)
            return;

        _windowedSize = new Point(
            Math.Max(MinResolutionWidth, size.X),
            Math.Max(MinResolutionHeight, size.Y));
        Graphics.PreferredBackBufferWidth = width;
        Graphics.PreferredBackBufferHeight = height;
        ApplyWindowChanges();
    }

    /// <summary>
    /// Switch to a windowed back buffer of the requested size, expressed in
    /// OS window points. On HiDPI displays SDL/MonoGame will provide a denser
    /// drawable automatically; we keep the configuration path in one space.
    /// </summary>
    public void SetWindowedResolution(int width, int height)
    {
        var clampedWidth = Math.Max(MinResolutionWidth, width);
        var clampedHeight = Math.Max(MinResolutionHeight, height);
        _windowedSize = new Point(clampedWidth, clampedHeight);

        if (!Graphics.IsFullScreen &&
            Graphics.PreferredBackBufferWidth == clampedWidth &&
            Graphics.PreferredBackBufferHeight == clampedHeight)
        {
            return;
        }

        Graphics.IsFullScreen = false;
        Graphics.PreferredBackBufferWidth = clampedWidth;
        Graphics.PreferredBackBufferHeight = clampedHeight;
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

    public Point GetUiClientSize()
    {
        return GetWindowClientSize();
    }

    public Vector2 GetWindowDensity()
    {
        return Vector2.One;
    }

    public Rectangle GetUiLogicalBounds(float uiScale = 1f)
    {
        var size = GetUiClientSize();
        var scale = Math.Max(0.01f, uiScale);
        return new Rectangle(
            0,
            0,
            Math.Max(1, (int)MathF.Round(size.X / scale)),
            Math.Max(1, (int)MathF.Round(size.Y / scale)));
    }

    public Matrix GetUiTransform(float uiScale = 1f)
    {
        var scale = Math.Max(0.01f, uiScale);
        return Matrix.CreateScale(scale, scale, 1f);
    }

    public Point ScreenToUi(Point pixelPoint, float uiScale)
    {
        var scale = Math.Max(0.01f, uiScale);
        return new Point(
            (int)MathF.Round(pixelPoint.X / scale),
            (int)MathF.Round(pixelPoint.Y / scale));
    }

    public Point GetWindowSizeForPixelSize(Point pixelSize)
    {
        return new Point(
            Math.Max(MinResolutionWidth, pixelSize.X),
            Math.Max(MinResolutionHeight, pixelSize.Y));
    }

    public Point GetPixelSizeForWindowSize(Point windowSize)
    {
        return new Point(
            Math.Max(MinResolutionWidth, windowSize.X),
            Math.Max(MinResolutionHeight, windowSize.Y));
    }

    public Point GetNativeDisplayPixelSize()
    {
        var currentDisplay = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        return new Point(
            Math.Max(MinResolutionWidth, currentDisplay.Width),
            Math.Max(MinResolutionHeight, currentDisplay.Height));
    }

    private Point GetWindowClientSize()
    {
        var client = Window.ClientBounds;
        if (client.Width > 0 && client.Height > 0)
            return new Point(client.Width, client.Height);

        var vp = GraphicsDevice.Viewport;
        return new Point(
            Math.Max(1, vp.Width),
            Math.Max(1, vp.Height));
    }
}
