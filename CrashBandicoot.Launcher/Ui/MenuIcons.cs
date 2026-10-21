using System.Numerics;

namespace CrashBandicoot.Launcher.Ui;

/// <summary>
/// Animated main-menu icons shared by the Windows (GDI+) and Linux (Avalonia) launchers,
/// the same family as the in-game pause menu icons (RecompOne CrashUi). Only the hot row
/// shows its icon: it fades in with the hover ease <c>t</c> and animates with
/// <c>phase</c> (seconds of hover, weighted by <c>t</c>). Each UI only paints the shapes.
/// </summary>
static class MenuIcons
{
    public enum Kind { None, Play, Gamepad, Gear, Puzzle, Bolt, Close }

    /// <summary>START GAME … EXIT.</summary>
    public static readonly Kind[] MainMenu = [Kind.Play, Kind.Gamepad, Kind.Gear, Kind.Puzzle, Kind.Bolt, Kind.Close];

    public enum ShapeType { Poly, Line, Circle, Ring }

    public readonly record struct Rgba(byte A, byte R, byte G, byte B);

    /// <summary>
    /// Poly: filled polygon. Line: Points[0]→Points[1], <see cref="Width"/> wide, round caps.
    /// Circle: filled, Points[0] centre, <see cref="Radius"/>. Ring: stroked circle.
    /// </summary>
    public readonly record struct Shape(ShapeType Type, Vector2[] Points, float Radius, float Width, Rgba Color);

    static readonly Vector3 Go = new(96, 208, 72);
    static readonly Vector3 Steel = new(184, 192, 202);
    static readonly Vector3 PadGray = new(196, 198, 204);
    static readonly Vector3 PadDark = new(70, 72, 80);
    static readonly Vector3 PieceBlue = new(70, 150, 255);
    static readonly Vector3 BoltYellow = new(255, 214, 40);
    static readonly Vector3 White = new(255, 255, 255);
    static readonly Vector3 Danger = new(255, 72, 56);

    /// <summary>Shadow pass (2 px down, silhouette only) then the icon.</summary>
    public static List<Shape> Build(Kind kind, Vector2 c, float r, float t, float phase)
    {
        var list = new List<Shape>();
        if (kind == Kind.None || t <= 0.01f) return list;
        Emit(list, kind, c + new Vector2(0f, 2f), r, t, phase, shadow: true);
        Emit(list, kind, c, r, t, phase, shadow: false);
        return list;
    }

