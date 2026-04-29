#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

/// <summary>
/// UE-Blueprint style node graph for AI location pathfinding. Edges are
/// undirected — direction in which you drag the wire is irrelevant.
/// Drag a map from the left list onto the canvas to create a node;
/// drag any pin to another node to add an edge;
/// click an edge to remove it; click X on a node to remove node + edges.
/// Node positions are stored in canvas-local coordinates so they survive viewport resizes.
/// </summary>
public sealed class LocationGraphPanel
{
    private const int NodeWidth = 176;
    private const int NodeHeight = 52;
    private const int PinRadius = 6;
    private const int CloseBoxSize = 14;
    private const int DragThreshold = 4;
    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;

    private sealed class GraphNode
    {
        public Guid Id = Guid.NewGuid();
        public string MapId = "";
        public string Name = "";
        public Point LocalPos; // relative to canvas top-left
    }

    private readonly record struct GraphEdge(Guid A, Guid B)
    {
        public bool Connects(Guid x, Guid y)
            => (A == x && B == y) || (A == y && B == x);
    }

    private readonly GraphicsDevice _graphics;
    private readonly List<GraphNode> _nodes = new();
    private readonly List<GraphEdge> _edges = new();

    private Rectangle _bounds;
    private Rectangle _searchRect;
    private Rectangle _listRect;
    private Rectangle _canvasRect;

    private string _searchText = "";
    private bool _searchFocused;
    private int _listScroll;

    private string? _listDragMapId;
    private Point _listDragStart;
    private bool _listDragActive;

    private Guid? _draggingNodeId;
    private Point _dragOffset;

    private Guid? _linkingFromId;
    private Point _linkingPos;

    private int _hoverEdgeIndex = -1;

    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;

    public bool IsTyping => _searchFocused;

