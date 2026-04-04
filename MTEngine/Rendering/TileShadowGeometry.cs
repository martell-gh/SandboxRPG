using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.Rendering;

public static class TileShadowGeometry
{
    private const float CornerInset = -0.75f;

    public static bool AppendShadow(
        List<VertexPositionColor> mainVertices,
        List<VertexPositionColor>? featherVertices,
        Vector2 origin,
        int tileX,
        int tileY,
        int tileSize,
        float shadowLength,
        float featherWidth,
        Color mainColor,
        Color featherInnerColor,
        Color featherOuterColor)
    {
        return AppendShadowRect(
            mainVertices, featherVertices, origin,
            tileX * tileSize, tileY * tileSize,
            (tileX + 1) * tileSize, (tileY + 1) * tileSize,
            shadowLength, featherWidth,
            mainColor, featherInnerColor, featherOuterColor);
    }

    public static bool AppendShadowRect(
        List<VertexPositionColor> mainVertices,
        List<VertexPositionColor>? featherVertices,
        Vector2 origin,
        float tileLeft,
        float tileTop,
        float tileRight,
        float tileBottom,
        float shadowLength,
        float featherWidth,
        Color mainColor,
        Color featherInnerColor,
        Color featherOuterColor)
    {
        var left = tileLeft + CornerInset;
        var top = tileTop + CornerInset;
        var right = tileRight - CornerInset;
        var bottom = tileBottom - CornerInset;

        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = new Vector2(left, top);
        corners[1] = new Vector2(right, top);
        corners[2] = new Vector2(right, bottom);
        corners[3] = new Vector2(left, bottom);

        Span<float> angles = stackalloc float[4];
        int minIdx = 0, maxIdx = 0;
        for (int i = 0; i < 4; i++)
        {
            angles[i] = MathF.Atan2(corners[i].Y - origin.Y, corners[i].X - origin.X);
            if (angles[i] < angles[minIdx]) minIdx = i;
            if (angles[i] > angles[maxIdx]) maxIdx = i;
        }

        Span<Vector2> paths = stackalloc Vector2[8];
        int cwCount = BuildBoundaryPath(corners, minIdx, maxIdx, true, paths);
        int ccwCount = BuildBoundaryPath(corners, minIdx, maxIdx, false, paths[4..]);

        bool useCW = AvgDistSq(paths, cwCount, origin) >= AvgDistSq(paths[4..], ccwCount, origin);
        int backOffset = useCW ? 0 : 4;
        int backCount = useCW ? cwCount : ccwCount;

        if (backCount < 2)
            return false;

        var firstBoundary = paths[backOffset];
        var lastBoundary = paths[backOffset + backCount - 1];
        var firstDir = SafeNormalize(firstBoundary - origin);
        var lastDir = SafeNormalize(lastBoundary - origin);
        if (firstDir == Vector2.Zero || lastDir == Vector2.Zero)
            return false;

        var farFirst = firstBoundary + firstDir * shadowLength;
        var farLast = lastBoundary + lastDir * shadowLength;

        Span<Vector2> polygon = stackalloc Vector2[backCount + 2];
        for (int i = 0; i < backCount; i++)
            polygon[i] = paths[backOffset + i];
        polygon[backCount] = farLast;
        polygon[backCount + 1] = farFirst;
        int polyCount = backCount + 2;

        AddPolygon(mainVertices, polygon, polyCount, mainColor);

        if (featherVertices != null && featherWidth > 0f)
        {
            var center = ComputeCenter(polygon, polyCount);
            AddFeather(featherVertices, firstBoundary, farFirst, center, featherWidth, featherInnerColor, featherOuterColor);
            AddFeather(featherVertices, lastBoundary, farLast, center, featherWidth, featherInnerColor, featherOuterColor);
        }

        return true;
    }

    private static int BuildBoundaryPath(Span<Vector2> corners, int startIdx, int endIdx, bool clockwise, Span<Vector2> output)
    {
        int count = 0;
        int idx = startIdx;
        while (true)
        {
            output[count++] = corners[idx];
            if (idx == endIdx)
                break;
            idx = clockwise ? (idx + 1) % 4 : (idx + 3) % 4;
        }
        return count;
    }

    private static float AvgDistSq(Span<Vector2> points, int count, Vector2 origin)
    {
        if (count == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < count; i++)
            sum += Vector2.DistanceSquared(points[i], origin);
        return sum / count;
    }

    private static void AddPolygon(List<VertexPositionColor> vertices, Span<Vector2> polygon, int count, Color color)
    {
        if (count < 3) return;
        var v0 = new Vector3(polygon[0], 0f);
        for (int i = 1; i < count - 1; i++)
        {
            vertices.Add(new VertexPositionColor(v0, color));
            vertices.Add(new VertexPositionColor(new Vector3(polygon[i], 0f), color));
            vertices.Add(new VertexPositionColor(new Vector3(polygon[i + 1], 0f), color));
        }
    }

    private static void AddFeather(
        List<VertexPositionColor> vertices,
        Vector2 near,
        Vector2 far,
        Vector2 insidePoint,
        float width,
        Color innerColor,
        Color outerColor)
    {
        var direction = SafeNormalize(far - near);
        if (direction == Vector2.Zero)
            return;

        var normalLeft = new Vector2(-direction.Y, direction.X);
        var side = direction.X * (insidePoint.Y - near.Y) - direction.Y * (insidePoint.X - near.X);
        var outward = side >= 0f ? -normalLeft : normalLeft;
        var offset = outward * width;

        var nearOuter = near + offset;
        var farOuter = far + offset;

        vertices.Add(new VertexPositionColor(new Vector3(near, 0f), innerColor));
        vertices.Add(new VertexPositionColor(new Vector3(far, 0f), innerColor));
        vertices.Add(new VertexPositionColor(new Vector3(farOuter, 0f), outerColor));

        vertices.Add(new VertexPositionColor(new Vector3(near, 0f), innerColor));
        vertices.Add(new VertexPositionColor(new Vector3(farOuter, 0f), outerColor));
        vertices.Add(new VertexPositionColor(new Vector3(nearOuter, 0f), outerColor));
    }

    private static Vector2 ComputeCenter(Span<Vector2> polygon, int count)
    {
        if (count == 0) return Vector2.Zero;
        var sum = Vector2.Zero;
        for (int i = 0; i < count; i++)
            sum += polygon[i];
        return sum / count;
    }

    private static Vector2 SafeNormalize(Vector2 value)
    {
        if (value.LengthSquared() <= 0.0001f)
            return Vector2.Zero;
        value.Normalize();
        return value;
    }
}
