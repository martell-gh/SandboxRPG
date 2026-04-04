#nullable enable
using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    private XmlWindow _window = null!;
    private UIProgressBar _healthBar = null!;
    private UIProgressBar _hungerBar = null!;
    private UIProgressBar _thirstBar = null!;
    private UIProgressBar _bladderBar = null!;
    private UIProgressBar _bowelBar = null!;
    private UILabel _statusLabel = null!;
    private UILabel _digestLabel = null!;
    private UILabel _speedLabel = null!;
    private InputManager _input = null!;
    private IKeyBindingSource? _keys;
    private SpriteBatch _sb = null!;
    private GraphicsDevice _gd = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _keys = ServiceLocator.Has<IKeyBindingSource>() ? ServiceLocator.Get<IKeyBindingSource>() : null;
        _sb = ServiceLocator.Get<SpriteBatch>();
        _gd = ServiceLocator.Get<GraphicsDevice>();
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        if (!ServiceLocator.Has<UIManager>()) return;

        var ui = ServiceLocator.Get<UIManager>();
        var path = Path.Combine("SandboxGame", "Content", "UI", "MetabolismWindow.xml");

        if (!File.Exists(path)) return;

        _window = ui.LoadWindow(path);
        _healthBar = _window.Get<UIProgressBar>("healthBar")!;
        _hungerBar = _window.Get<UIProgressBar>("hungerBar")!;
        _thirstBar = _window.Get<UIProgressBar>("thirstBar")!;
        _bladderBar = _window.Get<UIProgressBar>("bladderBar")!;
        _bowelBar = _window.Get<UIProgressBar>("bowelBar")!;
        _statusLabel = _window.Get<UILabel>("statusLabel")!;
        _digestLabel = _window.Get<UILabel>("digestLabel")!;
        _speedLabel = _window.Get<UILabel>("speedLabel")!;

        // Update every frame while open
        _window.OnUpdate += UpdateBars;
    }

    private void EnsureHudResources()
    {
        if (_pixel != null)
            return;

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData(new[] { Color.White });

        if (ServiceLocator.Has<UIManager>())
        {
            var ui = ServiceLocator.Get<UIManager>();
            var fontField = typeof(UIManager).GetField("_font", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _font = (SpriteFont?)fontField?.GetValue(ui) ?? _font;
        }
    }

    public override void Update(float deltaTime)
    {
        EnsureWindow();
        if (_window == null || _input == null) return;

        // Toggle with M key
        if (!DevConsole.DevMode && _window.IsOpen)
            _window.Close();

        if (DevConsole.DevMode && _input.IsPressed(GetKey("Metabolism", Microsoft.Xna.Framework.Input.Keys.M)) && !DevConsole.IsOpen)
        {
            if (_window.IsOpen)
                _window.Close();
            else
                _window.Open(new Point(20, 60));
        }
    }

    public override void Draw()
    {
        EnsureHudResources();
        if (_pixel == null || _font == null)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, MetabolismComponent>().FirstOrDefault();
        if (player == null)
            return;

        var m = player.GetComponent<MetabolismComponent>()!;
        var health = player.GetComponent<HealthComponent>();

        _sb.Begin(samplerState: SamplerState.PointClamp);

        var panelX = 16;
        var panelY = 16;
        var panelWidth = 248;
        var lineHeight = 20;
        var barX = panelX + 72;
        var barWidth = 160;
        var currentY = panelY;

        var rows = new System.Collections.Generic.List<(string Label, float Value, float Max, Color Fill, string Text)>();

        if (health != null)
        {
            var healthColor = health.IsDead
                ? new Color(90, 90, 90)
                : health.Health <= health.MaxHealth * 0.25f ? Color.Red
                : health.Health <= health.MaxHealth * 0.6f ? Color.Orange
                : new Color(90, 185, 90);
            rows.Add(("HP", health.Health, health.MaxHealth, healthColor, $"{health.Health:0}/{health.MaxHealth:0}"));
        }

        rows.Add(("Голод", m.Hunger, 100f, m.HungerStatus switch
        {
            NeedStatus.Critical => Color.Red,
            NeedStatus.Warning => new Color(204, 136, 51),
            _ => new Color(100, 180, 60)
        }, $"{m.Hunger:0}/100"));

        rows.Add(("Жажда", m.Thirst, 100f, m.ThirstStatus switch
        {
            NeedStatus.Critical => Color.Red,
            NeedStatus.Warning => new Color(51, 136, 204),
            _ => new Color(50, 140, 210)
        }, $"{m.Thirst:0}/100"));

        rows.Add(("Пузырь", m.Bladder, 100f, m.BladderStatus switch
        {
            NeedStatus.Critical => Color.Red,
            NeedStatus.Warning => new Color(204, 204, 51),
            _ => new Color(140, 180, 60)
        }, $"{m.Bladder:0}%"));

        rows.Add(("Кишеч.", m.Bowel, 100f, m.BowelStatus switch
        {
            NeedStatus.Critical => Color.Red,
            NeedStatus.Warning => new Color(160, 120, 50),
            _ => new Color(140, 160, 80)
        }, $"{m.Bowel:0}%"));

        var panelHeight = rows.Count * lineHeight + 12;
        _sb.Draw(_pixel, new Rectangle(panelX - 8, panelY - 8, panelWidth, panelHeight), Color.Black * 0.58f);

        foreach (var row in rows)
        {
            DrawHudBar(panelX, currentY, barX, barWidth, row.Label, row.Value, row.Max, row.Fill, row.Text);
            currentY += lineHeight;
        }

        _sb.End();
    }

    private void DrawHudBar(int panelX, int y, int barX, int barWidth, string label, float value, float max, Color fillColor, string text)
    {
        _sb.DrawString(_font, label, new Vector2(panelX, y + 2), Color.White);

        var barRect = new Rectangle(barX, y + 2, barWidth, 14);
        _sb.Draw(_pixel, barRect, new Color(35, 35, 35, 220));
        _sb.Draw(_pixel, new Rectangle(barRect.X, barRect.Y, barRect.Width, 1), Color.White * 0.15f);
        _sb.Draw(_pixel, new Rectangle(barRect.X, barRect.Bottom - 1, barRect.Width, 1), Color.Black * 0.45f);

        var ratio = max <= 0f ? 0f : Math.Clamp(value / max, 0f, 1f);
        var fillWidth = Math.Max(0, (int)((barRect.Width - 2) * ratio));
        if (fillWidth > 0)
            _sb.Draw(_pixel, new Rectangle(barRect.X + 1, barRect.Y + 1, fillWidth, barRect.Height - 2), fillColor);

        var textSize = _font.MeasureString(text);
        _sb.DrawString(_font, text, new Vector2(barRect.Right - textSize.X - 4, y + 1), Color.White);
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

    private Microsoft.Xna.Framework.Input.Keys GetKey(string action, Microsoft.Xna.Framework.Input.Keys fallback)
        => _keys?.GetKey(action) ?? fallback;
}
