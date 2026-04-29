using System;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.UI;

namespace MTEngine.Metabolism;

public class SubstanceWorkbenchSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private UIManager _ui = null!;
    private InputManager _input = null!;
    private XmlWindow? _transferWindow;
    private UILabel? _sourceLabel;
    private UILabel? _targetLabel;
    private UILabel? _hintLabel;
    private UIScrollPanel? _rowsPanel;

    private Entity? _actor;
    private ISubstanceReservoir? _source;
    private LiquidContainerComponent? _target;
    private bool _sourceMustStayHeld;
    private bool _targetMustStayHeld;
    private float _openGraceTimer;

    public override void OnInitialize()
    {
        _ui = ServiceLocator.Get<UIManager>();
        _input = ServiceLocator.Get<InputManager>();
    }

    public void OpenTransferWindow(Entity actor, ISubstanceReservoir source, LiquidContainerComponent target)
    {
        EnsureWindow();

        _actor = actor;
        _source = source;
        _target = target;
        _sourceMustStayHeld = IsHeldByActor(source, actor);
        _targetMustStayHeld = IsHeldByActor(target, actor);
        _openGraceTimer = 0.2f;
        if (_rowsPanel != null)
            _rowsPanel.ScrollOffset = 0;
        RebuildTransferRows();
        _transferWindow!.Open(GetSafeOpenPosition());
    }

    private void EnsureWindow()
    {
        if (_transferWindow != null)
            return;

        var window = new XmlWindow
        {
            Id = "substanceTransfer",
            Title = "Работа с веществами",
            Width = 470,
            Height = 420,
            Position = new Point(760, 80)
        };

        window.Root.Direction = LayoutDirection.Vertical;
        window.Root.Padding = 8;
        window.Root.Gap = 6;

        _sourceLabel = window.AddElement(new UILabel
        {
            Name = "sourceLabel",
            Height = 18,
            Color = Color.LightSkyBlue
        });

        _targetLabel = window.AddElement(new UILabel
        {
            Name = "targetLabel",
            Height = 18,
            Color = Color.LightGreen
        });

        _hintLabel = window.AddElement(new UILabel
        {
            Name = "hintLabel",
            Height = 18,
            Color = Color.Gray,
            Text = "Нажимай +5/+10/Все, чтобы перемещать вещества."
        });

        _rowsPanel = window.AddElement(new UIScrollPanel
        {
            Name = "rowsPanel",
            Height = 280,
            BackColor = new Color(12, 16, 20, 180),
            Padding = 6,
            Gap = 6
        });

        var closeButton = window.AddElement(new UIButton
        {
            Text = "Закрыть",
            Height = 28
        });
        closeButton.OnClick += () => _transferWindow?.Close();
        window.OnClosed += ClearContext;

        _ui.RegisterWindow(window);
        _transferWindow = window;
    }

    public override void Update(float deltaTime)
    {
        if (_transferWindow?.IsOpen != true)
            return;

        if (_openGraceTimer > 0f)
        {
            _openGraceTimer = Math.Max(0f, _openGraceTimer - deltaTime);
            return;
        }

        if (!IsWorkbenchContextValid())
            _transferWindow.Close();
    }

    private void RebuildTransferRows()
    {
        if (_transferWindow == null || _rowsPanel == null || _sourceLabel == null || _targetLabel == null || _hintLabel == null)
            return;

        _rowsPanel.Clear();

        if (_source == null || _target == null)
            return;

        _sourceLabel.Text = $"Источник: {_source.DisplayName}";
        _targetLabel.Text = $"Тара: {_target.ContainerName}  [{_target.CurrentVolume:0.#}/{_target.Capacity:0.#}]";
        _hintLabel.Text = _source.HasSubstances
            ? "Выбери вещество и объём переноса."
            : "Источник пуст.";

        var substances = SubstanceResolver.MergeById(_source.GetSubstances())
            .Where(dose => dose.Amount > 0.001f)
            .ToList();

        foreach (var substance in substances)
        {
            var row = new UIPanel
            {
                Direction = LayoutDirection.Horizontal,
                Height = 28,
                Gap = 6,
                BackColor = new Color(28, 34, 42, 180),
                Padding = 4
            };

            row.Add(new UILabel
            {
                Width = 170,
                Height = 18,
                Text = $"{substance.Name} x{substance.Amount:0.#}",
                Color = AssetManager.ParseHexColor(substance.Color, Color.White)
            });

            row.Add(MakeTransferButton("+5", substance.Id, 5f));
            row.Add(MakeTransferButton("+10", substance.Id, 10f));
            row.Add(MakeTransferButton("Все", substance.Id, substance.Amount));

            _rowsPanel.Add(row);
        }

        if (substances.Count == 0)
        {
            _rowsPanel.Add(new UILabel
            {
                Text = "Нечего переносить.",
                Height = 18,
                Color = Color.Gray
            });
        }
    }

    private UIButton MakeTransferButton(string text, string substanceId, float amount)
    {
        var button = new UIButton
        {
            Width = text == "Все" ? 70 : 56,
            Height = 22,
            Text = text
        };

        button.OnClick += () =>
        {
            if (_source == null || _target == null || _actor == null)
                return;

            var moved = _source.TransferSubstanceTo(_target, substanceId, amount);
            if (moved > 0.001f)
                Systems.PopupTextSystem.Show(_actor, $"Перенесено: {moved:0.#}", Color.LightSteelBlue, lifetime: 1.25f);

            RebuildTransferRows();

            if (!_source.HasSubstances || _target.FreeCapacity <= 0.001f)
                _transferWindow?.Close();
        };

        return button;
    }

    private bool IsWorkbenchContextValid()
    {
        if (_actor == null || !_actor.Active || _source == null || _target == null)
            return false;

        return IsReservoirAccessible(_source) && IsReservoirAccessible(_target);
    }

    private bool IsReservoirAccessible(ISubstanceReservoir reservoir)
    {
        if (reservoir is Component component)
        {
            var owner = component.Owner;
            if (owner == null)
                return false;

            var mustStayHeld = ReferenceEquals(reservoir, _source)
                ? _sourceMustStayHeld
                : ReferenceEquals(reservoir, _target)
                    ? _targetMustStayHeld
                    : false;

            if (mustStayHeld)
                return IsHeldByActor(reservoir, _actor);

            if (!owner.Active)
                return false;

            return mustStayHeld
                ? IsHeldByActor(reservoir, _actor)
                : IsNearActor(owner, _actor);
        }

        return true;
    }

    private void ClearContext()
    {
        _actor = null;
        _source = null;
        _target = null;
        _sourceMustStayHeld = false;
        _targetMustStayHeld = false;
        _openGraceTimer = 0f;
    }

    private Point GetSafeOpenPosition()
    {
        var mouse = _input.MousePosition;
        var x = mouse.X + 28;
        var y = mouse.Y + 20;

        if (!ServiceLocator.Has<Microsoft.Xna.Framework.Graphics.GraphicsDevice>())
            return new Point(x, y);

        var viewport = ServiceLocator.Get<Microsoft.Xna.Framework.Graphics.GraphicsDevice>().Viewport;
        x = Math.Clamp(x, 8, Math.Max(8, viewport.Width - 470 - 8));
        y = Math.Clamp(y, 8, Math.Max(8, viewport.Height - 420 - 8));

        var predictedWindow = new Rectangle(x, y, 470, 420);
        var closeRect = new Rectangle(
            predictedWindow.Right - XmlWindow.DefaultCloseButtonSize - 6,
            predictedWindow.Y + 5,
            XmlWindow.DefaultCloseButtonSize,
            XmlWindow.DefaultCloseButtonSize);

        if (closeRect.Contains(mouse))
        {
            x = Math.Clamp(mouse.X - predictedWindow.Width - 24, 8, Math.Max(8, viewport.Width - predictedWindow.Width - 8));
            predictedWindow.X = x;
            closeRect = new Rectangle(
                predictedWindow.Right - XmlWindow.DefaultCloseButtonSize - 6,
                predictedWindow.Y + 5,
                XmlWindow.DefaultCloseButtonSize,
                XmlWindow.DefaultCloseButtonSize);

            if (closeRect.Contains(mouse))
                y = Math.Clamp(mouse.Y + 36, 8, Math.Max(8, viewport.Height - predictedWindow.Height - 8));
        }

        return new Point(x, y);
    }

    private static bool IsHeldByActor(ISubstanceReservoir reservoir, Entity? actor)
    {
        if (actor == null || reservoir is not Component component)
            return false;

        var item = component.Owner?.GetComponent<ItemComponent>();
        return item?.ContainedIn == actor;
    }

    private static bool IsNearActor(Entity owner, Entity? actor)
    {
        if (actor == null)
            return false;

        var item = owner.GetComponent<ItemComponent>();
        if (item?.ContainedIn == actor)
            return true;

        if (item?.ContainedIn != null)
            return false;

        var actorTf = actor.GetComponent<TransformComponent>();
        var targetTf = owner.GetComponent<TransformComponent>();
        if (actorTf == null || targetTf == null)
            return owner.Active;

        var maxRange = owner.GetComponent<InteractableComponent>()?.InteractRange ?? 64f;
        return owner.Active && Vector2.Distance(actorTf.Position, targetTf.Position) <= maxRange;
    }
}