    public LocationGraphPanel(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    // ── Persistence ────────────────────────────────────────────────

    public void SyncFromWorldData(WorldData worldData)
    {
        _nodes.Clear();
        _edges.Clear();
        var graph = worldData.LocationGraph;
        if (graph == null) return;

        var idLookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.MapId)) continue;
            if (idLookup.ContainsKey(n.MapId)) continue;
            var node = new GraphNode
            {
                MapId = n.MapId,
                Name = n.MapId,
                LocalPos = new Point(n.X, n.Y)
            };
            _nodes.Add(node);
            idLookup[n.MapId] = node.Id;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in graph.Edges)
        {
            if (!idLookup.TryGetValue(e.A, out var a)) continue;
            if (!idLookup.TryGetValue(e.B, out var b)) continue;
            if (a == b) continue;
            var key = string.CompareOrdinal(e.A, e.B) <= 0 ? $"{e.A}|{e.B}" : $"{e.B}|{e.A}";
            if (!seen.Add(key)) continue;
            _edges.Add(new GraphEdge(a, b));
        }
    }

    public void WriteToWorldData(WorldData worldData)
    {
        var graph = new LocationGraphData
        {
            Nodes = _nodes.Select(n => new LocationGraphNodeData
            {
                MapId = n.MapId,
                X = n.LocalPos.X,
                Y = n.LocalPos.Y
            }).ToList()
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _edges)
        {
            var na = _nodes.FirstOrDefault(n => n.Id == e.A);
            var nb = _nodes.FirstOrDefault(n => n.Id == e.B);
            if (na == null || nb == null) continue;
            var key = string.CompareOrdinal(na.MapId, nb.MapId) <= 0
                ? $"{na.MapId}|{nb.MapId}"
                : $"{nb.MapId}|{na.MapId}";
            if (!seen.Add(key)) continue;
            graph.Edges.Add(new LocationGraphEdgeData { A = na.MapId, B = nb.MapId });
        }

        worldData.LocationGraph = graph;
    }

    // ── Update / Draw ─────────────────────────────────────────────

    public bool Update(
        MouseState mouse,
        MouseState prev,
        KeyboardState keys,
        KeyboardState prevKeys,
        IReadOnlyList<MapCatalogEntry> mapCatalog,
        Rectangle bounds)
    {
        _bounds = bounds;
        RebuildLayout();

        var filtered = FilterMaps(mapCatalog);
        var maxScroll = Math.Max(0, filtered.Count - VisibleListRows());
        if (_listScroll > maxScroll) _listScroll = maxScroll;
        if (_listScroll < 0) _listScroll = 0;

        var scrollDelta = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scrollDelta != 0 && _listRect.Contains(mouse.Position))
            _listScroll = Math.Clamp(_listScroll - Math.Sign(scrollDelta), 0, maxScroll);

        var leftDown = mouse.LeftButton == ButtonState.Pressed;
        var leftPrev = prev.LeftButton == ButtonState.Pressed;
        var justPressed = leftDown && !leftPrev;
        var justReleased = !leftDown && leftPrev;

        if (justPressed)
            HandleMouseDown(mouse.Position, filtered);

        if (leftDown)
        {
            if (_listDragMapId != null && !_listDragActive &&
                Math.Abs(mouse.Position.X - _listDragStart.X) + Math.Abs(mouse.Position.Y - _listDragStart.Y) > DragThreshold)
            {
                _listDragActive = true;
            }

            if (_draggingNodeId is Guid moveId)
            {
                var node = _nodes.FirstOrDefault(n => n.Id == moveId);
                if (node != null)
                {
                    var lx = mouse.Position.X - _canvasRect.X - _dragOffset.X;
                    var ly = mouse.Position.Y - _canvasRect.Y - _dragOffset.Y;
                    node.LocalPos = ClampLocalPos(new Point(lx, ly));
                }
            }

            if (_linkingFromId.HasValue)
                _linkingPos = mouse.Position;
        }

        if (justReleased)
            HandleMouseUp(mouse.Position);

        UpdateHoverEdge(mouse.Position);
        HandleTextInput(keys, prevKeys);

        if (_searchFocused && IsPressed(keys, prevKeys, Keys.Escape))
            _searchFocused = false;

        return false;
    }

    public void Draw(SpriteBatch spriteBatch, IReadOnlyList<MapCatalogEntry> mapCatalog)
    {
        RebuildLayout();
        var filtered = FilterMaps(mapCatalog);

        DrawListColumn(spriteBatch, filtered);
        DrawCanvas(spriteBatch);

        if (_listDragActive && _listDragMapId != null)
            DrawListDragGhost(spriteBatch);
    }

    private void RebuildLayout()
    {
        var listW = 250;
        _searchRect = new Rectangle(_bounds.X + 8, _bounds.Y + 8, listW - 8, 26);
        _listRect = new Rectangle(_bounds.X + 8, _searchRect.Bottom + 6, listW - 8, _bounds.Bottom - _searchRect.Bottom - 14);
        _canvasRect = new Rectangle(_listRect.Right + 10, _bounds.Y + 8, _bounds.Right - _listRect.Right - 18, _bounds.Height - 16);
    }

    private int VisibleListRows() => Math.Max(1, (_listRect.Height - 8) / 28);

    private List<MapCatalogEntry> FilterMaps(IReadOnlyList<MapCatalogEntry> mapCatalog)
    {
        IEnumerable<MapCatalogEntry> q = mapCatalog;
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var needle = _searchText.Trim();
            q = q.Where(m =>
                (m.Name ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (m.Id ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderByDescending(m => m.InGame)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    // ── Input ──────────────────────────────────────────────────────

    private void HandleMouseDown(Point p, List<MapCatalogEntry> filtered)
    {
        if (_searchRect.Contains(p))
        {
            _searchFocused = true;
            return;
        }

        _searchFocused = false;

        if (_listRect.Contains(p))
        {
            var row = HitListRow(p, filtered);
            if (row != null)
            {
                _listDragMapId = row.Id;
                _listDragStart = p;
                _listDragActive = false;
            }
            return;
        }

        if (_canvasRect.Contains(p))
        {
            // Either pin → start linking (edges are undirected, so any side works)
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                var node = _nodes[i];
                if (PointInCircle(p, LeftPin(node), PinRadius + 2) ||
                    PointInCircle(p, RightPin(node), PinRadius + 2))
                {
                    _linkingFromId = node.Id;
                    _linkingPos = p;
                    return;
                }
            }

            // X button → delete node
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                var node = _nodes[i];
                if (CloseBox(node).Contains(p))
                {
                    DeleteNode(node.Id);
                    return;
                }
            }

            // Node body → start move (and bring to front)
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                var node = _nodes[i];
                if (NodeRect(node).Contains(p))
                {
                    _draggingNodeId = node.Id;
                    _dragOffset = new Point(p.X - (node.LocalPos.X + _canvasRect.X), p.Y - (node.LocalPos.Y + _canvasRect.Y));
                    var existing = _nodes[i];
                    _nodes.RemoveAt(i);
                    _nodes.Add(existing);
                    return;
                }
            }

            // Edge hit → delete
            var edgeIdx = HitEdge(p);
            if (edgeIdx >= 0)
            {
                _edges.RemoveAt(edgeIdx);
                _hoverEdgeIndex = -1;
                return;
            }
        }
    }

    private void HandleMouseUp(Point p)
    {
        if (_draggingNodeId.HasValue)
            _draggingNodeId = null;

        if (_linkingFromId is Guid fromId)
        {
            var target = NodeAtPoint(p);
            if (target != null && target.Id != fromId &&
                !_edges.Any(e => e.Connects(fromId, target.Id)))
            {
                _edges.Add(new GraphEdge(fromId, target.Id));
            }
            _linkingFromId = null;
        }

        if (_listDragActive && _listDragMapId != null && _canvasRect.Contains(p))
        {
            if (!_nodes.Any(n => string.Equals(n.MapId, _listDragMapId, StringComparison.OrdinalIgnoreCase)))
            {
                var local = ClampLocalPos(new Point(
                    p.X - _canvasRect.X - NodeWidth / 2,
                    p.Y - _canvasRect.Y - NodeHeight / 2));
                _nodes.Add(new GraphNode
                {
                    MapId = _listDragMapId,
                    Name = _listDragMapId,
                    LocalPos = local
                });
            }
        }

        _listDragMapId = null;
        _listDragActive = false;
    }

    private void UpdateHoverEdge(Point p)
    {
        if (!_canvasRect.Contains(p))
        {
            _hoverEdgeIndex = -1;
            return;
        }

        foreach (var n in _nodes)
        {
            if (NodeRect(n).Contains(p) ||
                PointInCircle(p, LeftPin(n), PinRadius + 2) ||
                PointInCircle(p, RightPin(n), PinRadius + 2))
            {
                _hoverEdgeIndex = -1;
                return;
            }
        }

        _hoverEdgeIndex = HitEdge(p);
    }

    private int HitEdge(Point p)
    {
        const float threshold = 6f;
        var best = -1;
        var bestDist = threshold;
        for (var i = 0; i < _edges.Count; i++)
        {
            var (a, b) = ResolveEdge(_edges[i]);
            if (a == null || b == null) continue;
            var (pa, pb) = BestPinPair(a, b);
            var dist = DistanceToBezier(p, pa, pb);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    private GraphNode? NodeAtPoint(Point p)
    {
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i];
            if (NodeRect(n).Contains(p) ||
                PointInCircle(p, LeftPin(n), PinRadius + 2) ||
                PointInCircle(p, RightPin(n), PinRadius + 2))
                return n;
        }
        return null;
    }

    private MapCatalogEntry? HitListRow(Point p, List<MapCatalogEntry> filtered)
    {
        var rowH = 28;
        var y = _listRect.Y + 4 + 14;
        for (var i = _listScroll; i < filtered.Count; i++)
        {
            if (y + rowH > _listRect.Bottom - 4) break;
            var rect = new Rectangle(_listRect.X + 4, y, _listRect.Width - 8, rowH - 2);
            if (rect.Contains(p)) return filtered[i];
            y += rowH;
        }
        return null;
    }

    private void DeleteNode(Guid id)
    {
        _nodes.RemoveAll(n => n.Id == id);
        _edges.RemoveAll(e => e.A == id || e.B == id);
        if (_draggingNodeId == id) _draggingNodeId = null;
        if (_linkingFromId == id) _linkingFromId = null;
    }

    // ── Drawing ────────────────────────────────────────────────────

    private void DrawListColumn(SpriteBatch sb, List<MapCatalogEntry> filtered)
    {
        EditorTheme.FillRect(sb, _searchRect, _searchFocused ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(sb, _searchRect, _searchFocused ? EditorTheme.Warning : EditorTheme.Border);
        var placeholder = string.IsNullOrEmpty(_searchText) && !_searchFocused;
        var displayText = placeholder ? "Search maps..." : _searchText + (_searchFocused ? "│" : "");
        EditorTheme.DrawText(sb, EditorTheme.Small, displayText,
            new Vector2(_searchRect.X + 6, _searchRect.Y + 6),
            placeholder ? EditorTheme.TextMuted : EditorTheme.Text);

        EditorTheme.DrawPanel(sb, _listRect, EditorTheme.PanelAlt, EditorTheme.Border);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, $"{filtered.Count} maps · drag to canvas",
            new Vector2(_listRect.X + 8, _listRect.Y + 4), EditorTheme.TextMuted);

        var rowH = 28;
        var y = _listRect.Y + 4 + 14;
        for (var i = _listScroll; i < filtered.Count; i++)
        {
            if (y + rowH > _listRect.Bottom - 4) break;
            var map = filtered[i];
            var rect = new Rectangle(_listRect.X + 4, y, _listRect.Width - 8, rowH - 2);
            var inGraph = _nodes.Any(n => string.Equals(n.MapId, map.Id, StringComparison.OrdinalIgnoreCase));
            EditorTheme.FillRect(sb, rect, inGraph ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(sb, rect, inGraph ? EditorTheme.AccentDim : EditorTheme.Border);
            EditorTheme.DrawText(sb, EditorTheme.Small, Truncate(map.Name, rect.Width - 16),
                new Vector2(rect.X + 6, rect.Y + 4), inGraph ? EditorTheme.TextDim : EditorTheme.Text);
            EditorTheme.DrawText(sb, EditorTheme.Tiny, Truncate(map.Id, rect.Width - 16),
                new Vector2(rect.X + 6, rect.Y + 16), EditorTheme.TextMuted);
            y += rowH;
        }
    }

    private void DrawCanvas(SpriteBatch sb)
    {
        EditorTheme.DrawPanel(sb, _canvasRect, EditorTheme.BgDeep, EditorTheme.Border);
        DrawCanvasGrid(sb);

        for (var i = 0; i < _edges.Count; i++)
        {
            var (a, b) = ResolveEdge(_edges[i]);
            if (a == null || b == null) continue;
            var (pa, pb) = BestPinPair(a, b);
            var color = i == _hoverEdgeIndex ? EditorTheme.Error : EditorTheme.Accent;
            DrawBezier(sb, pa, pb, color, i == _hoverEdgeIndex ? 3 : 2);
        }

        if (_linkingFromId is Guid fromId)
        {
            var src = _nodes.FirstOrDefault(n => n.Id == fromId);
            if (src != null)
            {
                // Pick the pin closest to current cursor for a natural look
                var lp = LeftPin(src);
                var rp = RightPin(src);
                var pin = SqDist(lp, _linkingPos) < SqDist(rp, _linkingPos) ? lp : rp;
                DrawBezier(sb, pin, _linkingPos, EditorTheme.AccentHover, 2);
            }
        }

        foreach (var node in _nodes)
            DrawNode(sb, node);
    }

    private void DrawCanvasGrid(SpriteBatch sb)
    {
        const int step = 24;
        // Subtle dark gray grid — barely visible, not eye-straining.
        var color = new Color(38, 38, 42);
        for (var x = _canvasRect.X + step; x < _canvasRect.Right; x += step)
            sb.Draw(EditorTheme.Pixel, new Rectangle(x, _canvasRect.Y + 1, 1, _canvasRect.Height - 2), color);
        for (var y = _canvasRect.Y + step; y < _canvasRect.Bottom; y += step)
            sb.Draw(EditorTheme.Pixel, new Rectangle(_canvasRect.X + 1, y, _canvasRect.Width - 2, 1), color);
    }

    private void DrawNode(SpriteBatch sb, GraphNode node)
    {
        var rect = NodeRect(node);
        EditorTheme.DrawShadow(sb, rect, 4);
        EditorTheme.FillRect(sb, rect, EditorTheme.Panel);
        EditorTheme.DrawBorder(sb, rect, EditorTheme.BorderStrong);

        var titleStrip = new Rectangle(rect.X, rect.Y, rect.Width, 18);
        EditorTheme.FillRect(sb, titleStrip, EditorTheme.Accent);

        EditorTheme.DrawText(sb, EditorTheme.Small, Truncate(node.Name, rect.Width - 28),
            new Vector2(rect.X + 8, rect.Y + 3), Color.White);

        EditorTheme.DrawText(sb, EditorTheme.Tiny, Truncate(node.MapId, rect.Width - 16),
            new Vector2(rect.X + 8, rect.Y + 24), EditorTheme.TextDim);

        var close = CloseBox(node);
        EditorTheme.FillRect(sb, close, EditorTheme.AccentDim);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, "x",
            new Vector2(close.X + 4, close.Y + 1), Color.White);

        // Both pins same color — direction is irrelevant
        DrawPin(sb, LeftPin(node), EditorTheme.AccentHover);
        DrawPin(sb, RightPin(node), EditorTheme.AccentHover);
    }

    private void DrawPin(SpriteBatch sb, Point center, Color color)
    {
        var r = PinRadius;
        var rect = new Rectangle(center.X - r, center.Y - r, r * 2, r * 2);
        EditorTheme.FillRect(sb, rect, color);
        EditorTheme.DrawBorder(sb, rect, EditorTheme.Border);
    }

    private void DrawListDragGhost(SpriteBatch sb)
    {
        var mouseState = Mouse.GetState();
        var p = mouseState.Position;
        var rect = new Rectangle(p.X - NodeWidth / 2, p.Y - NodeHeight / 2, NodeWidth, NodeHeight);
        EditorTheme.FillRect(sb, rect, new Color(0, 122, 204, 140));
        EditorTheme.DrawBorder(sb, rect, EditorTheme.AccentHover);
        EditorTheme.DrawText(sb, EditorTheme.Small, Truncate(_listDragMapId ?? "", rect.Width - 16),
            new Vector2(rect.X + 8, rect.Y + 6), Color.White);
    }

    private void DrawBezier(SpriteBatch sb, Point a, Point b, Color color, int thickness)
    {
        var samples = SampleBezier(a, b, 28);
        for (var i = 0; i < samples.Length - 1; i++)
            DrawLine(sb, samples[i], samples[i + 1], color, thickness);
    }

    private static Vector2[] SampleBezier(Point a, Point b, int n)
    {
        var p0 = new Vector2(a.X, a.Y);
        var p3 = new Vector2(b.X, b.Y);
        var dx = p3.X - p0.X;
        var handle = MathF.Max(40f, MathF.Abs(dx) * 0.5f);
        // Bend horizontally — use sign of dx so curve flows the right way for left-to-right or right-to-left.
        var sign = dx >= 0 ? 1f : -1f;
        var p1 = new Vector2(p0.X + handle * sign, p0.Y);
        var p2 = new Vector2(p3.X - handle * sign, p3.Y);

        var pts = new Vector2[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var t = i / (float)n;
            var u = 1f - t;
            pts[i] = u * u * u * p0
                   + 3 * u * u * t * p1
                   + 3 * u * t * t * p2
                   + t * t * t * p3;
        }
        return pts;
    }

    private float DistanceToBezier(Point p, Point a, Point b)
    {
        var pts = SampleBezier(a, b, 28);
        var pv = new Vector2(p.X, p.Y);
        var best = float.MaxValue;
        for (var i = 0; i < pts.Length - 1; i++)
        {
            var d = DistanceToSegment(pv, pts[i], pts[i + 1]);
            if (d < best) best = d;
        }
        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 1e-4f) return Vector2.Distance(p, a);
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    private void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, int thickness)
    {
        var diff = b - a;
        var length = diff.Length();
        if (length < 0.5f) return;
        var angle = MathF.Atan2(diff.Y, diff.X);
        sb.Draw(EditorTheme.Pixel,
            new Rectangle((int)a.X, (int)a.Y, (int)length, thickness),
            null, color, angle, new Vector2(0, thickness * 0.5f), SpriteEffects.None, 0f);
    }

    // ── Geometry helpers ──────────────────────────────────────────

    private Rectangle NodeRect(GraphNode node)
        => new(node.LocalPos.X + _canvasRect.X, node.LocalPos.Y + _canvasRect.Y, NodeWidth, NodeHeight);

    private Point LeftPin(GraphNode node)
        => new(node.LocalPos.X + _canvasRect.X, node.LocalPos.Y + _canvasRect.Y + NodeHeight / 2);

    private Point RightPin(GraphNode node)
        => new(node.LocalPos.X + _canvasRect.X + NodeWidth, node.LocalPos.Y + _canvasRect.Y + NodeHeight / 2);

    private Rectangle CloseBox(GraphNode node)
        => new(node.LocalPos.X + _canvasRect.X + NodeWidth - CloseBoxSize - 3,
               node.LocalPos.Y + _canvasRect.Y + 2,
               CloseBoxSize, CloseBoxSize);

    /// <summary>Pick the pin pair (left or right of each node) that gives the shortest, cleanest curve.</summary>
    private (Point, Point) BestPinPair(GraphNode a, GraphNode b)
    {
        Point[] aOpts = { LeftPin(a), RightPin(a) };
        Point[] bOpts = { LeftPin(b), RightPin(b) };
        var bestSq = int.MaxValue;
        var bestA = aOpts[0];
        var bestB = bOpts[0];
        foreach (var pa in aOpts)
            foreach (var pb in bOpts)
            {
                var sq = SqDist(pa, pb);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestA = pa;
                    bestB = pb;
                }
            }
        return (bestA, bestB);
    }

    private static int SqDist(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static bool PointInCircle(Point p, Point center, int radius)
    {
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private Point ClampLocalPos(Point local)
        => new(Math.Clamp(local.X, 4, Math.Max(4, _canvasRect.Width - NodeWidth - 4)),
               Math.Clamp(local.Y, 4, Math.Max(4, _canvasRect.Height - NodeHeight - 4)));

    private (GraphNode? a, GraphNode? b) ResolveEdge(GraphEdge e)
        => (_nodes.FirstOrDefault(n => n.Id == e.A), _nodes.FirstOrDefault(n => n.Id == e.B));

    // ── Text input (search box) ───────────────────────────────────

    private void HandleTextInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (!_searchFocused)
        {
            ResetDeleteRepeat();
            return;
        }

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static k => k))
        {
            if (key == Keys.Escape)
            {
                if (!prevKeys.IsKeyDown(key))
                {
                    _searchFocused = false;
                    ResetDeleteRepeat();
                    return;
                }
                continue;
            }

            if (key is Keys.Back or Keys.Delete)
            {
                if (ShouldRepeatKey(keys, prevKeys, key))
                {
                    if (_searchText.Length > 0)
                        _searchText = _searchText[..^1];
                }
                deleteHandled = true;
                continue;
            }

            if (prevKeys.IsKeyDown(key))
                continue;

            var character = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (character == '\0') continue;
            _searchText += character;
            _listScroll = 0;
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private bool ShouldRepeatKey(KeyboardState keys, KeyboardState prevKeys, Keys key)
    {
        var isDown = keys.IsKeyDown(key);
        if (!isDown)
        {
            if (_heldDeleteKey == key) ResetDeleteRepeat();
            return false;
        }

        var now = Environment.TickCount64;
        if (prevKeys.IsKeyUp(key) || _heldDeleteKey != key)
        {
            _heldDeleteKey = key;
            _nextDeleteRepeatAt = now + DeleteRepeatInitialDelayMs;
            return true;
        }

        if (now < _nextDeleteRepeatAt) return false;
        _nextDeleteRepeatAt = now + DeleteRepeatIntervalMs;
        return true;
    }

    private void ResetDeleteRepeat()
    {
        _heldDeleteKey = null;
        _nextDeleteRepeatAt = 0;
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => '-',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static bool IsPressed(KeyboardState keys, KeyboardState prevKeys, Keys key)
        => keys.IsKeyDown(key) && prevKeys.IsKeyUp(key);

    private static string Truncate(string value, int maxWidth)
    {
        if (EditorTheme.Small.MeasureString(value).X <= maxWidth) return value;
        while (value.Length > 1 && EditorTheme.Small.MeasureString(value + "...").X > maxWidth)
            value = value[..^1];
        return value + "...";
    }
}
