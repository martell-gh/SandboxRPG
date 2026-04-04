using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public enum EditorCommand
{
    None,
    NewMap,
    LoadMap,
    SaveMap,
    Undo,
    Redo,
    ToggleFullscreen,
    ToolTiles,
    ToolPrototypes,
    ToolSpawns,
    PointerBrush,
    PointerMouse,
    ShapePoint,
    ShapeLine,
    ShapeFilledRectangle,
    ShapeHollowRectangle
}

public class EditorHUD
{
    private sealed class MenuButton
    {
        public required string Label { get; init; }
        public required EditorCommand Command { get; init; }
        public bool IsPrimary { get; init; }
        public bool IsToolButton { get; init; }
        public Rectangle Bounds { get; set; }
    }

    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;
    private readonly Texture2D _pixel;
    private readonly List<MenuButton> _menuButtons = new();
    private readonly List<MenuButton> _toolButtons = new();

    private string _message = "";
    private float _messageTimer = 0f;
    private Rectangle _topBarBounds;
    private Rectangle _bottomBarBounds;
    private EditorCommand _hoveredCommand = EditorCommand.None;

    private const int TopBarHeight = 156;
    private const int BottomBarHeight = 34;
    private const int MenuGap = 10;
    private const int MenuButtonHeight = 34;
    private const int MenuButtonPadding = 14;
    private const int OuterMargin = 18;
    private const int BrandBlockWidth = 192;
    private const int ToolbarInnerGap = 14;

    public Rectangle Bounds { get; private set; }
    public Rectangle TopBarBounds => _topBarBounds;
    public Rectangle BottomBarBounds => _bottomBarBounds;

    public EditorHUD(SpriteFont font, GraphicsDevice graphics)
    {
        _font = font;
        _graphics = graphics;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
        RebuildLayout();
    }

    public void ShowMessage(string msg)
    {
        _message = msg;
        _messageTimer = 3f;
        Console.WriteLine($"[Editor] {msg}");
    }

    public void ApplyToMap(MapData map) { }
    public void SetMapInfo(MapData map) { }

    public EditorCommand Update(MouseState mouse, MouseState prev)
    {
        RebuildLayout();

        if (_messageTimer > 0)
            _messageTimer -= 0.016f;

        _hoveredCommand = EditorCommand.None;
        var point = mouse.Position;
        foreach (var button in _menuButtons)
        {
            if (!button.Bounds.Contains(point))
                continue;

            _hoveredCommand = button.Command;
            break;
        }

        if (_hoveredCommand == EditorCommand.None)
        {
            foreach (var button in _toolButtons)
            {
                if (!button.Bounds.Contains(point))
                    continue;

                _hoveredCommand = button.Command;
                break;
            }
        }

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            foreach (var button in _menuButtons)
            {
                if (button.Bounds.Contains(point))
                    return button.Command;
            }

            foreach (var button in _toolButtons)
            {
                if (button.Bounds.Contains(point))
                    return button.Command;
            }
        }

