using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.Wounds;

namespace MTEngine.Combat;

public class CombatSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;
    private const float SpamPenaltyWindow = 0.12f;
    private const float SpamPenaltyDamageDivisor = 1.5f;
    private const float SpamPenaltyMissMultiplier = 1.2f;

    private readonly Random _random = new();
    private static readonly AttackProfile UnarmedAttack = new(
        DamageType.Blunt,
        6f,
        10f,
        38f,
        0.34f,
        0.88f,
        "Ударить",
        "Кулак",
        SkillType.HandToHand,
        true);
    private readonly List<SwingState> _swings = new();
    private readonly Dictionary<int, float> _cooldowns = new();
    private readonly Dictionary<int, float> _readyTimers = new();
    private SpriteBatch? _spriteBatch;
    private Camera? _camera;
    private Texture2D? _weaponSwingTexture;
    private Texture2D? _fistSwingTexture;

    private sealed class SwingState
    {
        public required Entity Actor { get; init; }
        public required Vector2 Direction { get; init; }
        public required AttackProfile Attack { get; init; }
        public float Age { get; set; }
        public float Duration { get; init; } = 0.14f;
    }

    public override void Update(float deltaTime)
    {
        var readyKeys = _readyTimers.Keys.ToArray();
        foreach (var key in readyKeys)
        {
            _readyTimers[key] += deltaTime;
            if (_readyTimers[key] > 0.5f)
                _readyTimers.Remove(key);
        }

        var cooldownKeys = _cooldowns.Keys.ToArray();
        foreach (var key in cooldownKeys)
        {
            _cooldowns[key] = Math.Max(0f, _cooldowns[key] - deltaTime);
            if (_cooldowns[key] <= 0f)
            {
                _cooldowns.Remove(key);
                _readyTimers[key] = 0f;
            }
        }

        for (var i = _swings.Count - 1; i >= 0; i--)
        {
            var swing = _swings[i];
            swing.Age += deltaTime;
            if (swing.Age >= swing.Duration || !swing.Actor.Active)
                _swings.RemoveAt(i);
        }
    }

    public override void Draw()
    {
        if (_swings.Count == 0)
            return;

        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        if (_spriteBatch == null || _camera == null)
            return;

        if ((_weaponSwingTexture == null || _fistSwingTexture == null) && ServiceLocator.Has<AssetManager>())
        {
            var assets = ServiceLocator.Get<AssetManager>();
            _weaponSwingTexture ??= assets.LoadFromFile(Path.Combine("SandboxGame", "Content", "Textures", "Combat", "weapon_swing.png"));
            _fistSwingTexture ??= assets.LoadFromFile(Path.Combine("SandboxGame", "Content", "Textures", "Combat", "fist_swing.png"));
        }

        if (_weaponSwingTexture == null || _fistSwingTexture == null)
            return;

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix());

        foreach (var swing in _swings)
        {
            var tf = swing.Actor.GetComponent<TransformComponent>();
            if (tf == null)
                continue;

            var progress = Math.Clamp(swing.Age / swing.Duration, 0f, 1f);
            var alpha = 1f - progress;
            var dir = swing.Direction == Vector2.Zero ? new Vector2(1f, 0f) : Vector2.Normalize(swing.Direction);
            var isFist = swing.Attack.IsUnarmed;
            var angle = MathF.Atan2(dir.Y, dir.X) + MathHelper.PiOver2;
            var texture = isFist ? _fistSwingTexture : _weaponSwingTexture;
            var effectPos = tf.Position + dir * (isFist ? (5.6f + 1.6f * progress) : (4.7f + 2.1f * progress));
            var origin = new Vector2(texture.Width / 2f, texture.Height);

            _spriteBatch.Draw(
                texture,
                effectPos,
                null,
                Color.White * (0.8f * alpha),
                angle,
                origin,
                1f,
                SpriteEffects.None,
                0f);
        }

        _spriteBatch.End();
    }

    public bool CanAttack(Entity attacker, Entity target, WeaponComponent weapon)
        => CanAttack(attacker, target, new AttackProfile(
            weapon.DamageType,
            weapon.EffectiveMinDamage,
            weapon.EffectiveMaxDamage,
            weapon.Range,
            weapon.Windup,
            weapon.Accuracy,
            weapon.AttackVerb,
            weapon.Owner?.GetComponent<ItemComponent>()?.ItemName ?? weapon.Owner?.Name ?? "Оружие",
            GetWeaponSkillType(weapon.Owner)));

    public bool CanAttack(Entity attacker, Entity target, AttackProfile attack)
    {
        if (!attacker.Active || !target.Active || attacker == target)
            return false;

        var attackerHealth = attacker.GetComponent<HealthComponent>();
        var targetHealth = target.GetComponent<HealthComponent>();
        if (attackerHealth?.IsDead == true || targetHealth?.IsDead == true)
            return false;

        var attackerTransform = attacker.GetComponent<TransformComponent>();
        var targetTransform = target.GetComponent<TransformComponent>();
        if (attackerTransform == null || targetTransform == null)
            return false;

        return Vector2.Distance(attackerTransform.Position, targetTransform.Position) <= attack.Range;
    }

    public bool TryAttack(Entity attacker, Entity target, Entity weaponEntity, WeaponComponent weapon)
    {
        var profile = new AttackProfile(
            weapon.DamageType,
            weapon.EffectiveMinDamage,
            weapon.EffectiveMaxDamage,
            weapon.Range,
            weapon.Windup,
            weapon.Accuracy,
            weapon.AttackVerb,
            weaponEntity.GetComponent<ItemComponent>()?.ItemName ?? weaponEntity.Name,
            GetWeaponSkillType(weaponEntity));

        if (!CanAttack(attacker, target, profile))
            return false;

        var item = weaponEntity.GetComponent<ItemComponent>();
        if (item?.ContainedIn != attacker)
            return false;

        return TryAttack(attacker, target, profile);
    }

    public bool TryAttack(Entity attacker, Entity target, AttackProfile attack)
    {
        if (!CanAttackNow(attacker))
            return false;

        if (!CanAttack(attacker, target, attack))
            return false;

        var spamPenalty = HasSpamPenalty(attacker);
        StartSwing(attacker, GetDirection(attacker, target), attack);
        StartCooldown(attacker, attack);

        var targetHealth = target.GetComponent<HealthComponent>();
        var targetWounds = target.GetComponent<WoundComponent>();
        if (targetHealth == null && targetWounds == null)
            return false;

        if (spamPenalty)
            PopupTextSystem.Show(attacker, "Слабый удар", Color.Khaki, verticalOffset: -24f, lifetime: 0.9f);

        var rolledDamage = RollAttackDamage(attack);
        var hitChance = GetEffectiveAccuracy(attacker, attack, spamPenalty);
        if (!RollHit(hitChance))
        {
            PopupTextSystem.Show(attacker, $"Промах ({attack.SourceName})", Color.SlateGray, lifetime: 1.1f);
            return true;
        }

        if (TryDodge(target))
        {
            PopupTextSystem.Show(target, "Уклонение", Color.LightBlue, lifetime: 1.2f);
            MarkWorldDirty();
            return true;
        }

        if (TryBlock(target, attack.DamageType))
        {
            PopupTextSystem.Show(target, "Блок", Color.LightSteelBlue, lifetime: 1.2f);
            MarkWorldDirty();
            return true;
        }

        var damage = CalculateFinalDamage(target, attack.DamageType, GetEffectiveBaseDamage(attacker, attack, rolledDamage, spamPenalty));
        if (damage <= 0.05f)
        {
            PopupTextSystem.Show(target, "Без вреда", Color.Silver, lifetime: 1.1f);
            return true;
        }

        if (targetWounds != null)
            WoundComponent.ApplyDamage(target, attack.DamageType, damage);
        else if (targetHealth != null)
        {
            targetHealth.Health = Math.Max(0f, targetHealth.Health - damage);
            if (attack.DamageType != DamageType.Exhaustion)
                DamageFlashComponent.Trigger(target);
        }

        ImproveSkillsOnHit(attacker, target, damage);
        ImproveAttackSkill(attacker, attack.AttackSkill, spamPenalty ? (0.11f / 1.2f) : 0.11f);
        DamageArmor(target, attack.DamageType, damage);
        MarkWorldDirty();

        if (ServiceLocator.Has<EventBus>())
        {
            ServiceLocator.Get<EventBus>().Publish(new EntityDamagedEvent(
                attacker,
                target,
                damage,
                attack.DamageType,
                targetHealth?.IsDead ?? false)
            {
                IsWeaponAttack = !attack.IsUnarmed
            });
        }

        if (target.GetComponent<TrainingDummyComponent>() is { } dummy)
        {
            dummy.LastDamage = damage;
            if (targetHealth != null)
                targetHealth.Health = targetHealth.MaxHealth;

            PopupTextSystem.Show(target, $"Урон: {damage:F0}", Color.Orange, lifetime: 1.5f);
            PopupTextSystem.Show(target, GetDamageLabel(attack.DamageType), Color.LightGoldenrodYellow, verticalOffset: -30f, lifetime: 1.3f);
            return true;
        }

        var popupColor = attack.DamageType switch
        {
            DamageType.Slash => Color.IndianRed,
            DamageType.Blunt => Color.Orange,
            DamageType.Burn => Color.OrangeRed,
            _ => Color.White
        };
        PopupTextSystem.Show(target, $"-{damage:F0} {GetDamageLabel(attack.DamageType)}", popupColor, lifetime: 1.25f);
        return true;
    }

    public AttackProfile GetCurrentAttackProfile(Entity attacker)
    {
        var hands = attacker.GetComponent<HandsComponent>();
        var activeWeapon = hands?.ActiveItem?.GetComponent<WeaponComponent>();
        if (activeWeapon?.Owner != null)
        {
            return new AttackProfile(
                activeWeapon.DamageType,
                activeWeapon.EffectiveMinDamage,
                activeWeapon.EffectiveMaxDamage,
                activeWeapon.Range,
                activeWeapon.Windup,
                activeWeapon.Accuracy,
                activeWeapon.AttackVerb,
                activeWeapon.Owner.GetComponent<ItemComponent>()?.ItemName ?? activeWeapon.Owner.Name,
                GetWeaponSkillType(activeWeapon.Owner));
        }

        return UnarmedAttack;
    }

    public bool TryAttackOrSwing(Entity attacker, Entity? target, Vector2 aimWorldPosition)
    {
        var attack = GetCurrentAttackProfile(attacker);
        if (!CanAttackNow(attacker))
            return true;

        if (target != null && CanAttack(attacker, target, attack))
            return TryAttack(attacker, target, attack);

        var direction = GetDirection(attacker, aimWorldPosition);
        StartSwing(attacker, direction, attack);
        StartCooldown(attacker, attack);
        return true;
    }

    public Vector2 GetVisualOffset(Entity entity)
    {
        var swing = _swings.LastOrDefault(state => state.Actor == entity);
        if (swing == null)
            return Vector2.Zero;

        var progress = Math.Clamp(swing.Age / swing.Duration, 0f, 1f);
        var push = MathF.Sin(progress * MathF.PI);
        return swing.Direction * (4f * push);
    }

    private bool CanAttackNow(Entity attacker)
        => !_cooldowns.TryGetValue(attacker.Id, out var cooldown) || cooldown <= 0f;

    private void StartCooldown(Entity attacker, AttackProfile attack)
    {
        _cooldowns[attacker.Id] = Math.Max(0.18f, attack.Windup);
        _readyTimers.Remove(attacker.Id);
    }

    private void StartSwing(Entity attacker, Vector2 direction, AttackProfile attack)
    {
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(1f, 0f);
        else
            direction.Normalize();

        _swings.RemoveAll(state => state.Actor == attacker);
        _swings.Add(new SwingState
        {
            Actor = attacker,
            Direction = direction,
            Attack = attack
        });
    }

    private static Vector2 GetDirection(Entity attacker, Entity target)
    {
        var attackerTf = attacker.GetComponent<TransformComponent>();
        var targetTf = target.GetComponent<TransformComponent>();
        if (attackerTf == null || targetTf == null)
            return new Vector2(1f, 0f);

        return GetDirection(attackerTf.Position, targetTf.Position);
    }

    private static Vector2 GetDirection(Entity attacker, Vector2 aimWorldPosition)
    {
        var attackerTf = attacker.GetComponent<TransformComponent>();
        if (attackerTf == null)
            return new Vector2(1f, 0f);

        return GetDirection(attackerTf.Position, aimWorldPosition);
    }

    private static Vector2 GetDirection(Vector2 from, Vector2 to)
    {
        var direction = to - from;
        if (direction.LengthSquared() < 0.0001f)
            return new Vector2(1f, 0f);
        return Vector2.Normalize(direction);
    }

    private bool RollHit(float accuracy)
        => _random.NextDouble() <= Math.Clamp(accuracy, 0.05f, 1f);

    private float RollAttackDamage(AttackProfile attack)
    {
        var min = MathF.Min(attack.MinDamage, attack.MaxDamage);
        var max = MathF.Max(attack.MinDamage, attack.MaxDamage);
        if (Math.Abs(max - min) <= 0.001f)
            return min;

        return MathHelper.Lerp(min, max, (float)_random.NextDouble());
    }

    private float GetEffectiveAccuracy(Entity attacker, AttackProfile attack, bool spamPenalty)
    {
        var skill = attacker.GetComponent<SkillComponent>()?.GetSkill(attack.AttackSkill) ?? 0f;
        var bonus = SkillComponent.GetAccuracyOffset(attack.AttackSkill, skill);
        var minChance = attack.AttackSkill == SkillType.HandToHand ? 0.32f : 0.28f;
        var maxChance = attack.AttackSkill == SkillType.HandToHand ? 0.99f : 0.98f;
        var hitChance = MathHelper.Clamp(attack.Accuracy + bonus, minChance, maxChance);
        if (!spamPenalty)
            return hitChance;

        var missChance = (1f - hitChance) * SpamPenaltyMissMultiplier;
        return MathHelper.Clamp(1f - missChance, minChance, maxChance);
    }

    private float GetEffectiveBaseDamage(Entity attacker, AttackProfile attack, float rolledDamage, bool spamPenalty)
    {
        var skill = attacker.GetComponent<SkillComponent>()?.GetSkill(attack.AttackSkill) ?? 0f;
        var multiplier = SkillComponent.GetDamageMultiplier(attack.AttackSkill, skill);
        var damage = rolledDamage * multiplier;
        return spamPenalty ? damage / SpamPenaltyDamageDivisor : damage;
    }

    private bool HasSpamPenalty(Entity attacker)
        => _readyTimers.TryGetValue(attacker.Id, out var readyTime) && readyTime < SpamPenaltyWindow;

    private bool TryDodge(Entity target)
    {
        var skills = target.GetComponent<SkillComponent>();
        var dodgeChance = skills == null
            ? 0.02f
            : MathHelper.Clamp(0.02f + skills.Dodge * 0.0045f, 0.02f, 0.45f);

        var success = _random.NextDouble() < dodgeChance;
        if (success)
            skills?.Improve(SkillType.Dodge, 0.14f);

        return success;
    }

    private bool TryBlock(Entity target, DamageType incomingType)
    {
        if (incomingType == DamageType.Burn)
            return false;

        var hands = target.GetComponent<HandsComponent>();
        var activeWeapon = hands?.ActiveItem?.GetComponent<WeaponComponent>();
        if (activeWeapon == null)
            return false;

        var skills = target.GetComponent<SkillComponent>();
        var skillChance = skills == null ? 0.03f : skills.Blocking * 0.004f;
        var totalChance = MathHelper.Clamp(0.05f + activeWeapon.BlockBonus + skillChance, 0f, 0.65f);
        var success = _random.NextDouble() < totalChance;
        if (success)
            skills?.Improve(SkillType.Blocking, 0.16f);

        return success;
    }

    private float CalculateFinalDamage(Entity target, DamageType type, float baseDamage)
    {
        var skills = target.GetComponent<SkillComponent>();
        var fortitudeMultiplier = skills != null
            ? SkillComponent.GetFortitudeDamageTakenMultiplier(skills.Fortitude)
            : 1.12f;

        var equipment = target.GetComponent<EquipmentComponent>();
        var armorMultiplier = equipment != null
            ? 1f - MathHelper.Clamp(equipment.GetArmorResistance(type), 0f, 0.65f)
            : 1f;

        var finalDamage = baseDamage * fortitudeMultiplier * armorMultiplier;
        return Math.Max(0f, finalDamage);
    }

    private static void ImproveSkillsOnHit(Entity attacker, Entity target, float damage)
    {
        target.GetComponent<SkillComponent>()?.Improve(SkillType.Fortitude, Math.Max(0.04f, damage * 0.015f));
    }

    private static void ImproveAttackSkill(Entity attacker, SkillType skill, float amount)
    {
        if (skill != SkillType.HandToHand
            && skill != SkillType.OneHandedWeapons
            && skill != SkillType.TwoHandedWeapons
            && skill != SkillType.RangedWeapons)
        {
            return;
        }

        attacker.GetComponent<SkillComponent>()?.Improve(skill, amount);
    }

    private static SkillType GetWeaponSkillType(Entity? weaponEntity)
    {
        if (weaponEntity?.GetComponent<ItemComponent>()?.TwoHanded == true)
            return SkillType.TwoHandedWeapons;

        return SkillType.OneHandedWeapons;
    }

    private static void DamageArmor(Entity target, DamageType type, float damage)
    {
        var equipment = target.GetComponent<EquipmentComponent>();
        if (equipment == null)
            return;

        var wearAmount = Math.Max(0.1f, damage * 0.18f);
        foreach (var slot in equipment.Slots)
        {
            var wearable = slot.Item?.GetComponent<WearableComponent>();
            if (wearable == null)
                continue;

            if (wearable.GetArmorResistance(type) <= 0f)
                continue;

            wearable.Durability = Math.Max(0f, wearable.Durability - wearAmount);
        }
    }

    private static string GetDamageLabel(DamageType type) => type switch
    {
        DamageType.Slash => "порезы",
        DamageType.Blunt => "ушибы",
        DamageType.Burn => "ожоги",
        DamageType.Exhaustion => "истощение",
        _ => "урон"
    };

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
