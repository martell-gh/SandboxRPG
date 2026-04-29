using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Npc;
using MTEngine.UI;
using MTEngine.World;

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
        if (Owner == null)
            yield break;

        var item = Owner.GetComponent<Items.ItemComponent>();
        var isHeldByActor = item?.ContainedIn == ctx.Actor;
        if (ctx.Target != Owner && !(isHeldByActor && ctx.Target == ctx.Actor))
            yield break;

        yield return new InteractionEntry
        {
            Id = "info.examine",
            Label = "Инфо",
            Priority = -10,
            InterruptsCurrentAction = false,
            Execute = _ => OpenInfoWindow(Owner)
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
        var identity = target.GetComponent<IdentityComponent>();
        var age = target.GetComponent<AgeComponent>();

        var name = identity != null && !string.IsNullOrWhiteSpace(identity.FullName)
            ? identity.FullName
            : LocalizationManager.T(interactable?.DisplayName ?? target.Name ?? "???");
        var description = ComposeEntityInfo(identity, age, target) + LocalizationManager.T(info?.Description ?? "");
        var font = GetUiFont(ui);
        const int windowWidth = 420;
        const int leftColumnWidth = 86;
        const int panelPadding = 8;
        const int gap = 10;
        const int descriptionWidth = windowWidth - (panelPadding * 2) - leftColumnWidth - gap - 16;
        var wrappedDescription = WrapText(description, font, descriptionWidth, 1f);
        var descriptionHeight = MeasureWrappedTextHeight(wrappedDescription, font, 1f, 8);
        var contentHeight = Math.Max(80, descriptionHeight) + panelPadding * 2;
        var windowHeight = Math.Max(168, XmlWindow.DefaultTitleBarHeight + contentHeight);

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

    private static int MeasureWrappedTextHeight(string wrappedText, SpriteFont? font, float scale, int extraPadding = 0)
    {
        if (font == null)
            return 72;

        var lineCount = CountLines(wrappedText);
        var lineHeight = Math.Max(1, (int)MathF.Ceiling(font.LineSpacing * scale));
        return lineCount * lineHeight + extraPadding;
    }

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

    private static string ComposeEntityInfo(IdentityComponent? identity, AgeComponent? age, Entity target)
    {
        if (target.HasComponent<PlayerTagComponent>())
            return ComposePlayerInfo(identity, age);

        return ComposeNpcInfo(identity, age, target);
    }

    private static string ComposePlayerInfo(IdentityComponent? identity, AgeComponent? age)
    {
        var lines = new List<string>();
        if (age != null && age.Years > 0)
            lines.Add($"Возраст: {age.Years} {YearWord(age.Years)}");

        var factionId = identity?.FactionId ?? "";
        var faction = ResolveFactionName(factionId);
        lines.Add($"Фракция: {(string.IsNullOrWhiteSpace(faction) ? "нет" : faction)}");

        return lines.Count == 0 ? "" : string.Join("\n", lines) + "\n\n";
    }

    private static string ComposeNpcInfo(IdentityComponent? identity, AgeComponent? age, Entity target)
    {
        if (identity == null)
            return "";

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(identity.FullName))
            lines.Add($"Имя: {identity.FullName}");

        lines.Add($"Пол: {(identity.Gender == Gender.Female ? "женский" : "мужской")}");

        if (age != null && age.Years > 0)
            lines.Add($"Возраст: {age.Years} {YearWord(age.Years)}");

        if (!string.IsNullOrWhiteSpace(identity.SettlementId))
        {
            var residence = identity.SettlementId;
            if (!string.IsNullOrWhiteSpace(identity.DistrictId))
                residence += $", {identity.DistrictId}";
            lines.Add($"Прописан: {residence}");
        }

        var kin = target.GetComponent<KinComponent>();
        if (kin != null && kin.Links.Count > 0)
            lines.Add($"Родня: {kin.Links.Count}");

        var relationships = target.GetComponent<RelationshipsComponent>();
        if (relationships != null)
        {
            var partnerLine = BuildPartnerLine(target, relationships);
            if (!string.IsNullOrEmpty(partnerLine))
                lines.Add(partnerLine);
            else if (relationships.Status == RelationshipStatus.Widowed)
                lines.Add(identity.Gender == Gender.Female ? "Вдова" : "Вдовец");
        }

        var playerRelLine = BuildPlayerRelationshipLines(target, relationships);
        foreach (var line in playerRelLine)
            lines.Add(line);

        return string.Join("\n", lines) + "\n\n";
    }

    /// <summary>Returns "Жена: Имя" / "Возлюбленный: Имя" only for active relationships, otherwise empty.</summary>
    private static string BuildPartnerLine(Entity target, RelationshipsComponent rel)
    {
        if (rel.Status is not (RelationshipStatus.Dating or RelationshipStatus.Engaged or RelationshipStatus.Married))
            return "";

        var (partnerName, partnerGender) = ResolvePartner(target, rel);
        if (string.IsNullOrWhiteSpace(partnerName))
            return "";

        var role = PartnerRoleLabel(rel.Status, partnerGender);
        return $"{role}: {partnerName}";
    }

    private static (string name, Gender gender) ResolvePartner(Entity target, RelationshipsComponent rel)
    {
        var world = target.World;
        if (rel.PartnerIsPlayer)
        {
            var player = world?.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();
            var pid = player?.GetComponent<IdentityComponent>();
            var name = !string.IsNullOrWhiteSpace(pid?.FullName) ? pid!.FullName : "Игрок";
            return (name, pid?.Gender ?? Gender.Male);
        }

        if (string.IsNullOrWhiteSpace(rel.PartnerNpcSaveId) || world == null)
            return ("", Gender.Male);

        foreach (var npc in world.GetEntitiesWith<SaveEntityIdComponent>())
        {
            var marker = npc.GetComponent<SaveEntityIdComponent>();
            if (marker == null || !string.Equals(marker.SaveId, rel.PartnerNpcSaveId, StringComparison.OrdinalIgnoreCase))
                continue;

            var pid = npc.GetComponent<IdentityComponent>();
            var name = !string.IsNullOrWhiteSpace(pid?.FullName) ? pid!.FullName : "";
            return (name, pid?.Gender ?? Gender.Male);
        }

        return ("", Gender.Male);
    }

    private static string PartnerRoleLabel(RelationshipStatus status, Gender partnerGender)
        => (status, partnerGender) switch
        {
            (RelationshipStatus.Married, Gender.Female) => "Жена",
            (RelationshipStatus.Married, Gender.Male)   => "Муж",
            (RelationshipStatus.Dating or RelationshipStatus.Engaged, Gender.Female) => "Возлюбленная",
            (RelationshipStatus.Dating or RelationshipStatus.Engaged, Gender.Male)   => "Возлюбленный",
            _ => "Партнёр"
        };

    private static IEnumerable<string> BuildPlayerRelationshipLines(Entity target, RelationshipsComponent? rel)
    {
        var lines = new List<string>();
        var playerRel = target.GetComponent<RelationshipWithPlayerComponent>();
        if (playerRel != null)
        {
            if (playerRel.Friendship > 0)
                lines.Add($"Дружба с вами: {playerRel.Friendship}/100");
            if (playerRel.Romance > 0)
                lines.Add($"Романтика с вами: {playerRel.Romance}/100");
        }

        if (rel != null && rel.PlayerOpinion != 0)
            lines.Add($"Мнение о вас: {rel.PlayerOpinion:+#;-#;0}");

        return lines;
    }

    private static string YearWord(int years)
    {
        var n = Math.Abs(years) % 100;
        if (n is >= 11 and <= 14)
            return "лет";

        return (n % 10) switch
        {
            1 => "год",
            2 or 3 or 4 => "года",
            _ => "лет"
        };
    }

    private static string ResolveFactionName(string factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId))
            return "";

        if (ServiceLocator.Has<MapManager>())
        {
            var faction = ServiceLocator.Get<MapManager>().GetFaction(factionId);
            if (faction != null)
                return LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name);
        }

        return factionId;
    }
}
