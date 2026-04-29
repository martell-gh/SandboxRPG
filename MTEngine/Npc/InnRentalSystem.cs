using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Metabolism;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.World;

namespace MTEngine.Npc;

[SaveObject("innRental")]
public class InnRentalSystem : GameSystem
{
    private const int DefaultRoomPrice = 20;
    private const int DefaultBedPrice = 8;
    private const int MaxRoomTiles = 18;

    [SaveField("rentals")]
    public List<InnRentalRecord> Rentals { get; set; } = new();

    private MapManager? _mapManager;
    private GameClock? _clock;
    private readonly HashSet<int> _highlightedDoors = new();

    public override void Update(float deltaTime)
    {
        if (!EnsureServices())
            return;

        RemoveExpiredRentals();
        RefreshDoorHighlights();
    }

    public bool TryBuildOffer(Entity innkeeper, Entity renter, out InnRentalOffer offer)
    {
        offer = default;
        if (!EnsureServices() || !MerchantWorkRules.IsInnkeeper(innkeeper))
            return false;

        var map = _mapManager!.CurrentMap;
        var tileMap = _mapManager.CurrentTileMap;
        if (map == null || tileMap == null)
            return false;

        RemoveExpiredRentals();
        var rooms = BuildRoomOffers(map, tileMap, renter).ToList();
        if (rooms.Count > 0)
        {
            offer = rooms[0];
            return true;
        }

        var beds = BuildBedOffers(map, tileMap, renter).ToList();
        if (beds.Count == 0)
            return false;

        offer = beds[0];
        return true;
    }

    public bool TryRent(Entity renter, Entity innkeeper)
    {
        if (!TryBuildOffer(innkeeper, renter, out var offer))
        {
            PopupTextSystem.Show(renter, "Свободных мест нет.", Color.LightGoldenrodYellow, lifetime: 1.5f);
            return false;
        }

        var currency = renter.GetComponent<CurrencyComponent>() ?? renter.AddComponent(new CurrencyComponent());
        if (!currency.TrySpend(offer.Price))
        {
            PopupTextSystem.Show(renter, $"Нужно {offer.Price} {currency.Symbol}.", Color.LightGoldenrodYellow, lifetime: 1.5f);
            return false;
        }

        var innkeeperCurrency = innkeeper.GetComponent<CurrencyComponent>() ?? innkeeper.AddComponent(new CurrencyComponent());
        innkeeperCurrency.Add(offer.Price);

        var renterSaveId = GetActorId(renter);
        Rentals.RemoveAll(r => IsSameRental(r, offer) || !IsActive(r));
        Rentals.Add(new InnRentalRecord
        {
            Kind = offer.Kind,
            MapId = offer.MapId,
            InnAreaId = offer.InnAreaId,
            RenterSaveId = renterSaveId,
            ExpiresAtAbsoluteSeconds = _clock!.TotalSecondsAbsolute + GameClock.SecondsPerDay,
            DoorTileX = offer.DoorTile.X,
            DoorTileY = offer.DoorTile.Y,
            BedTileX = offer.BedTile.X,
            BedTileY = offer.BedTile.Y,
            Price = offer.Price
        });

        MarkWorldDirty();
        var label = offer.Kind == InnRentalKind.Room ? "Комната снята на сутки." : "Кровать снята на сутки.";
        PopupTextSystem.Show(renter, label, Color.LightGreen, lifetime: 1.6f);
        RefreshDoorHighlights();
        return true;
    }

