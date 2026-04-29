using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Npc;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.World;
using MTEngine.Wounds;

namespace MTEngine.Combat;

public class RangedCombatSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private sealed class ActorRangedState
    {
        public int WeaponEntityId;
        public float Cooldown;
        public bool BowCharging;
        public float BowChargeSeconds;
        public bool CrossbowLoaded;
        public bool CrossbowReloading;
        public float CrossbowReloadSeconds;
    }

    private readonly Dictionary<int, ActorRangedState> _states = new();
    private readonly Random _random = new();

    private InputManager? _input;
    private Camera? _camera;
    private MapManager? _mapManager;
    private PrototypeManager? _prototypes;
    private EntityFactory? _entityFactory;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;

    public override void Update(float deltaTime)
    {
        ResolveServices();
        UpdateProjectiles(deltaTime);
        UpdatePlayerRangedInput(deltaTime);
    }

    public override void Draw()
    {
        ResolveServices();
        if (_spriteBatch == null || _input == null || _pixel == null)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        if (player == null)
            return;

        var active = GetActiveRangedWeapon(player);
        if (active == null || active.Value.Component.Kind != RangedWeaponKind.Bow)
            return;

        var state = GetState(player.Id, active.Value.Item.Id);
        if (!state.BowCharging)
            return;

        var charge = GetBowCharge01(state, active.Value.Component);
        var mouse = _input.MousePosition.ToVector2();
        var radiusX = 9f + charge * 8f;
        var radiusY = 7f + charge * 3f;
        var color = Color.Lerp(new Color(210, 210, 170, 180), new Color(130, 235, 150, 220), charge);

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        DrawEllipse(mouse, radiusX, radiusY, color);
        _spriteBatch.End();
    }

    public static bool HasActiveRangedWeapon(Entity actor)
        => actor.GetComponent<HandsComponent>()?.ActiveItem?.GetComponent<RangedWeaponComponent>() != null;

    private void UpdatePlayerRangedInput(float deltaTime)
    {
        if (_input == null || _camera == null || _mapManager?.CurrentMap == null)
            return;
        if (DevConsole.IsOpen)
            return;
        if (ServiceLocator.Has<IGodModeService>() && ServiceLocator.Get<IGodModeService>().IsGodModeActive)
            return;
        if (ServiceLocator.Has<ITradeUiService>() && ServiceLocator.Get<ITradeUiService>().IsTradeOpen)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        if (player == null || player.GetComponent<HealthComponent>()?.IsDead == true)
            return;
        if (player.GetComponent<CombatModeComponent>()?.CombatEnabled != true)
            return;

        var active = GetActiveRangedWeapon(player);
        if (active == null)
            return;

        var state = GetState(player.Id, active.Value.Item.Id);
        state.Cooldown = Math.Max(0f, state.Cooldown - deltaTime);

        var weapon = active.Value.Component;
        if (weapon.Kind == RangedWeaponKind.Bow)
            UpdateBowInput(player, weapon, state, deltaTime);
        else
            UpdateCrossbowInput(player, weapon, state, deltaTime);
    }

    private void UpdateBowInput(Entity actor, RangedWeaponComponent weapon, ActorRangedState state, float deltaTime)
    {
        if (_input == null || _camera == null)
            return;

        if (_input.LeftClicked && state.Cooldown <= 0f)
        {
            state.BowCharging = true;
            state.BowChargeSeconds = 0f;
        }

        if (state.BowCharging && _input.LeftDown)
        {
            state.BowChargeSeconds += deltaTime;
            FaceAim(actor, _camera.ScreenToWorld(_input.MousePosition.ToVector2()));
            return;
        }

        if (state.BowCharging && _input.LeftReleased)
        {
            var charge = GetBowCharge01(state, weapon);
            state.BowCharging = false;
            state.BowChargeSeconds = 0f;
            Fire(actor, weapon, charge, rangeToCursor: false);
            state.Cooldown = Math.Max(0.12f, weapon.Cooldown);
        }
    }

    private void UpdateCrossbowInput(Entity actor, RangedWeaponComponent weapon, ActorRangedState state, float deltaTime)
    {
        if (_input == null || _camera == null)
            return;

        if (state.CrossbowReloading)
        {
            state.CrossbowReloadSeconds += deltaTime;
            if (state.CrossbowReloadSeconds >= Math.Max(0.05f, weapon.CrossbowReloadSeconds))
            {
                state.CrossbowReloading = false;
                state.CrossbowLoaded = true;
                state.CrossbowReloadSeconds = 0f;
                PopupTextSystem.Show(actor, "Заряжено", Color.LightGreen, lifetime: 0.9f);
            }
            return;
        }

        if (state.CrossbowLoaded && _input.LeftDown)
            OffsetCameraTowardAim(actor, _camera.ScreenToWorld(_input.MousePosition.ToVector2()));

        if (!_input.LeftClicked || state.Cooldown > 0f)
            return;

        if (!state.CrossbowLoaded)
        {
            state.CrossbowReloading = true;
            state.CrossbowReloadSeconds = 0f;
            PopupTextSystem.Show(actor, "Заряжаю...", Color.LightGoldenrodYellow, lifetime: 0.8f);
            return;
        }

        Fire(actor, weapon, 1f, rangeToCursor: true);
        state.CrossbowLoaded = false;
        state.Cooldown = Math.Max(0.12f, weapon.Cooldown);
    }

    private void Fire(Entity shooter, RangedWeaponComponent weapon, float power, bool rangeToCursor)
    {
        if (_camera == null || _input == null || _prototypes == null || _entityFactory == null)
            return;

        var transform = shooter.GetComponent<TransformComponent>();
        if (transform == null)
            return;

        var aimWorld = _camera.ScreenToWorld(_input.MousePosition.ToVector2());
        var direction = aimWorld - transform.Position;
        if (direction.LengthSquared() < 0.001f)
            direction = new Vector2(1f, 0f);
        direction.Normalize();

        direction = ApplyAccuracySpread(shooter, weapon, direction, power);
        FaceAim(shooter, transform.Position + direction);

        var cursorDistance = Vector2.Distance(transform.Position, aimWorld);
        var range = rangeToCursor
            ? Math.Min(Math.Max(24f, cursorDistance), weapon.MaxRange)
            : Math.Max(32f, weapon.MaxRange * power);
        var speed = Math.Max(64f, weapon.ProjectileSpeed * (weapon.Kind == RangedWeaponKind.Bow ? MathHelper.Lerp(0.45f, 1f, power) : 1f));
        var damage = RollDamage(weapon) * (weapon.Kind == RangedWeaponKind.Bow ? MathHelper.Lerp(0.55f, 1f, power) : 1f);
        damage *= SkillComponent.GetDamageMultiplier(SkillType.RangedWeapons, shooter.GetComponent<SkillComponent>()?.RangedWeapons ?? 0f);

        var proto = _prototypes.GetEntity(weapon.ProjectilePrototypeId);
        if (proto == null)
        {
            PopupTextSystem.Show(shooter, $"Нет projectile: {weapon.ProjectilePrototypeId}", Color.IndianRed, lifetime: 1.3f);
            return;
        }

        var spawn = transform.Position + direction * 18f;
        var projectile = _entityFactory.CreateFromPrototype(proto, spawn);
        if (projectile == null)
            return;

        projectile.Name = weapon.Kind == RangedWeaponKind.Crossbow ? "Болт" : "Стрела";
        var projectileTf = projectile.GetComponent<TransformComponent>() ?? projectile.AddComponent(new TransformComponent(spawn));
        projectileTf.Position = spawn;
        projectileTf.Rotation = MathF.Atan2(direction.Y, direction.X);

        var component = projectile.GetComponent<RangedProjectileComponent>() ?? projectile.AddComponent(new RangedProjectileComponent());
        component.ShooterEntityId = shooter.Id;
        component.Velocity = direction * speed;
        component.RemainingRange = range;
        component.Damage = damage;
        component.DamageType = weapon.DamageType;
        component.SourceName = weapon.Kind == RangedWeaponKind.Crossbow ? "Арбалетный болт" : "Стрела";
    }

    private void UpdateProjectiles(float deltaTime)
    {
        ResolveServices();
        var tileMap = _mapManager?.CurrentTileMap;
        if (tileMap == null)
            return;

        foreach (var entity in World.GetEntitiesWith<RangedProjectileComponent, TransformComponent>().ToList())
        {
            var projectile = entity.GetComponent<RangedProjectileComponent>()!;
            var transform = entity.GetComponent<TransformComponent>()!;

            if (projectile.Stuck)
            {
                projectile.StuckSeconds += deltaTime;
                if (projectile.StuckSeconds >= projectile.StuckLifetime)
                    World.DestroyEntity(entity);
                continue;
            }

            var stepDistance = projectile.Velocity.Length() * deltaTime;
            if (stepDistance <= 0.001f)
            {
                Stick(entity, projectile);
                continue;
            }

            var from = transform.Position;
            var to = from + projectile.Velocity * deltaTime;
            var steps = Math.Max(1, (int)MathF.Ceiling(stepDistance / 6f));

            for (var i = 1; i <= steps; i++)
            {
                var sample = Vector2.Lerp(from, to, i / (float)steps);
                var moved = Vector2.Distance(transform.Position, sample);
                projectile.TravelledDistance += moved;
                projectile.RemainingRange -= moved;
                transform.Position = sample;

                if (HitsSolidTile(tileMap, sample) || HitsBlockingEntity(entity, sample))
                {
                    Stick(entity, projectile);
                    break;
                }

                var target = FindLivingTarget(entity, projectile, sample);
                if (target != null)
                {
                    HitTarget(entity, projectile, target);
                    break;
                }

                if (projectile.RemainingRange <= 0f)
                {
                    Stick(entity, projectile);
                    break;
                }
            }
        }
    }

    private void HitTarget(Entity projectileEntity, RangedProjectileComponent projectile, Entity target)
    {
        var shooter = World.GetEntities().FirstOrDefault(entity => entity.Id == projectile.ShooterEntityId);
        var targetHealth = target.GetComponent<HealthComponent>();
        var targetWounds = target.GetComponent<WoundComponent>();
        var damage = CalculateFinalProjectileDamage(target, projectile.DamageType, projectile.Damage);

        if (damage > 0.05f)
        {
            if (targetWounds != null)
                WoundComponent.ApplyDamage(target, projectile.DamageType, damage);
            else if (targetHealth != null)
            {
                targetHealth.Health = Math.Max(0f, targetHealth.Health - damage);
                DamageFlashComponent.Trigger(target);
            }

            if (target.GetComponent<TrainingDummyComponent>() != null && targetHealth != null)
                targetHealth.Health = targetHealth.MaxHealth;

            PopupTextSystem.Show(target, $"-{damage:F0} {GetDamageLabel(projectile.DamageType)}", Color.IndianRed, lifetime: 1.25f);
            var livingTarget = target.HasComponent<NpcTagComponent>() || target.HasComponent<PlayerTagComponent>();
            if (livingTarget)
                target.GetComponent<SkillComponent>()?.Improve(SkillType.Fortitude, Math.Max(0.04f, damage * 0.012f));

            if (shooter != null)
            {
                if (livingTarget)
                {
                    var xp = 0.08f + MathHelper.Clamp(projectile.TravelledDistance / 360f, 0f, 2.2f) * 0.09f;
                    shooter.GetComponent<SkillComponent>()?.Improve(SkillType.RangedWeapons, xp);
                }

                if (ServiceLocator.Has<EventBus>())
                {
                    ServiceLocator.Get<EventBus>().Publish(new EntityDamagedEvent(
                        shooter,
                        target,
                        damage,
                        projectile.DamageType,
                        targetHealth?.IsDead ?? false)
                    {
                        IsWeaponAttack = true,
                        IsRangedAttack = true
                    });
                }
            }

            MarkWorldDirty();
        }

        Stick(projectileEntity, projectile);
    }

    private float CalculateFinalProjectileDamage(Entity target, DamageType type, float baseDamage)
    {
        var fortitudeMultiplier = target.GetComponent<SkillComponent>() is { } skills
            ? SkillComponent.GetFortitudeDamageTakenMultiplier(skills.Fortitude)
            : 1.12f;
        var armorMultiplier = target.GetComponent<EquipmentComponent>() is { } equipment
            ? 1f - MathHelper.Clamp(equipment.GetArmorResistance(type), 0f, 0.65f)
            : 1f;

        return Math.Max(0f, baseDamage * fortitudeMultiplier * armorMultiplier);
    }

    private Entity? FindLivingTarget(Entity projectileEntity, RangedProjectileComponent projectile, Vector2 point)
    {
        foreach (var target in World.GetEntitiesWith<TransformComponent>())
        {
            if (target == projectileEntity || target.Id == projectile.ShooterEntityId)
                continue;
            if (target.GetComponent<HealthComponent>()?.IsDead == true)
                continue;
            if (target.GetComponent<HealthComponent>() == null
                && target.GetComponent<WoundComponent>() == null
                && target.GetComponent<TrainingDummyComponent>() == null)
            {
                continue;
            }

            if (GetEntityHitBounds(target).Contains(point))
                return target;
        }

        return null;
    }

    private static Rectangle GetEntityHitBounds(Entity entity)
    {
        var tf = entity.GetComponent<TransformComponent>();
        var collider = entity.GetComponent<ColliderComponent>();
        if (tf != null && collider != null)
            return collider.GetBounds(tf.Position);

        var sprite = entity.GetComponent<SpriteComponent>();
        var w = sprite?.SourceRect?.Width ?? sprite?.Width ?? 28;
        var h = sprite?.SourceRect?.Height ?? sprite?.Height ?? 28;
        var pos = tf?.Position ?? Vector2.Zero;
        return new Rectangle((int)(pos.X - w / 2f), (int)(pos.Y - h / 2f), w, h);
    }

    private bool HitsBlockingEntity(Entity projectileEntity, Vector2 point)
    {
        foreach (var entity in World.GetEntities())
        {
            if (entity == projectileEntity || !EntityOcclusionHelper.IsMovementBlocker(entity))
                continue;

            if (EntityOcclusionHelper.TryGetBlockerBounds(entity, out var bounds) && bounds.Contains(point))
                return true;
        }

        return false;
    }

    private static bool HitsSolidTile(TileMap tileMap, Vector2 point)
    {
        var tile = tileMap.WorldToTile(point);
        return !tileMap.IsInBounds(tile.X, tile.Y) || tileMap.IsSolid(tile.X, tile.Y);
    }

    private static void Stick(Entity projectileEntity, RangedProjectileComponent projectile)
    {
        projectile.Stuck = true;
        projectile.StuckSeconds = 0f;
        projectile.Velocity = Vector2.Zero;
        projectileEntity.GetComponent<SpriteComponent>()?.PlayClip("idle");
    }

    private Vector2 ApplyAccuracySpread(Entity shooter, RangedWeaponComponent weapon, Vector2 direction, float power)
    {
        var skill = shooter.GetComponent<SkillComponent>()?.RangedWeapons ?? 0f;
        var skillT = MathHelper.Clamp(skill / 100f, 0f, 1f);
        var accuracyBonus = SkillComponent.GetAccuracyOffset(SkillType.RangedWeapons, skill);
        var spread = Math.Max(0.35f, weapon.SpreadDegrees * (1f - skillT * 0.72f));
        spread *= MathHelper.Lerp(1.35f, 0.88f, MathHelper.Clamp(weapon.Accuracy + accuracyBonus, 0.05f, 1f));
        spread *= MathHelper.Lerp(1.35f, 1f, power);

        var radians = MathHelper.ToRadians(spread);
        var offset = ((float)_random.NextDouble() * 2f - 1f) * radians;
        var cos = MathF.Cos(offset);
        var sin = MathF.Sin(offset);
        return Vector2.Normalize(new Vector2(
            direction.X * cos - direction.Y * sin,
            direction.X * sin + direction.Y * cos));
    }

    private float RollDamage(RangedWeaponComponent weapon)
    {
        var min = MathF.Min(weapon.MinDamage, weapon.MaxDamage);
        var max = MathF.Max(weapon.MinDamage, weapon.MaxDamage);
        return Math.Abs(max - min) <= 0.001f
            ? min
            : MathHelper.Lerp(min, max, (float)_random.NextDouble());
    }

    private static void FaceAim(Entity actor, Vector2 aimWorld)
    {
        var tf = actor.GetComponent<TransformComponent>();
        var sprite = actor.GetComponent<SpriteComponent>();
        if (tf == null || sprite == null)
            return;

        var dir = aimWorld - tf.Position;
        if (dir.LengthSquared() > 0.001f)
            sprite.PlayDirectionalIdle(dir);
    }

    private void OffsetCameraTowardAim(Entity actor, Vector2 aimWorld)
    {
        if (_camera == null)
            return;

        var tf = actor.GetComponent<TransformComponent>();
        if (tf == null)
            return;

        var dir = aimWorld - tf.Position;
        if (dir.LengthSquared() < 0.001f)
            return;

        dir.Normalize();
        _camera.Follow(tf.Position + dir * 38f, 0.12f);
    }

    private ActorRangedState GetState(int actorId, int weaponId)
    {
        if (!_states.TryGetValue(actorId, out var state))
        {
            state = new ActorRangedState { WeaponEntityId = weaponId };
            _states[actorId] = state;
        }

        if (state.WeaponEntityId != weaponId)
        {
            state.WeaponEntityId = weaponId;
            state.BowCharging = false;
            state.BowChargeSeconds = 0f;
            state.CrossbowLoaded = false;
            state.CrossbowReloading = false;
            state.CrossbowReloadSeconds = 0f;
            state.Cooldown = 0f;
        }

        return state;
    }

    private static float GetBowCharge01(ActorRangedState state, RangedWeaponComponent weapon)
    {
        var draw = Math.Max(0.05f, weapon.BowDrawSeconds);
        var t = MathHelper.Clamp(state.BowChargeSeconds / draw, 0f, 1f);
        return MathHelper.Lerp(MathHelper.Clamp(weapon.BowMinPower, 0.05f, 1f), 1f, t);
    }

    private (Entity Item, RangedWeaponComponent Component)? GetActiveRangedWeapon(Entity actor)
    {
        var item = actor.GetComponent<HandsComponent>()?.ActiveItem;
        var ranged = item?.GetComponent<RangedWeaponComponent>();
        if (item == null || ranged == null)
            return null;

        return (item, ranged);
    }

    private void DrawEllipse(Vector2 center, float radiusX, float radiusY, Color color)
    {
        const int segments = 32;
        var previous = center + new Vector2(radiusX, 0f);
        for (var i = 1; i <= segments; i++)
        {
            var a = MathHelper.TwoPi * i / segments;
            var next = center + new Vector2(MathF.Cos(a) * radiusX, MathF.Sin(a) * radiusY);
            DrawLine(previous, next, color, 2f);
            previous = next;
        }
    }

    private void DrawLine(Vector2 from, Vector2 to, Color color, float thickness)
    {
        if (_spriteBatch == null || _pixel == null)
            return;

        var delta = to - from;
        var length = delta.Length();
        if (length <= 0.01f)
            return;

        _spriteBatch.Draw(
            _pixel,
            from,
            null,
            color,
            MathF.Atan2(delta.Y, delta.X),
            Vector2.Zero,
            new Vector2(length, thickness),
            SpriteEffects.None,
            0f);
    }

    private static string GetDamageLabel(DamageType type) => type switch
    {
        DamageType.Slash => "порезы",
        DamageType.Blunt => "ушибы",
        DamageType.Burn => "ожоги",
        DamageType.Exhaustion => "истощение",
        _ => "урон"
    };

    private void ResolveServices()
    {
        _input ??= ServiceLocator.Has<InputManager>() ? ServiceLocator.Get<InputManager>() : null;
        _camera ??= ServiceLocator.Has<Camera>() ? ServiceLocator.Get<Camera>() : null;
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _prototypes ??= ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        _entityFactory ??= ServiceLocator.Has<EntityFactory>() ? ServiceLocator.Get<EntityFactory>() : null;
        _spriteBatch ??= ServiceLocator.Has<SpriteBatch>() ? ServiceLocator.Get<SpriteBatch>() : null;

        if (_pixel == null && ServiceLocator.Has<GraphicsDevice>())
        {
            _pixel = new Texture2D(ServiceLocator.Get<GraphicsDevice>(), 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