        return EditorCommand.None;
    }

    public void Draw(
        SpriteBatch spriteBatch,
        EditorGame.Tool activeTool,
        PointerTool pointerTool,
        BrushShape brushShape,
        MapData map,
        EditorHistory history,
        int activeLayer,
        TilePalette palette)
    {
        RebuildLayout();
        var tool = activeTool switch
        {
            EditorGame.Tool.TilePainter => "[1] Tiles",
            EditorGame.Tool.EntityPainter => "[2] Objects",
            _ => "[3] Spawn"
        };
        var selected = activeTool == EditorGame.Tool.TilePainter
            ? palette.SelectedTileId ?? "-"
            : activeTool == EditorGame.Tool.EntityPainter
                ? palette.SelectedEntityId ?? "-"
                : "spawn";
        var viewport = _graphics.Viewport;

        DrawTopBar(spriteBatch, activeTool, pointerTool, brushShape, selected);

        spriteBatch.Draw(_pixel, _bottomBarBounds, new Color(10, 12, 14, 245));
        spriteBatch.Draw(_pixel, new Rectangle(_bottomBarBounds.X, _bottomBarBounds.Y, _bottomBarBounds.Width, 1), new Color(90, 120, 96));

        var info = $"Tool: {tool}   Selected: {selected}   Layer: {activeLayer}   Map: {map.Id}   Size: {map.Width}x{map.Height}   Spawns: {map.SpawnPoints.Count}   Objects: {map.Entities.Count}";
        spriteBatch.DrawString(_font, info, new Vector2(12, _bottomBarBounds.Y + 8), Color.LimeGreen);

        // сообщение по центру над панелью
        if (_messageTimer > 0)
        {
            var alpha = Math.Min(1f, _messageTimer);
            var size = _font.MeasureString(_message);
            spriteBatch.Draw(_pixel,
                new Rectangle((int)(viewport.Width / 2f - size.X / 2f - 8), _bottomBarBounds.Y - 28, (int)size.X + 16, 24),
                Color.Black * 0.85f * alpha);
            spriteBatch.DrawString(_font, _message,
                new Vector2(viewport.Width / 2f - size.X / 2f, _bottomBarBounds.Y - 26),
                Color.Yellow * alpha);
        }
    }

    private void RebuildLayout()
    {
        var viewport = _graphics.Viewport;
        _topBarBounds = new Rectangle(0, 0, viewport.Width, TopBarHeight);
        _bottomBarBounds = new Rectangle(0, viewport.Height - BottomBarHeight, viewport.Width, BottomBarHeight);
        Bounds = Rectangle.Union(_topBarBounds, _bottomBarBounds);

        _menuButtons.Clear();
        _toolButtons.Clear();
        var left = new (string Label, EditorCommand Command, bool Primary)[]
        {
            ("New", EditorCommand.NewMap, false),
            ("Load", EditorCommand.LoadMap, false),
            ("Save", EditorCommand.SaveMap, true),
            ("Undo", EditorCommand.Undo, false),
            ("Redo", EditorCommand.Redo, false),
            ("Fullscreen", EditorCommand.ToggleFullscreen, false),
        };

        var right = new (string Label, EditorCommand Command, bool Primary)[]
        {
            ("Tiles", EditorCommand.ToolTiles, false),
            ("Prototypes", EditorCommand.ToolPrototypes, false),
            ("Spawns", EditorCommand.ToolSpawns, false),
        };

        var leftStart = OuterMargin + BrandBlockWidth + ToolbarInnerGap;
        var rightReservedStart = _topBarBounds.Right - OuterMargin;
        var x = leftStart;
        foreach (var (label, command, primary) in left)
        {
            var width = (int)_font.MeasureString(label).X + MenuButtonPadding * 2;
            if (x + width > rightReservedStart)
                break;
            _menuButtons.Add(new MenuButton
            {
                Label = label,
                Command = command,
                IsPrimary = primary,
                IsToolButton = false,
                Bounds = new Rectangle(x, _topBarBounds.Y + 18, width, MenuButtonHeight)
            });
            x += width + MenuGap;
        }

        var rightWidth = 0;
        foreach (var (label, _, _) in right)
            rightWidth += (int)_font.MeasureString(label).X + MenuButtonPadding * 2;
        rightWidth += Math.Max(0, right.Length - 1) * MenuGap;

        x = Math.Max(leftStart, rightReservedStart - rightWidth);
        foreach (var (label, command, primary) in right)
        {
            var width = (int)_font.MeasureString(label).X + MenuButtonPadding * 2;
            _menuButtons.Add(new MenuButton
            {
                Label = label,
                Command = command,
                IsPrimary = primary,
                IsToolButton = true,
                Bounds = new Rectangle(x, _topBarBounds.Y + 18, width, MenuButtonHeight)
            });
            x += width + MenuGap;
        }

        var toolRowY = 110;
        var pointerButtons = new (string Label, EditorCommand Command)[]
        {
            ("Brush", EditorCommand.PointerBrush),
            ("Mouse", EditorCommand.PointerMouse)
        };
        var shapeButtons = new (string Label, EditorCommand Command)[]
        {
            ("Point", EditorCommand.ShapePoint),
            ("Line", EditorCommand.ShapeLine),
            ("Solid", EditorCommand.ShapeFilledRectangle),
            ("Hollow", EditorCommand.ShapeHollowRectangle)
        };

        var pointerWidth = pointerButtons.Sum(button => (int)_font.MeasureString(button.Label).X + MenuButtonPadding * 2) + MenuGap * (pointerButtons.Length - 1);
        var shapeWidth = shapeButtons.Sum(button => (int)_font.MeasureString(button.Label).X + MenuButtonPadding * 2) + MenuGap * (shapeButtons.Length - 1);
        var totalWidth = pointerWidth + 18 + shapeWidth;
        x = _topBarBounds.Center.X - totalWidth / 2;

        foreach (var (label, command) in pointerButtons)
        {
            var width = (int)_font.MeasureString(label).X + MenuButtonPadding * 2;
            _toolButtons.Add(new MenuButton
            {
                Label = label,
                Command = command,
                IsPrimary = false,
                IsToolButton = false,
                Bounds = new Rectangle(x, toolRowY, width, MenuButtonHeight)
            });
            x += width + MenuGap;
        }

        x += 18;
        foreach (var (label, command) in shapeButtons)
        {
            var width = (int)_font.MeasureString(label).X + MenuButtonPadding * 2;
            _toolButtons.Add(new MenuButton
            {
                Label = label,
                Command = command,
                IsPrimary = false,
                IsToolButton = false,
                Bounds = new Rectangle(x, toolRowY, width, MenuButtonHeight)
            });
            x += width + MenuGap;
        }
    }

    private void DrawTopBar(SpriteBatch spriteBatch, EditorGame.Tool activeTool, PointerTool pointerTool, BrushShape brushShape, string selected)
    {
        spriteBatch.Draw(_pixel, _topBarBounds, new Color(12, 16, 18, 240));
        spriteBatch.Draw(_pixel, new Rectangle(_topBarBounds.X, 72, _topBarBounds.Width, 1), new Color(62, 87, 74, 120));
        spriteBatch.Draw(_pixel, new Rectangle(_topBarBounds.X, 104, _topBarBounds.Width, 1), new Color(62, 87, 74, 80));
        spriteBatch.Draw(_pixel, new Rectangle(_topBarBounds.X, _topBarBounds.Bottom - 2, _topBarBounds.Width, 2), new Color(62, 87, 74, 140));
        spriteBatch.Draw(_pixel, new Rectangle(_topBarBounds.X, _topBarBounds.Bottom, _topBarBounds.Width, 10), new Color(0, 0, 0, 40));

        var brandRect = new Rectangle(OuterMargin, _topBarBounds.Y + 14, BrandBlockWidth, 42);
        DrawSoftBlock(spriteBatch, brandRect, new Color(21, 29, 30, 230));
        spriteBatch.Draw(_pixel, new Rectangle(brandRect.X + 12, brandRect.Y + 8, 4, brandRect.Height - 16), new Color(124, 189, 134));
        spriteBatch.DrawString(_font, "MTEditor", new Vector2(brandRect.X + 28, brandRect.Y + 4), new Color(233, 240, 235));
        spriteBatch.DrawString(_font, "Map workspace", new Vector2(brandRect.X + 28, brandRect.Y + 22), new Color(130, 145, 136));

        foreach (var button in _menuButtons)
        {
            var hovered = button.Command == _hoveredCommand;
            var activeToolButton = button.IsToolButton && CommandMatchesTool(button.Command, activeTool);
            Color fill;
            Color text;

            if (activeToolButton)
            {
                fill = hovered ? new Color(108, 157, 116) : new Color(91, 136, 99);
                text = Color.White;
            }
            else if (button.IsPrimary)
            {
                fill = hovered ? new Color(68, 106, 78) : new Color(50, 79, 58);
                text = hovered ? Color.White : new Color(220, 230, 223);
            }
            else
            {
                fill = hovered ? new Color(34, 43, 43) : new Color(22, 28, 29, 228);
                text = hovered ? new Color(235, 241, 236) : new Color(184, 194, 188);
            }

            DrawSoftBlock(spriteBatch, button.Bounds, fill);
            spriteBatch.DrawString(_font, button.Label, new Vector2(button.Bounds.X + MenuButtonPadding, button.Bounds.Y + 7), text);
        }

        var selectorText = $"Selected: {selected}";
        var selectorWidth = Math.Min(420, Math.Max(220, (int)_font.MeasureString(selectorText).X + 48));
        var selectorRect = new Rectangle(_topBarBounds.Center.X - selectorWidth / 2, 78, selectorWidth, 24);
        DrawSoftBlock(spriteBatch, selectorRect, new Color(17, 22, 23, 210));
        var selectorSize = _font.MeasureString(selectorText);
        spriteBatch.DrawString(_font, selectorText, new Vector2(selectorRect.Center.X - selectorSize.X / 2f, selectorRect.Y + 3), new Color(220, 229, 223));

        foreach (var button in _toolButtons)
        {
            var hovered = button.Command == _hoveredCommand;
            var active = CommandMatchesPointerTool(button.Command, pointerTool) || CommandMatchesBrushShape(button.Command, brushShape);
            var fill = active
                ? hovered ? new Color(101, 151, 110) : new Color(86, 128, 94)
                : hovered ? new Color(34, 43, 43) : new Color(18, 24, 25, 220);
            var text = active ? Color.White : hovered ? new Color(235, 241, 236) : new Color(184, 194, 188);
            DrawSoftBlock(spriteBatch, button.Bounds, fill);
            spriteBatch.DrawString(_font, button.Label, new Vector2(button.Bounds.X + MenuButtonPadding, button.Bounds.Y + 7), text);
        }
    }

    private static bool CommandMatchesTool(EditorCommand command, EditorGame.Tool activeTool)
    {
        return (command, activeTool) switch
        {
            (EditorCommand.ToolTiles, EditorGame.Tool.TilePainter) => true,
            (EditorCommand.ToolPrototypes, EditorGame.Tool.EntityPainter) => true,
            (EditorCommand.ToolSpawns, EditorGame.Tool.SpawnPoint) => true,
            _ => false
        };
    }

    private static bool CommandMatchesPointerTool(EditorCommand command, PointerTool pointerTool)
    {
        return (command, pointerTool) switch
        {
            (EditorCommand.PointerBrush, PointerTool.Brush) => true,
            (EditorCommand.PointerMouse, PointerTool.Mouse) => true,
            _ => false
        };
    }

    private static bool CommandMatchesBrushShape(EditorCommand command, BrushShape brushShape)
    {
        return (command, brushShape) switch
        {
            (EditorCommand.ShapePoint, BrushShape.Point) => true,
            (EditorCommand.ShapeLine, BrushShape.Line) => true,
            (EditorCommand.ShapeFilledRectangle, BrushShape.FilledRectangle) => true,
            (EditorCommand.ShapeHollowRectangle, BrushShape.HollowRectangle) => true,
            _ => false
        };
    }

    private void DrawSoftBlock(SpriteBatch spriteBatch, Rectangle rect, Color fill)
    {
        spriteBatch.Draw(_pixel, rect, fill);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), Color.White * 0.06f);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Color.Black * 0.2f);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), Color.White * 0.04f);
        spriteBatch.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), Color.Black * 0.18f);
    }
}
