using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.Rendering;

/// <summary>
/// Projects shadow geometry from occluder edges.
/// Each edge casts a trapezoidal shadow away from the light/viewer origin.
/// Optionally adds feather (penumbra) quads on both sides.
/// </summary>
public static class TileShadowGeometry
{
    /// <summary>
    /// Appends shadow geometry for a single occluder edge.
    /// The edge A→B should be wound so its outward normal faces the origin.
    /// </summary>
    public static void AppendEdgeShadow(
        List<VertexPositionColor> mainVertices,
        List<VertexPositionColor>? featherVertices,
        Vector2 origin,
        Vector2 edgeA,
        Vector2 edgeB,
        float shadowLength,
        float featherWidth,
        Color mainColor,
        Color featherInnerColor,
        Color featherOuterColor)
    {
        var dirA = edgeA - origin;
        var dirB = edgeB - origin;

        if (dirA.LengthSquared() < 0.001f || dirB.LengthSquared() < 0.001f)
            return;

        dirA = Normalize(dirA);
        dirB = Normalize(dirB);

        var farA = edgeA + dirA * shadowLength;
        var farB = edgeB + dirB * shadowLength;

        // Main shadow quad: edgeA → edgeB → farB → farA
        AddQuad(mainVertices, edgeA, edgeB, farB, farA, mainColor);

        if (featherVertices == null || featherWidth <= 0f)
            return;

        // Feather on the A side (left edge of shadow)
        var perpA = new Vector2(-dirA.Y, dirA.X);
        // Choose outward direction: away from the shadow interior
        var shadowMid = (edgeA + edgeB) * 0.5f;
        if (Vector2.Dot(perpA, edgeA - shadowMid) < 0f)
            perpA = -perpA;

        var featherNearA = edgeA + perpA * featherWidth;
        var featherFarA = farA + perpA * featherWidth;

        AddFeatherQuad(featherVertices, edgeA, farA, featherNearA, featherFarA,
            featherInnerColor, featherOuterColor);

        // Feather on the B side
        var perpB = new Vector2(-dirB.Y, dirB.X);
        if (Vector2.Dot(perpB, edgeB - shadowMid) < 0f)
            perpB = -perpB;

        var featherNearB = edgeB + perpB * featherWidth;
        var featherFarB = farB + perpB * featherWidth;

        AddFeatherQuad(featherVertices, edgeB, farB, featherNearB, featherFarB,
            featherInnerColor, featherOuterColor);
    }

    private static void AddQuad(
        List<VertexPositionColor> vertices,
        Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color color)
    {
        var va = new VertexPositionColor(new Vector3(a, 0f), color);
        var vb = new VertexPositionColor(new Vector3(b, 0f), color);
        var vc = new VertexPositionColor(new Vector3(c, 0f), color);
        var vd = new VertexPositionColor(new Vector3(d, 0f), color);

        vertices.Add(va);
        vertices.Add(vb);
        vertices.Add(vc);

        vertices.Add(va);
        vertices.Add(vc);
        vertices.Add(vd);
    }

    private static void AddFeatherQuad(
        List<VertexPositionColor> vertices,
        Vector2 innerNear, Vector2 innerFar,
        Vector2 outerNear, Vector2 outerFar,
        Color innerColor, Color outerColor)
    {
        // Triangle 1
        vertices.Add(new VertexPositionColor(new Vector3(innerNear, 0f), innerColor));
        vertices.Add(new VertexPositionColor(new Vector3(innerFar, 0f), innerColor));
        vertices.Add(new VertexPositionColor(new Vector3(outerFar, 0f), outerColor));

        // Triangle 2
        vertices.Add(new VertexPositionColor(new Vector3(innerNear, 0f), innerColor));
        vertices.Add(new VertexPositionColor(new Vector3(outerFar, 0f), outerColor));
        vertices.Add(new VertexPositionColor(new Vector3(outerNear, 0f), outerColor));
    }

    private static Vector2 Normalize(Vector2 v)
    {
        var len = v.Length();
        return len > 0.0001f ? v / len : Vector2.Zero;
    }
}