    public bool CanActorUseBed(Entity actor, Entity bed, BedComponent bedComponent, out string reason)
    {
        reason = "";
        if (!EnsureServices())
            return true;

        var map = _mapManager!.CurrentMap;
        var tileMap = _mapManager.CurrentTileMap;
        var bedTransform = bed.GetComponent<TransformComponent>();
        if (map == null || tileMap == null || bedTransform == null)
            return true;

        var bedTile = WorldToTile(bedTransform.Position, map.TileSize);
        var innArea = FindAreaContaining(map, AreaZoneKinds.Inn, bedTile);
        if (innArea == null)
            return true;

        RemoveExpiredRentals();
        var renterId = GetActorId(actor);
        var room = DetectRoomForBed(map, tileMap, innArea, bed, bedTile);
        if (room != null)
        {
            var rental = Rentals.FirstOrDefault(r =>
                r.Kind == InnRentalKind.Room
                && string.Equals(r.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
                && r.DoorTileX == room.DoorTile.X
                && r.DoorTileY == room.DoorTile.Y
                && IsActive(r));

            if (rental != null && string.Equals(rental.RenterSaveId, renterId, StringComparison.OrdinalIgnoreCase))
                return true;

            reason = rental == null ? "Сначала сними комнату." : "Комната занята.";
            return false;
        }

        var bedRental = Rentals.FirstOrDefault(r =>
            r.Kind == InnRentalKind.Bed
            && string.Equals(r.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
            && r.BedTileX == bedTile.X
            && r.BedTileY == bedTile.Y
            && IsActive(r));

        if (bedRental != null && string.Equals(bedRental.RenterSaveId, renterId, StringComparison.OrdinalIgnoreCase))
            return true;

        reason = bedRental == null ? "Сначала сними кровать." : "Кровать уже снята.";
        return false;
    }

    public bool TryGetDoorAccess(Entity actor, Entity door, out bool allowed)
    {
        allowed = false;
        if (!EnsureServices())
            return false;

        var map = _mapManager!.CurrentMap;
        var tileMap = _mapManager.CurrentTileMap;
        var doorTransform = door.GetComponent<TransformComponent>();
        if (map == null || tileMap == null || doorTransform == null)
            return false;

        RemoveExpiredRentals();
        var doorTile = WorldToTile(doorTransform.Position, map.TileSize);
        var rental = Rentals.FirstOrDefault(r =>
            r.Kind == InnRentalKind.Room
            && string.Equals(r.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
            && r.DoorTileX == doorTile.X
            && r.DoorTileY == doorTile.Y
            && IsActive(r));
        if (rental != null)
        {
            allowed = string.Equals(rental.RenterSaveId, GetActorId(actor), StringComparison.OrdinalIgnoreCase);
            return true;
        }

        foreach (var area in map.Areas.Where(a => string.Equals(a.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var bed in GetBedsInArea(map, area))
            {
                var bedTile = WorldToTile(bed.GetComponent<TransformComponent>()!.Position, map.TileSize);
                var room = DetectRoomForBed(map, tileMap, area, bed, bedTile);
                if (room != null && room.DoorTile == doorTile)
                {
                    allowed = false;
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerable<InnRentalOffer> BuildRoomOffers(MapData map, TileMap tileMap, Entity renter)
    {
        var seenDoors = new HashSet<Point>();
        foreach (var area in map.Areas.Where(a => string.Equals(a.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var bed in GetBedsInArea(map, area))
            {
                var bedTile = WorldToTile(bed.GetComponent<TransformComponent>()!.Position, map.TileSize);
                var room = DetectRoomForBed(map, tileMap, area, bed, bedTile);
                if (room == null || !seenDoors.Add(room.DoorTile))
                    continue;

                if (IsRoomOccupied(map, room, renter) || IsRoomRented(map.Id, room.DoorTile))
                    continue;

                yield return new InnRentalOffer(
                    InnRentalKind.Room,
                    map.Id,
                    area.Id,
                    GetRentPrice(area, "roomPrice", DefaultRoomPrice),
                    room.DoorTile,
                    room.BedTiles.FirstOrDefault());
            }
        }
    }

    private IEnumerable<InnRentalOffer> BuildBedOffers(MapData map, TileMap tileMap, Entity renter)
    {
        foreach (var area in map.Areas.Where(a => string.Equals(a.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var bed in GetBedsInArea(map, area))
            {
                var bedTile = WorldToTile(bed.GetComponent<TransformComponent>()!.Position, map.TileSize);
                if (DetectRoomForBed(map, tileMap, area, bed, bedTile) != null)
                    continue;

                if (IsBedOccupied(map, bedTile, renter) || IsBedRented(map.Id, bedTile))
                    continue;

                yield return new InnRentalOffer(
                    InnRentalKind.Bed,
                    map.Id,
                    area.Id,
                    GetRentPrice(area, "bedPrice", DefaultBedPrice),
                    new Point(-1, -1),
                    bedTile);
            }
        }
    }

    private RoomCandidate? DetectRoomForBed(MapData map, TileMap tileMap, AreaZoneData area, Entity bed, Point bedTile)
    {
        var areaTiles = area.Tiles.Select(t => new Point(t.X, t.Y)).ToHashSet();
        if (!areaTiles.Contains(bedTile))
            return null;

        var doors = GetDoors(map).ToList();
        var doorTiles = doors.Select(d => d.Tile).ToHashSet();
        var start = FindFloodStart(bedTile, areaTiles, doorTiles, tileMap);
        if (start == null)
            return null;

        var region = FloodArea(start.Value, areaTiles, doorTiles, tileMap);
        if (region.Count == 0 || region.Count > MaxRoomTiles)
            return null;

        var regionDoors = doors.Where(d => region.Any(t => Manhattan(t, d.Tile) == 1)).ToList();
        if (regionDoors.Count != 1)
            return null;

        var bedTiles = GetBedsInArea(map, area)
            .Select(b => WorldToTile(b.GetComponent<TransformComponent>()!.Position, map.TileSize))
            .Where(region.Contains)
            .Distinct()
            .ToList();
        if (bedTiles.Count == 0 || bedTiles.Count > 2)
            return null;

        return new RoomCandidate(region, bedTiles, regionDoors[0].Tile, regionDoors[0].Door);
    }

    private Point? FindFloodStart(Point bedTile, HashSet<Point> areaTiles, HashSet<Point> doorTiles, TileMap tileMap)
    {
        if (CanFloodThrough(bedTile, areaTiles, doorTiles, tileMap))
            return bedTile;

        foreach (var tile in Neighbors4(bedTile))
        {
            if (CanFloodThrough(tile, areaTiles, doorTiles, tileMap))
                return tile;
        }

        return null;
    }

    private static HashSet<Point> FloodArea(Point start, HashSet<Point> areaTiles, HashSet<Point> doorTiles, TileMap tileMap)
    {
        var result = new HashSet<Point>();
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        result.Add(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbors4(current))
            {
                if (result.Contains(next) || !CanFloodThrough(next, areaTiles, doorTiles, tileMap))
                    continue;

                result.Add(next);
                queue.Enqueue(next);
            }
        }

        return result;
    }

    private static bool CanFloodThrough(Point tile, HashSet<Point> areaTiles, HashSet<Point> doorTiles, TileMap tileMap)
        => areaTiles.Contains(tile)
           && !doorTiles.Contains(tile)
           && !tileMap.IsSolid(tile.X, tile.Y);

    private bool IsRoomOccupied(MapData map, RoomCandidate room, Entity ignored)
        => room.BedTiles.Any(tile => IsBedOccupied(map, tile, ignored))
           || World.GetEntitiesWith<NpcTagComponent, TransformComponent>()
               .Where(npc => npc != ignored && npc.GetComponent<HealthComponent>()?.IsDead != true)
               .Any(npc =>
               {
                   var intent = npc.GetComponent<NpcIntentComponent>();
                   if (intent is { Action: ScheduleAction.Sleep, HasTarget: true }
                       && room.Tiles.Contains(WorldToTile(intent.TargetPosition, map.TileSize)))
                       return true;

                   return room.Tiles.Contains(WorldToTile(npc.GetComponent<TransformComponent>()!.Position, map.TileSize))
                          && npc.GetComponent<ScheduleComponent>()?.FindSlot(_clock!.HourInt)?.Action == ScheduleAction.Sleep;
               });

    private bool IsBedOccupied(MapData map, Point bedTile, Entity ignored)
        => World.GetEntitiesWith<NpcTagComponent, TransformComponent>()
            .Where(npc => npc != ignored && npc.GetComponent<HealthComponent>()?.IsDead != true)
            .Any(npc =>
            {
                var intent = npc.GetComponent<NpcIntentComponent>();
                if (intent is { Action: ScheduleAction.Sleep, HasTarget: true }
                    && WorldToTile(intent.TargetPosition, map.TileSize) == bedTile)
                    return true;

                return WorldToTile(npc.GetComponent<TransformComponent>()!.Position, map.TileSize) == bedTile
                       && npc.GetComponent<ScheduleComponent>()?.FindSlot(_clock!.HourInt)?.Action == ScheduleAction.Sleep;
            });

    private IEnumerable<Entity> GetBedsInArea(MapData map, AreaZoneData area)
        => World.GetEntitiesWith<BedComponent, TransformComponent>()
            .Where(bed => area.ContainsTile(
                WorldToTile(bed.GetComponent<TransformComponent>()!.Position, map.TileSize).X,
                WorldToTile(bed.GetComponent<TransformComponent>()!.Position, map.TileSize).Y));

    private IEnumerable<DoorInfo> GetDoors(MapData map)
        => World.GetEntitiesWith<DoorComponent, TransformComponent>()
            .Select(door => new DoorInfo(door, WorldToTile(door.GetComponent<TransformComponent>()!.Position, map.TileSize)));

    private bool IsRoomRented(string mapId, Point doorTile)
        => Rentals.Any(r => r.Kind == InnRentalKind.Room
                            && string.Equals(r.MapId, mapId, StringComparison.OrdinalIgnoreCase)
                            && r.DoorTileX == doorTile.X
                            && r.DoorTileY == doorTile.Y
                            && IsActive(r));

    private bool IsBedRented(string mapId, Point bedTile)
        => Rentals.Any(r => r.Kind == InnRentalKind.Bed
                            && string.Equals(r.MapId, mapId, StringComparison.OrdinalIgnoreCase)
                            && r.BedTileX == bedTile.X
                            && r.BedTileY == bedTile.Y
                            && IsActive(r));

    private AreaZoneData? FindAreaContaining(MapData map, string kind, Point tile)
        => map.Areas.FirstOrDefault(area =>
            string.Equals(area.Kind, kind, StringComparison.OrdinalIgnoreCase)
            && area.ContainsTile(tile.X, tile.Y));

    private void RefreshDoorHighlights()
    {
        var map = _mapManager?.CurrentMap;
        if (map == null)
            return;

        var activeDoorTiles = Rentals
            .Where(r => r.Kind == InnRentalKind.Room && string.Equals(r.MapId, map.Id, StringComparison.OrdinalIgnoreCase) && IsActive(r))
            .Select(r => new Point(r.DoorTileX, r.DoorTileY))
            .ToHashSet();

        foreach (var door in World.GetEntitiesWith<DoorComponent, TransformComponent>())
        {
            var sprite = door.GetComponent<SpriteComponent>();
            if (sprite == null)
                continue;

            var tile = WorldToTile(door.GetComponent<TransformComponent>()!.Position, map.TileSize);
            if (activeDoorTiles.Contains(tile))
            {
                sprite.Color = new Color(255, 232, 80);
                _highlightedDoors.Add(door.Id);
            }
            else if (_highlightedDoors.Remove(door.Id))
            {
                sprite.Color = Color.White;
            }
        }
    }

    private void RemoveExpiredRentals()
    {
        var removed = Rentals.RemoveAll(r => !IsActive(r)) > 0;
        if (removed)
            MarkWorldDirty();
    }

    private bool IsActive(InnRentalRecord rental)
        => _clock != null && rental.ExpiresAtAbsoluteSeconds > _clock.TotalSecondsAbsolute;

    private static bool IsSameRental(InnRentalRecord rental, InnRentalOffer offer)
        => string.Equals(rental.MapId, offer.MapId, StringComparison.OrdinalIgnoreCase)
           && rental.Kind == offer.Kind
           && (offer.Kind == InnRentalKind.Room
               ? rental.DoorTileX == offer.DoorTile.X && rental.DoorTileY == offer.DoorTile.Y
               : rental.BedTileX == offer.BedTile.X && rental.BedTileY == offer.BedTile.Y);

    private static int GetRentPrice(AreaZoneData area, string primaryKey, int fallback)
    {
        foreach (var key in new[] { primaryKey, "rentPrice", "price" })
        {
            if (area.Properties.TryGetValue(key, out var raw)
                && int.TryParse(raw, out var value)
                && value >= 0)
            {
                return value;
            }
        }

        return fallback;
    }

    private bool EnsureServices()
    {
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        return _mapManager != null && _clock != null;
    }

    private static Point WorldToTile(Vector2 position, int tileSize)
        => new((int)MathF.Floor(position.X / tileSize), (int)MathF.Floor(position.Y / tileSize));

    private static IEnumerable<Point> Neighbors4(Point p)
    {
        yield return new Point(p.X + 1, p.Y);
        yield return new Point(p.X - 1, p.Y);
        yield return new Point(p.X, p.Y + 1);
        yield return new Point(p.X, p.Y - 1);
    }

    private static int Manhattan(Point a, Point b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static string GetActorId(Entity actor)
        => actor.GetComponent<SaveEntityIdComponent>()?.SaveId is { Length: > 0 } saveId
            ? saveId
            : actor.HasComponent<PlayerTagComponent>() ? "player" : actor.Id.ToString();

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private sealed record DoorInfo(Entity Door, Point Tile);
    private sealed record RoomCandidate(HashSet<Point> Tiles, List<Point> BedTiles, Point DoorTile, Entity Door);
}

public enum InnRentalKind
{
    Bed,
    Room
}

public readonly record struct InnRentalOffer(
    InnRentalKind Kind,
    string MapId,
    string InnAreaId,
    int Price,
    Point DoorTile,
    Point BedTile);

public class InnRentalRecord
{
    public InnRentalKind Kind { get; set; }
    public string MapId { get; set; } = "";
    public string InnAreaId { get; set; } = "";
    public string RenterSaveId { get; set; } = "";
    public double ExpiresAtAbsoluteSeconds { get; set; }
    public int DoorTileX { get; set; }
    public int DoorTileY { get; set; }
    public int BedTileX { get; set; }
    public int BedTileY { get; set; }
    public int Price { get; set; }
}
