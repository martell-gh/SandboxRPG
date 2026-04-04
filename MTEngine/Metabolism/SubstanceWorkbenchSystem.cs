using System;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.UI;

namespace MTEngine.Metabolism;

public class SubstanceWorkbenchSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private UIManager _ui = null!;
    private XmlWindow? _transferWindow;
    private UILabel? _sourceLabel;
    private UILabel? _targetLabel;
    private UILabel? _hintLabel;
    private UIScrollPanel? _rowsPanel;

    private Entity? _actor;
    private ISubstanceReservoir? _source;
    private LiquidContainerComponent? _target;

    public override void OnInitialize()
    {
        _ui = ServiceLocator.Get<UIManager>();
    }

    public void OpenTransferWindow(Entity actor, ISubstanceReservoir source, LiquidContainerComponent target)
    {
        EnsureWindow();

        _actor = actor;
        _source = source;
        _target = target;
        RebuildTransferRows();
        _transferWindow!.Open(new Point(760, 80));
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

        _ui.RegisterWindow(window);
        _transferWindow = window;
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

        foreach (var substance in _source.GetSubstances().Where(dose => dose.Amount > 0.001f))
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

        if (!_source.GetSubstances().Any(dose => dose.Amount > 0.001f))
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
}