    static void Emit(List<Shape> list, Kind kind, Vector2 c, float r, float t, float phase, bool shadow)
    {
        Rgba Col(Vector3 rgb, float alpha = 1f) => shadow
            ? new Rgba((byte)(140 * t * alpha), 0, 0, 0)
            : new Rgba((byte)Math.Clamp(255f * t * alpha, 0f, 255f), (byte)rgb.X, (byte)rgb.Y, (byte)rgb.Z);
        Vector2 V(float x, float y) => c + new Vector2(x, y) * r;
        Vector2 Rot(Vector2 p, float a) =>
            c + new Vector2(
                (p.X - c.X) * MathF.Cos(a) - (p.Y - c.Y) * MathF.Sin(a),
                (p.X - c.X) * MathF.Sin(a) + (p.Y - c.Y) * MathF.Cos(a));
        void Poly(Rgba col, params Vector2[] pts) => list.Add(new Shape(ShapeType.Poly, pts, 0f, 0f, col));
        void Line(Vector2 a, Vector2 b, float w, Rgba col) => list.Add(new Shape(ShapeType.Line, [a, b], 0f, w, col));
        void Dot(Vector2 p, float rad, Rgba col) => list.Add(new Shape(ShapeType.Circle, [p], rad, 0f, col));

        switch (kind)
        {
            case Kind.Play:
            {
                // Pulsing play triangle with a ghost sliding out behind it.
                float s = 1f + 0.08f * MathF.Sin(phase * 7f) * t;
                float ox = 0.08f;
                if (!shadow)
                {
                    float gx = ox - 0.45f * t;
                    Poly(Col(Go, 0.3f), V(gx - 0.6f * s, -0.85f * s), V(gx + 0.85f * s, 0f), V(gx - 0.6f * s, 0.85f * s));
                }
                Poly(Col(Go), V(ox - 0.6f * s, -0.85f * s), V(ox + 0.85f * s, 0f), V(ox - 0.6f * s, 0.85f * s));
                break;
            }
            case Kind.Gamepad:
            {
                // PS1 pad that rocks; the face buttons light up in turn.
                float a = 0.18f * MathF.Sin(phase * 6f) * t;
                Rgba body = Col(PadGray);
                Line(Rot(V(-0.55f, -0.08f), a), Rot(V(0.55f, -0.08f), a), 0.62f * r, body);
                Line(Rot(V(-0.6f, 0f), a), Rot(V(-0.74f, 0.5f), a), 0.42f * r, body);
                Line(Rot(V(0.6f, 0f), a), Rot(V(0.74f, 0.5f), a), 0.42f * r, body);
                if (shadow) break;

                Rgba dark = Col(PadDark);
                float dw = Math.Max(1.5f, 0.13f * r);
                Line(Rot(V(-0.72f, -0.08f), a), Rot(V(-0.36f, -0.08f), a), dw, dark);
                Line(Rot(V(-0.54f, -0.26f), a), Rot(V(-0.54f, 0.1f), a), dw, dark);
                Vector3[] face = [new(70, 200, 150), new(255, 80, 80), new(90, 150, 255), new(255, 130, 200)];
                Vector2[] at = [new(0f, -0.18f), new(0.18f, 0f), new(0f, 0.18f), new(-0.18f, 0f)];
                int lit = (int)(phase * 6f) % 4;
                for (int i = 0; i < 4; i++)
                {
                    var p = Rot(V(0.55f + at[i].X, -0.08f + at[i].Y), a);
                    float rad = Math.Max(1f, 0.08f * r) * (i == lit && t > 0.5f ? 1.5f : 1f);
                    Dot(p, rad, Col(face[i]));
                }
                break;
            }
            case Kind.Gear:
            {
                // Steel gear that spins while hot.
                float a = phase * 2.6f;
                Rgba col = Col(Steel);
                const int teeth = 8;
                for (int i = 0; i < teeth; i++)
                {
                    float ang = a + i * MathF.Tau / teeth;
                    var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                    var side = new Vector2(-dir.Y, dir.X);
                    Poly(col, c + dir * 0.6f - side * 0.2f, c + dir - side * 0.13f,
                        c + dir + side * 0.13f, c + dir * 0.6f + side * 0.2f);
                }
                list.Add(new Shape(ShapeType.Ring, [c], 0.5f * r, 0.4f * r, col)); // body 0.3r..0.7r
                break;
            }
            case Kind.Puzzle:
            {
                // Puzzle piece (mods) that wiggles into place.
                float a = 0.22f * MathF.Sin(phase * 8f) * t;
                Rgba col = Col(PieceBlue);
                Poly(col, Rot(V(-0.75f, -0.45f), a), Rot(V(0.45f, -0.45f), a),
                    Rot(V(0.45f, 0.75f), a), Rot(V(-0.75f, 0.75f), a));
                Dot(Rot(V(-0.15f, -0.62f), a), 0.24f * r, col);
                Dot(Rot(V(0.62f, 0.15f), a), 0.24f * r, col);
                break;
            }
            case Kind.Bolt:
            {
                // Lightning bolt (cheats) that flickers white.
                float s = 1f + 0.1f * MathF.Sin(phase * 10f) * t;
                float flash = Math.Max(0f, MathF.Sin(phase * 13f)) * 0.55f * t;
                Rgba col = Col(Vector3.Lerp(BoltYellow, White, flash));
                Poly(col, V(0.18f * s, -1f * s), V(-0.55f * s, 0.12f * s), V(-0.04f * s, 0.12f * s),
                    V(-0.2f * s, 1f * s), V(0.55f * s, -0.16f * s), V(0.05f * s, -0.16f * s));
                break;
            }
            case Kind.Close:
            {
                // Red X: quarter turn as the hover eases in, then a light pulse.
                float a = t * MathF.PI * 0.5f;
                float s = 1f + 0.1f * MathF.Sin(phase * 9f) * t;
                Rgba col = Col(Danger);
                for (int i = 0; i < 2; i++)
                {
                    float ang = a + MathF.PI * 0.25f + i * MathF.PI * 0.5f;
                    var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (0.75f * r * s);
                    Line(c - d, c + d, Math.Max(2f, 0.3f * r), col);
                }
                break;
            }
        }
    }

    /// <summary>Hover ease toward <paramref name="hot"/> (~0.12 s) and its weighted phase.</summary>
    public static void Step(ref float t, ref float phase, bool hot, float dt)
    {
        dt = Math.Min(dt, 0.1f);
        t = hot ? Math.Min(1f, t + dt * 8f) : Math.Max(0f, t - dt * 8f);
        phase = (phase + dt * t) % 3600f;
    }
}
