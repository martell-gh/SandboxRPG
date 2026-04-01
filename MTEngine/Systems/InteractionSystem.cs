using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Systems;

public class InteractionSystem : GameSystem
{

    public override DrawLayer DrawLayer => DrawLayer.Overlay;
    private InputManager? _input;
    private Camera? _camera;
    private SpriteBatch? _sb;
    private SpriteFont? _font;
    private Texture2D? _pixel;
    private GraphicsDevice? _gd;

    // Состояние меню
    private bool _menuOpen;
    private Vector2 _menuScreenPos;
    private Entity? _targetEntity;
    private List<InteractionAction> _menuActions = new();
    private int _hoveredIndex = -1;

    private const int MenuWidth = 200;
    private const int HeaderHeight = 26;
    private const int ItemHeight = 24;
    private const int MenuPadding = 4;

    public void SetFont(SpriteFont font) => _font = font;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _camera = ServiceLocator.Get<Camera>();
        _gd = ServiceLocator.Get<GraphicsDevice>();
    }

    private void EnsureResources()
    {
        _sb ??= ServiceLocator.Get<SpriteBatch>();
        if (_pixel == null && _gd != null)
        {
            _pixel = new Texture2D(_gd, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
    }

    public override void Update(float deltaTime)
    {
        if (_input == null || _camera == null) return;

        // Не работаем пока открыта dev-консоль
        if (DevConsole.IsOpen) { CloseMenu(); return; }

        var mousePos = new Vector2(_input.MousePosition.X, _input.MousePosition.Y);
        var worldPos = _camera.ScreenToWorld(mousePos);

        // Escape — закрыть
        if (Keyboard.GetState().IsKeyDown(Keys.Escape) && _menuOpen)
        {
            CloseMenu();
            return;
        }

        if (_menuOpen)
        {
            // ЛКМ — клик по пункту или закрытие
            if (_input.LeftClicked)
            {
                var menuRect = GetMenuRect();
                if (!menuRect.Contains((int)mousePos.X, (int)mousePos.Y))
                    CloseMenu();
                else
                    HandleMenuClick(mousePos);
                return;
            }
            UpdateHover(mousePos);
            return;
        }

        // ПКМ — ищем интерактивный объект
        if (_input.RightClicked)
            TryOpenMenu(worldPos, mousePos);
    }

    private void TryOpenMenu(Vector2 worldPos, Vector2 screenPos)
    {
        Entity? player = null;
        foreach (var e in World.GetEntitiesWith<PlayerTagComponent>())
        { player = e; break; }

        if (player == null) return;

        var playerTf = player.GetComponent<TransformComponent>();
        if (playerTf == null) return;

        Entity? best = null;
        float bestMouseDist = float.MaxValue;

        foreach (var entity in World.GetEntitiesWith<TransformComponent, InteractableComponent>())
        {
            var tf = entity.GetComponent<TransformComponent>()!;
            var ia = entity.GetComponent<InteractableComponent>()!;

            float pDist = Vector2.Distance(playerTf.Position, tf.Position);
            if (pDist > ia.InteractRange) continue;

            float mDist = Vector2.Distance(worldPos, tf.Position);
            if (mDist < bestMouseDist)
            {
                bestMouseDist = mDist;
                best = entity;
            }
        }

        if (best != null)
        {
            var ia = best.GetComponent<InteractableComponent>()!;
            _targetEntity = best;
            _menuActions = ia.Actions.ToList();
            _menuScreenPos = screenPos;
            _menuOpen = true;
            _hoveredIndex = -1;
            Console.WriteLine($"[Interaction] Opened: {ia.DisplayName} ({_menuActions.Count} actions)");
        }
    }

    private void HandleMenuClick(Vector2 mousePos)
    {
        for (int i = 0; i < _menuActions.Count; i++)
        {
            if (GetItemRect(i).Contains((int)mousePos.X, (int)mousePos.Y))
            {
                ExecuteAction(i);
                return;
            }
        }
    }

    private void ExecuteAction(int index)
    {
        if (_targetEntity == null || index < 0 || index >= _menuActions.Count) return;

        Entity? actor = null;
        foreach (var e in World.GetEntitiesWith<PlayerTagComponent>())
        { actor = e; break; }

        var action = _menuActions[index];
        Console.WriteLine($"[Interaction] Execute: {action.Label}");
        action.Execute?.Invoke(actor!, _targetEntity);

        CloseMenu();
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _targetEntity = null;
        _menuActions.Clear();
        _hoveredIndex = -1;
    }

    private void UpdateHover(Vector2 mousePos)
    {
        _hoveredIndex = -1;
        for (int i = 0; i < _menuActions.Count; i++)
            if (GetItemRect(i).Contains((int)mousePos.X, (int)mousePos.Y))
            { _hoveredIndex = i; break; }
    }

    public override void Draw()
    {
        if (!_menuOpen || _font == null) return;
        EnsureResources();
        if (_sb == null || _pixel == null) return;

        var targetName = _targetEntity?.GetComponent<InteractableComponent>()?.DisplayName ?? "Object";
        var menuRect = GetMenuRect();

        _sb.Begin();

        // Тень
        _sb.Draw(_pixel, new Rectangle(menuRect.X + 3, menuRect.Y + 3, menuRect.Width, menuRect.Height),
            Color.Black * 0.4f);

        // Фон
        _sb.Draw(_pixel, menuRect, new Color(18, 22, 28));

        // Хедер
        var hdr = new Rectangle(menuRect.X, menuRect.Y, menuRect.Width, HeaderHeight);
        _sb.Draw(_pixel, hdr, new Color(35, 55, 35));

        // Граница хедера снизу
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Y + HeaderHeight, menuRect.Width, 1),
            new Color(70, 110, 70));

        // Название объекта
        _sb.DrawString(_font, targetName,
            new Vector2(menuRect.X + 8, menuRect.Y + (HeaderHeight - 14) / 2),
            Color.LimeGreen);

        // Пункты
        for (int i = 0; i < _menuActions.Count; i++)
        {
            var itemRect = GetItemRect(i);
            bool hovered = i == _hoveredIndex;

            if (hovered)
                _sb.Draw(_pixel, itemRect, new Color(55, 85, 55));

            _sb.DrawString(_font, _menuActions[i].Label,
                new Vector2(itemRect.X + 10, itemRect.Y + (ItemHeight - 14) / 2),
                hovered ? Color.White : new Color(200, 200, 200));

            // Разделитель
            if (i < _menuActions.Count - 1)
                _sb.Draw(_pixel, new Rectangle(itemRect.X + 6, itemRect.Bottom, itemRect.Width - 12, 1),
                    Color.White * 0.08f);
        }

        // Внешняя рамка
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Y, menuRect.Width, 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Bottom, menuRect.Width, 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Y, 1, menuRect.Height + 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(menuRect.Right, menuRect.Y, 1, menuRect.Height + 1), new Color(70, 110, 70));

        _sb.End();
    }

    private Rectangle GetMenuRect()
    {
        int totalH = HeaderHeight + _menuActions.Count * ItemHeight + MenuPadding;
        int x = (int)_menuScreenPos.X;
        int y = (int)_menuScreenPos.Y;

        if (_gd != null)
        {
            if (x + MenuWidth > _gd.Viewport.Width) x = _gd.Viewport.Width - MenuWidth - 4;
            if (y + totalH > _gd.Viewport.Height) y = _gd.Viewport.Height - totalH - 4;
            x = Math.Max(0, x);
            y = Math.Max(0, y);
        }

        return new Rectangle(x, y, MenuWidth, totalH);
    }

    private Rectangle GetItemRect(int i)
    {
        var mr = GetMenuRect();
        return new Rectangle(mr.X + 1, mr.Y + HeaderHeight + i * ItemHeight, mr.Width - 2, ItemHeight);
    }
}