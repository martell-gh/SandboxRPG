using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Metabolism;
using MTEngine.UI;

namespace SandboxGame.Systems;

/// <summary>
/// Binds the MetabolismWindow.xml to the player's MetabolismComponent.
/// Opens/closes with the M key.
/// </summary>
public class MetabolismUI : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private XmlWindow _window;
    private UIProgressBar _healthBar;
    private UIProgressBar _hungerBar;
    private UIProgressBar _thirstBar;
    private UIProgressBar _bladderBar;
    private UIProgressBar _bowelBar;
    private UILabel _statusLabel;
    private UILabel _digestLabel;
    private UILabel _speedLabel;
    private InputManager _input;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        if (!ServiceLocator.Has<UIManager>()) return;

        var ui = ServiceLocator.Get<UIManager>();
        var path = Path.Combine("SandboxGame", "Content", "UI", "MetabolismWindow.xml");

        if (!File.Exists(path)) return;

        _window = ui.LoadWindow(path);
        _healthBar = _window.Get<UIProgressBar>("healthBar");
        _hungerBar = _window.Get<UIProgressBar>("hungerBar");
        _thirstBar = _window.Get<UIProgressBar>("thirstBar");
        _bladderBar = _window.Get<UIProgressBar>("bladderBar");
        _bowelBar = _window.Get<UIProgressBar>("bowelBar");
        _statusLabel = _window.Get<UILabel>("statusLabel");
        _digestLabel = _window.Get<UILabel>("digestLabel");
        _speedLabel = _window.Get<UILabel>("speedLabel");

        // Update every frame while open
        _window.OnUpdate += UpdateBars;
    }

    public override void Update(float deltaTime)
    {
        EnsureWindow();
        if (_window == null || _input == null) return;

        // Toggle with M key
        if (_input.IsPressed(Keys.M) && !DevConsole.IsOpen)
        {
            if (_window.IsOpen)
                _window.Close();
            else
                _window.Open(new Point(20, 60));
        }
    }

    private void UpdateBars(float dt)
    {
        var player = World.GetEntitiesWith<PlayerTagComponent, MetabolismComponent>().FirstOrDefault();
        if (player == null) return;

        var m = player.GetComponent<MetabolismComponent>()!;
        var health = player.GetComponent<HealthComponent>();

        if (_healthBar != null)
        {
            _healthBar.Visible = health != null;
            if (health != null)
            {
                _healthBar.Value = health.Health;
                _healthBar.MaxValue = health.MaxHealth;
                _healthBar.FillColor = health.IsDead
                    ? new Color(90, 90, 90)
                    : health.Health <= health.MaxHealth * 0.25f ? Color.Red
                    : health.Health <= health.MaxHealth * 0.6f ? Color.Orange
                    : new Color(90, 185, 90);
            }
        }

        // Hunger & Thirst (100 = full, 0 = empty)
        if (_hungerBar != null)
        {
            _hungerBar.Value = m.Hunger;
            _hungerBar.MaxValue = 100f;
            _hungerBar.FillColor = m.HungerStatus switch
            {
                NeedStatus.Critical => Color.Red,
                NeedStatus.Warning => new Color(204, 136, 51),
                _ => new Color(100, 180, 60)
            };
        }

        if (_thirstBar != null)
        {
            _thirstBar.Value = m.Thirst;
            _thirstBar.MaxValue = 100f;
            _thirstBar.FillColor = m.ThirstStatus switch
            {
                NeedStatus.Critical => Color.Red,
                NeedStatus.Warning => new Color(51, 136, 204),
                _ => new Color(50, 140, 210)
            };
        }

        // Bladder & Bowel (0 = empty, 100 = full — bar shows fill %)
        if (_bladderBar != null)
        {
            _bladderBar.Value = m.Bladder;
            _bladderBar.MaxValue = 100f;
            _bladderBar.FillColor = m.BladderStatus switch
            {
                NeedStatus.Critical => Color.Red,
                NeedStatus.Warning => new Color(204, 204, 51),
                _ => new Color(140, 180, 60)
            };
        }

        if (_bowelBar != null)
        {
            _bowelBar.Value = m.Bowel;
            _bowelBar.MaxValue = 100f;
            _bowelBar.FillColor = m.BowelStatus switch
            {
                NeedStatus.Critical => Color.Red,
                NeedStatus.Warning => new Color(160, 120, 50),
                _ => new Color(140, 160, 80)
            };
        }

        // Status text
        if (_statusLabel != null)
        {
            var worst = new[] { m.HungerStatus, m.ThirstStatus, m.BladderStatus, m.BowelStatus }.Max();
            _statusLabel.Text = worst switch
            {
                _ when health?.IsDead == true => "Состояние: Мёртв",
                NeedStatus.Critical => "Состояние: Критическое!",
                NeedStatus.Warning => "Состояние: Плохое",
                _ => "Состояние: Нормальное"
            };
            _statusLabel.Color = worst switch
            {
                _ when health?.IsDead == true => Color.Gray,
                NeedStatus.Critical => Color.Red,
                NeedStatus.Warning => Color.Yellow,
                _ => new Color(136, 204, 136)
            };
        }

        // Digestion + substances info
        if (_digestLabel != null)
        {
            var lines = new System.Collections.Generic.List<string>();

            if (m.DigestingItems.Count > 0)
            {
                var names = string.Join(", ", m.DigestingItems.Select(d => d.Name));
                lines.Add($"Переваривается: {names}");
            }

            if (m.SubstanceConcentrations.Count > 0)
            {
                lines.Add("Вещества:");
                foreach (var substance in m.SubstanceConcentrations.Values
                             .OrderByDescending(s => s.Amount)
                             .ThenBy(s => s.Name))
                {
                    lines.Add($"- {substance.Name}: {substance.Amount:0.##}");
                }
            }

            _digestLabel.Text = string.Join("\n", lines);
        }

        // Speed modifier
        if (_speedLabel != null)
        {
            if (m.SpeedModifier < 0.99f)
                _speedLabel.Text = $"Скорость: {m.SpeedModifier * 100:0}%";
            else if (m.SpeedModifier > 1.01f)
                _speedLabel.Text = $"Скорость: +{(m.SpeedModifier - 1f) * 100:0}%";
            else
                _speedLabel.Text = "";
        }
    }
}
