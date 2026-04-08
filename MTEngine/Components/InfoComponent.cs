using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.UI;

namespace MTEngine.Components;

/// <summary>
/// Добавляет действие "Инфо" в контекстное меню.
/// При нажатии открывается окно с картинкой предмета, названием и описанием.
/// Название берётся из InteractableComponent.DisplayName или Entity.Name.
/// </summary>
[RegisterComponent("info")]
public class InfoComponent : Component, IInteractionSource
{
    [DataField("description")]
    [SaveField("description")]
    public string Description { get; set; } = "";

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        yield return new InteractionEntry
        {
            Id = "info.examine",
            Label = "Инфо",
            Priority = -10,
            InterruptsCurrentAction = false,
            Execute = c => OpenInfoWindow(c.Target)
        };
    }

    private static void OpenInfoWindow(Entity target)
    {
        var ui = ServiceLocator.Get<UIManager>();
        var windowId = $"info_{target.Id}";

        var existing = ui.GetWindow(windowId);
        if (existing != null)
        {
            if (existing.IsOpen) { existing.Close(); return; }
            ui.RemoveWindow(existing);
        }

        var info = target.GetComponent<InfoComponent>();
        var sprite = target.GetComponent<SpriteComponent>();
        var interactable = target.GetComponent<InteractableComponent>();

        var name = interactable?.DisplayName ?? target.Name ?? "???";
        var description = info?.Description ?? "";
        var font = GetUiFont(ui);
        const int windowWidth = 420;
        const int leftColumnWidth = 86;
        const int panelPadding = 8;
        const int gap = 10;
        const int descriptionWidth = windowWidth - (panelPadding * 2) - leftColumnWidth - gap - 16;
        var wrappedDescription = WrapText(description, font, descriptionWidth, 1f);
        var descriptionLines = CountLines(wrappedDescription);
        var descriptionHeight = Math.Max(72, descriptionLines * 18 + 8);
        var windowHeight = Math.Max(168, descriptionHeight + 28);

        // Построить окно: слева вверху картинка + название, справа описание
        var xml = $@"<Window Id=""{windowId}"" Title=""{EscapeXml(name)}"" Width=""{windowWidth}"" Height=""{windowHeight}"" Closable=""true"">
    <Panel Direction=""Horizontal"" Padding=""8"" Gap=""10"">
        <Panel Direction=""Vertical"" Gap=""4"" Width=""{leftColumnWidth}"">
            <Image Name=""icon"" Width=""64"" Height=""64"" />
            <Label Name=""itemName"" Text=""{EscapeXml(name)}"" Color=""#AADDAA"" Scale=""0.9"" />
        </Panel>
        <Label Name=""desc"" Text=""{EscapeXml(wrappedDescription)}"" Color=""#CCCCCC"" Width=""{descriptionWidth}"" Height=""{descriptionHeight}"" />
    </Panel>
</Window>";

        var window = ui.LoadWindowFromString(xml);

        // Установить текстуру иконки из SpriteComponent
        if (sprite?.Texture != null)
        {
            var icon = window.Get<UIImage>("icon");
            if (icon != null)
            {
                icon.Texture = sprite.Texture;
                icon.SourceRect = sprite.SourceRect;
            }
        }

        var position = GetCenteredWindowPosition(windowWidth, windowHeight);
        window.Open(position);
    }

    private static SpriteFont? GetUiFont(UIManager ui)
    {
        var fontField = typeof(UIManager).GetField("_font", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (SpriteFont?)fontField?.GetValue(ui);
    }

    private static string WrapText(string text, SpriteFont? font, int maxWidth, float scale)
    {
        if (string.IsNullOrWhiteSpace(text) || font == null || maxWidth <= 0)
            return text;

        var paragraphs = text.Replace("\r\n", "\n").Split('\n');
        var wrappedParagraphs = new List<string>(paragraphs.Length);

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                wrappedParagraphs.Add(string.Empty);
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;
            var lines = new List<string>();

            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (font.MeasureString(candidate).X * scale <= maxWidth)
                {
                    line = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(line))
                    lines.Add(line);

                line = word;
            }

            if (!string.IsNullOrEmpty(line))
                lines.Add(line);

            wrappedParagraphs.Add(string.Join('\n', lines));
        }

        return string.Join('\n', wrappedParagraphs);
    }

    private static int CountLines(string text)
        => string.IsNullOrEmpty(text) ? 1 : text.Count(ch => ch == '\n') + 1;

    private static Point GetCenteredWindowPosition(int windowWidth, int windowHeight)
    {
        if (!ServiceLocator.Has<GraphicsDevice>())
            return Point.Zero;

        var viewport = ServiceLocator.Get<GraphicsDevice>().Viewport;
        return new Point(
            Math.Max(0, (viewport.Width - windowWidth) / 2),
            Math.Max(0, (viewport.Height - windowHeight) / 2));
    }

    private static string EscapeXml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
