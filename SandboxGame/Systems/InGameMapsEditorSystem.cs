#nullable enable
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.UI;
using MTEngine.World;

namespace SandboxGame.Systems;

public class InGameMapsEditorSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private InputManager _input = null!;
    private UIManager _ui = null!;
    private MapManager _mapManager = null!;
    private IKeyBindingSource? _keys;
    private XmlWindow? _window;
    private UIScrollPanel? _mapList;
    private UILabel? _summaryLabel;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _ui = ServiceLocator.Get<UIManager>();
        _mapManager = ServiceLocator.Get<MapManager>();
        _keys = ServiceLocator.Has<IKeyBindingSource>() ? ServiceLocator.Get<IKeyBindingSource>() : null;
    }

    public override void Update(float deltaTime)
    {
        if (!DevConsole.DevMode)
        {
            _window?.Close();
            return;
        }

        if (_input.IsPressed(GetKey("InGameMaps", Keys.F6)) && !DevConsole.IsOpen)
        {
            EnsureWindow();
            if (_window == null)
                return;

            if (_window.IsOpen)
                _window.Close();
            else
            {
                RefreshMapList();
                _window.Open(new Point(80, 80));
            }
        }
    }

    private void EnsureWindow()
    {
        if (_window != null)
            return;

        _window = new XmlWindow
        {
            Id = "ingame_maps_editor",
            Title = "In-Game Maps",
            Width = 460,
            Height = 460,
            Closable = true,
            Draggable = true
        };

        var root = _window.Root;
        root.Direction = LayoutDirection.Vertical;
        root.Padding = 10;
        root.Gap = 8;

        _summaryLabel = _window.AddElement(new UILabel
        {
            Name = "summary",
            Height = 20,
            Color = new Color(190, 220, 190)
        });

        _mapList = _window.AddElement(new UIScrollPanel
        {
            Name = "mapList",
            Height = 340,
            Padding = 6,
            Gap = 6,
            BackColor = new Color(14, 18, 22, 180)
        });

        var refreshButton = _window.AddElement(new UIButton
        {
            Name = "refreshMaps",
            Height = 30,
            Text = "Refresh List"
        });
        refreshButton.OnClick += RefreshMapList;

        _ui.RegisterWindow(_window);
    }

    private void RefreshMapList()
    {
        if (_window == null || _mapList == null || _summaryLabel == null)
            return;

        _mapList.Clear();
        var catalog = _mapManager.GetMapCatalog();
        var inGameCount = catalog.Count(entry => entry.InGame);
        _summaryLabel.Text = $"Maps: {catalog.Count}  InGame: {inGameCount}";

        foreach (var entry in catalog)
        {
            var row = new UIPanel
            {
                Direction = LayoutDirection.Horizontal,
                Gap = 8,
                Height = 30
            };

            row.Add(new UILabel
            {
                Width = 270,
                Height = 20,
                Text = $"{entry.Name} ({entry.Id})",
                Color = Color.White
            });

            var toggleButton = new UIButton
            {
                Width = 150,
                Height = 28,
                Text = entry.InGame ? "InGame: ON" : "InGame: OFF",
                BackColor = entry.InGame ? new Color(44, 92, 54) : new Color(68, 52, 52),
                HoverColor = entry.InGame ? new Color(58, 118, 70) : new Color(92, 66, 66)
            };
            toggleButton.OnClick += () =>
            {
                var nextValue = !entry.InGame;
                if (_mapManager.SetMapInGameFlag(entry.Id, nextValue))
                {
                    DevConsole.Log($"Map '{entry.Id}' inGame = {nextValue}");
                    RefreshMapList();
                }
                else
                {
                    DevConsole.Log($"Failed to update map '{entry.Id}'.");
                }
            };
            row.Add(toggleButton);

            _mapList.Add(row);
        }

        _window.PerformLayout();
    }

    private Keys GetKey(string action, Keys fallback)
        => _keys?.GetKey(action) ?? fallback;
}
