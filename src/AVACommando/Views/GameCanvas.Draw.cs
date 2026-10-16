using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVACommando.Engine;
using Effect = AVACommando.Engine.Effect;

namespace AVACommando.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVACommando/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);



    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private const uint Skin = 0xFFE0B080;

    /// <summary>The colours of each kind of country: the ground, its flecks, the rocks, and the trees.</summary>
    private static readonly (uint Ground, uint Fleck, uint Rock, uint Tree)[] Themes =
    [
        (0xFFC8B078, 0xFFB09860, 0xFF8A8070, 0xFF4A8A3A),
        (0xFF4A6A32, 0xFF3A5A28, 0xFF6A7A5A, 0xFF2A5A20),
        (0xFF8A7050, 0xFF7A6040, 0xFF7A7A7A, 0xFF3A6A2A),
    ];

    private double Sy(double wy) => Field.ScreenY(wy, Engine.Camera);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101010), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawGround(dc, g.Area, g.Camera);
            foreach (var c in g.Crates) DrawCrate(dc, c.X, Sy(c.Y));
            foreach (var b in g.Pillboxes) DrawPillbox(dc, b);
            foreach (var gr in g.Grenades) dc.DrawEllipse(Brush(0x50000000), null, new Point(gr.X, Sy(gr.Y)), 5, 3);
            foreach (var pr in g.Prisoners) DrawPrisoner(dc, pr.X, Sy(pr.Y), pr.Walk, pr.Free, 1.3);
            foreach (var s in g.Soldiers) DrawSoldier(dc, s.Kind, s.X, Sy(s.Y), s.Facing, s.Walk, s.Aiming, 1.3);
            foreach (var b in g.Bikes) DrawBike(dc, b.X, Sy(b.Y), b.Vx > 0);
            DrawPlayer(dc);
            DrawTrees(dc, g.Area, g.Camera);
            foreach (var b in g.Bullets)
            {
                var at = new Point(b.X, Sy(b.Y));
                dc.DrawEllipse(Brush(b.Hostile ? 0x60FF6A3Au : 0x60FFF0A0u), null, at, 5, 5);
                dc.DrawEllipse(Brush(b.Hostile ? 0xFFFF8A4A : 0xFFFFFFC0), null, at, 2.4, 2.4);
            }
            foreach (var gr in g.Grenades)
            {
                var at = new Point(gr.X, Sy(gr.Y) - gr.Height);
                dc.DrawEllipse(Brush(gr.Hostile ? 0xFF4A4A3A : 0xFF3A5A2A), Pen(0xFF101010, 1), at, 4.5, 4.5);
            }
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                dc.DrawEllipse(Brush(Alpha(d.Colour, a)), null, new Point(d.X, Sy(d.Y)), 2, 2);
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 16), 26, Yellow, center: true);
    }

    // ───────────────────────────── the ground ─────────────────────────────

    /// <summary>The cells on the screen: the ground with its flecks, and whatever stands on it (but the trees, drawn over the soldiers).</summary>
    private void DrawGround(DrawingContext dc, Area area, double camera)
    {
        var (ground, fleck, rock, _) = Themes[area.Theme];
        dc.FillRectangle(Brush(ground), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        int r0 = Field.RowOf(camera), r1 = Field.RowOf(camera + Field.Height) + 1;
        for (int r = r0; r <= r1; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            double x = c * Field.Cell, y = Field.ScreenY((r + 1) * Field.Cell, camera);
            var cell = new Rect(x, y, Field.Cell, Field.Cell);
            uint h = Hash(c, r, area.Theme);
            char at = area.At(c, r);
            switch (at)
            {
                case Ground.Water:
                case Ground.Bridge:
                {
                    dc.FillRectangle(Brush(0xFF2A5A9A), cell);
                    double wave = (_clock * 30 + h % 32) % 32;
                    dc.DrawLine(Pen(0x70A0D0FF, 1.5), new Point(x + 4, y + wave * 0.9), new Point(x + 14, y + wave * 0.9));
                    if (at == Ground.Bridge)
                    {
                        dc.FillRectangle(Brush(0xFF8A5A30), cell);
                        for (int k = 0; k < 4; k++) dc.DrawLine(Pen(0xFF5A3A1A, 1.5), new Point(x, y + k * 8 + 4), new Point(x + Field.Cell, y + k * 8 + 4));
                    }
                    // A bank where the water meets the ground.
                    if (area.At(c, r + 1) is not (Ground.Water or Ground.Bridge)) dc.FillRectangle(Brush(0xFF6A5A3A), new Rect(x, y, Field.Cell, 4));
                    if (area.At(c, r - 1) is not (Ground.Water or Ground.Bridge)) dc.FillRectangle(Brush(0xFF6A5A3A), new Rect(x, y + Field.Cell - 4, Field.Cell, 4));
                    break;
                }
                case Ground.Rock:
                    dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 18, y + 20), 15, 12);
                    Poly(dc, rock, 0xFF3A3A3A, 1.2, new Point(x + 4, y + 18), new Point(x + 9, y + 5), new Point(x + 22, y + 3), new Point(x + 29, y + 14), new Point(x + 26, y + 28), new Point(x + 10, y + 29));
                    dc.DrawLine(Pen(0x50FFFFFF, 2), new Point(x + 10, y + 8), new Point(x + 20, y + 6));
                    break;
                case Ground.Sandbag:
                    for (int k = 0; k < 2; k++)
                    for (int j = 0; j < 2; j++)
                        dc.DrawRectangle(Brush(0xFFC8B880), Pen(0xFF7A6A40, 1), new Rect(x + j * 16 + (k == 1 ? 8 : 0) - (k == 1 && j == 1 ? 8 : 0), y + 6 + k * 10, 16, 10), 4, 4);
                    break;
                case Ground.Wall:
                    dc.FillRectangle(Brush(0xFF8A8A8A), cell);
                    for (int k = 0; k < 4; k++)
                    {
                        dc.DrawLine(Pen(0xFF5A5A5A, 1), new Point(x, y + k * 8), new Point(x + Field.Cell, y + k * 8));
                        double o = k % 2 == 0 ? 0 : 8;
                        dc.DrawLine(Pen(0xFF5A5A5A, 1), new Point(x + o + 8, y + k * 8), new Point(x + o + 8, y + k * 8 + 8));
                        dc.DrawLine(Pen(0xFF5A5A5A, 1), new Point(x + o + 24, y + k * 8), new Point(x + o + 24, y + k * 8 + 8));
                    }
                    break;
                case Ground.Fort:
                    dc.FillRectangle(Brush(0xFF5A5450), cell);
                    if (h % 5 == 0) dc.DrawRectangle(null, Pen(0xFF3A3430, 1), new Rect(x + 4, y + 4, 24, 24));
                    break;
                case Ground.Gate:
                    dc.FillRectangle(Brush(0xFF2A2018), cell);
                    break;
                default:
                    for (int k = 0; k < 3; k++)
                    {
                        uint f = Hash(c, r, k + 11);
                        dc.FillRectangle(Brush(fleck), new Rect(x + f % 28, y + (f >> 8) % 28, 3, 2));
                    }
                    break;
            }
        }
        // The fort's battlements along the top of its wall, and its gate's doors swung open.
        double gy = Field.ScreenY(area.GateY + 2 * Field.Cell, camera);
        if (gy > -40 && gy < Field.Height)
        {
            for (double x = 0; x < Field.Width; x += 32)
                if (x < Area.GateCol * Field.Cell || x >= (Area.GateCol + Area.GateWidth) * Field.Cell)
                    dc.FillRectangle(Brush(0xFF6A6A6A), new Rect(x + 4, gy - 10, 16, 10));
            double gx0 = Area.GateCol * Field.Cell, gx1 = (Area.GateCol + Area.GateWidth) * Field.Cell;
            Poly(dc, 0xFF7A4A20, 0xFF3A2010, 1.5, new Point(gx0, gy), new Point(gx0 - 26, gy + 40), new Point(gx0 - 26, gy + 64), new Point(gx0, gy + 64));
            Poly(dc, 0xFF7A4A20, 0xFF3A2010, 1.5, new Point(gx1, gy), new Point(gx1 + 26, gy + 40), new Point(gx1 + 26, gy + 64), new Point(gx1, gy + 64));
        }
    }

    /// <summary>The trees: canopies over everything on the ground, so a soldier under one is half hidden.</summary>
    private void DrawTrees(DrawingContext dc, Area area, double camera)
    {
        var tree = Themes[area.Theme].Tree;
        int r0 = Field.RowOf(camera), r1 = Field.RowOf(camera + Field.Height) + 1;
        for (int r = r0; r <= r1; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            if (area.At(c, r) != Ground.Tree) continue;
            double x = c * Field.Cell + 16, y = Field.ScreenY((r + 1) * Field.Cell, camera) + 16;
            dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 6, y + 8), 20, 16);
            if (area.Theme == 0)
            {
                // A palm: fronds out from the top.
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.PI / 3 + 0.3;
                    dc.DrawLine(Pen(Alpha(tree, 0.9), 6), new Point(x, y), new Point(x + Math.Cos(a) * 20, y + Math.Sin(a) * 20));
                }
                dc.DrawEllipse(Brush(0xFF7A5A2A), null, new Point(x, y), 4, 4);
            }
            else
            {
                dc.DrawEllipse(Brush(Alpha(tree, 0.92)), null, new Point(x, y), 21, 19);
                dc.DrawEllipse(Brush(Alpha(0xFF8AC060, 0.35)), null, new Point(x - 6, y - 6), 9, 8);
            }
        }
    }

    private void DrawPillbox(DrawingContext dc, Pillbox b)
    {
        double x = b.X, y = Sy(b.Y);
        if (b.Dead)
        {
            for (int k = 0; k < 7; k++)
            {
                uint h = Hash(b.Col, b.Row, k);
                dc.DrawEllipse(Brush(0xFF7A7A7A), Pen(0xFF4A4A4A, 1), new Point(x - 24 + h % 48, y - 20 + (h >> 8) % 40), 6, 5);
            }
            return;
        }
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x + 5, y + 6), 33, 31);
        Poly(dc, 0xFF9A9A92, 0xFF4A4A44, 2, new Point(x - 14, y - 30), new Point(x + 14, y - 30), new Point(x + 30, y - 14), new Point(x + 30, y + 14),
            new Point(x + 14, y + 30), new Point(x - 14, y + 30), new Point(x - 30, y + 14), new Point(x - 30, y - 14));
        dc.DrawEllipse(Brush(0xFFB0B0A8), Pen(0xFF5A5A54, 1.5), new Point(x, y), 16, 16);
        // The firing slit, facing down the map.
        dc.FillRectangle(Brush(0xFF101010), new Rect(x - 16, y + 18, 32, 6));
    }

    private static void DrawCrate(DrawingContext dc, double x, double y)
    {
        dc.DrawRectangle(Brush(0xFF9A6A30), Pen(0xFF4A3010, 1.5), new Rect(x - 12, y - 10, 24, 20));
        dc.DrawLine(Pen(0xFF4A3010, 1), new Point(x - 12, y - 10), new Point(x + 12, y + 10));
        Text(dc, "G", new Point(x, y), 20, 0xFFFFE14A, center: true, shadow: false);
    }

    // ───────────────────────────── the soldiers ─────────────────────────────

    private void DrawPlayer(DrawingContext dc)
    {
        var p = Engine.Player;
        if (!p.Alive) return;
        if (p.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        DrawPerson(dc, p.X, Sy(p.Y), -Field.AngleOf(p.Facing), p.Walk, Look.Commando, 1.3);
    }

    /// <summary>Who a figure is, for how he's dressed and what he carries.</summary>
    private enum Look { Commando, Soldier, Grenadier, Officer, Prisoner }

    /// <summary>
    /// A man as an arcade sprite draws him, seen from a little above: boots and legs striding as he runs, a body
    /// with a belt, arms holding his rifle out the way he faces, and his head (a face when he's turned our way, the
    /// back of his head when he's turned away). <paramref name="facing"/> is an angle on the screen, so up the
    /// screen is -π/2; (x, y) is the middle of him, with his feet a little below.
    /// </summary>
    private static void DrawPerson(DrawingContext dc, double x, double y, double facing, double walk, Look look, double k, bool seated = false, bool aiming = false)
    {
        double fx = Math.Cos(facing), fy = Math.Sin(facing);
        bool away = fy < -0.35, side = Math.Abs(fx) > 0.5;
        var (shirt, trousers, arms) = look switch
        {
            Look.Commando => (0xFF6A7A3Au, 0xFF4A5A2Au, Skin),
            Look.Grenadier => (0xFF7A6A4Au, 0xFF5A4A30u, 0xFF7A6A4Au),
            Look.Officer => (0xFFE0DED0u, 0xFF3A3A40u, 0xFFE0DED0u),
            Look.Prisoner => (0xFFE8E8E0u, 0xFF8A8A80u, Skin),
            _ => (0xFF5A6A8Au, 0xFF3A4A62u, 0xFF5A6A8Au),
        };
        var dark = Pen(0xC0101010, 1);
        if (!seated) dc.DrawEllipse(Brush(0x45000000), null, new Point(x + 1, y + 12 * k), 8.5 * k, 3.4 * k);
        // The legs, stepping: forward and back when he's side on, up and down when he's facing us or away.
        if (!seated)
        {
            double phase = Math.Sin(walk * 13);
            foreach (int leg in new[] { -1, 1 })
            {
                double hx = x + leg * 2.6 * k, hy = y + 3 * k, footX, footY;
                if (side)
                {
                    footX = hx + Math.Sign(fx) * leg * phase * 4 * k;
                    footY = y + 11 * k - (leg * phase > 0 ? 1.2 * k : 0);
                }
                else
                {
                    footX = hx;
                    footY = y + 11 * k - Math.Max(0, leg * phase) * 2.6 * k;
                }
                dc.DrawLine(Pen(trousers, 3.4 * k), new Point(hx, hy), new Point(footX, footY));
                dc.DrawEllipse(Brush(0xFF2A2018), null, new Point(footX + (side ? fx * 0.8 * k : 0), footY + 0.6 * k), 2.3 * k, 1.5 * k);
            }
        }
        // Where his hands hold the gun, and the gun itself: behind him when he's facing away.
        double gx = x + fx * 5 * k, gy = y - 1 * k + fy * 3.5 * k;
        bool armed = look is Look.Commando or Look.Soldier;
        void Rifle()
        {
            if (!armed) return;
            var back = new Point(gx - fx * 5 * k, gy - fy * 5 * k + 1.5 * k);
            var muzzle = new Point(gx + fx * (aiming ? 15 : 12) * k, gy + fy * (aiming ? 15 : 12) * k);
            dc.DrawLine(Pen(0xFF5A3A1A, 3 * k), back, new Point(gx, gy));
            dc.DrawLine(Pen(0xFF202020, 2.2 * k), new Point(gx, gy), muzzle);
        }
        if (away) Rifle();
        // The body: a rounded tunic, a belt, and for the uniforms a strap across.
        var body = new Rect(x - 5 * k, y - 6.5 * k, 10 * k, 10.5 * k);
        dc.DrawRectangle(Brush(shirt), dark, body, 3 * k, 3 * k);
        if (look == Look.Prisoner)
            for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFF6A6A70, 1.4 * k), new Point(x - 4.6 * k, y - 4 * k + i * 3 * k), new Point(x + 4.6 * k, y - 4 * k + i * 3 * k));
        else
        {
            dc.FillRectangle(Brush(0xFF2A2418), new Rect(x - 5 * k, y + 1.4 * k, 10 * k, 1.8 * k));
            if (look == Look.Officer) dc.DrawLine(Pen(0xFFC02A2A, 1.8 * k), new Point(x - 4.5 * k, y - 6 * k), new Point(x + 4.5 * k, y + 1.4 * k));
            else if (!away) dc.DrawLine(Pen(Alpha(0xFF202020, 0.45), 1.2 * k), new Point(x - 4 * k, y - 5.5 * k), new Point(x + 3.5 * k, y + 1.4 * k));
        }
        // The arms: out to the gun (or, unarmed, at his sides; a grenadier's raised with one ready to throw).
        var left = new Point(x - 4.6 * k, y - 4.5 * k);
        var right = new Point(x + 4.6 * k, y - 4.5 * k);
        if (armed)
        {
            dc.DrawLine(Pen(arms, 2.6 * k), left, new Point(gx - fx * 2 * k, gy - fy * 2 * k + 0.5 * k));
            dc.DrawLine(Pen(arms, 2.6 * k), right, new Point(gx, gy));
            dc.DrawEllipse(Brush(Skin), null, new Point(gx, gy), 1.4 * k, 1.4 * k);
        }
        else if (look == Look.Prisoner)
        {
            // Hands tied in front of him.
            var tied = new Point(x, y);
            dc.DrawLine(Pen(arms, 2.4 * k), left, tied);
            dc.DrawLine(Pen(arms, 2.4 * k), right, tied);
            dc.DrawLine(Pen(0xFF8A6A3A, 1.2 * k), new Point(x - 1.8 * k, y), new Point(x + 1.8 * k, y));
        }
        else if (look == Look.Grenadier && aiming)
        {
            dc.DrawLine(Pen(arms, 2.6 * k), left, new Point(x - 5 * k, y + 1 * k));
            var up = new Point(x + 6 * k, y - 12 * k);
            dc.DrawLine(Pen(arms, 2.6 * k), right, up);
            dc.DrawEllipse(Brush(0xFF3A4A2A), dark, up, 2.2 * k, 2.2 * k);
        }
        else
        {
            double swing = Math.Sin(walk * 13) * 1.5 * k;
            dc.DrawLine(Pen(arms, 2.6 * k), left, new Point(x - 5.6 * k, y + 1 * k + swing));
            dc.DrawLine(Pen(arms, 2.6 * k), right, new Point(x + 5.6 * k, y + 1 * k - swing));
        }
        if (!away) Rifle();
        // The head.
        var head = new Point(x + fx * 0.6 * k, y - 10 * k);
        dc.DrawEllipse(Brush(Skin), dark, head, 4.2 * k, 4.2 * k);
        switch (look)
        {
            case Look.Commando:
            {
                // Dark hair, and a red headband with its tails streaming out behind.
                dc.DrawEllipse(Brush(0xFF2A1A10), null, new Point(head.X, head.Y - (away ? 0.8 : 2) * k), 4.2 * k, (away ? 3.6 : 2.4) * k);
                dc.DrawLine(Pen(0xFFD02A2A, 1.6 * k), new Point(head.X - 4.1 * k, head.Y - 1.2 * k), new Point(head.X + 4.1 * k, head.Y - 1.2 * k));
                double wave = Math.Sin(walk * 9) * 1.5 * k, tx = side ? -Math.Sign(fx) : 0.6, ty = side ? 0 : 1;
                var knot = new Point(head.X + tx * 3.8 * k, head.Y - 1.2 * k);
                dc.DrawLine(Pen(0xFFD02A2A, 1.3 * k), knot, new Point(knot.X + tx * 6 * k, knot.Y + ty * 4 * k + wave));
                dc.DrawLine(Pen(0xFFD02A2A, 1.3 * k), knot, new Point(knot.X + tx * 5 * k, knot.Y + ty * 5 * k + 2 * k - wave));
                break;
            }
            case Look.Officer:
                dc.DrawEllipse(Brush(0xFF3A3A40), null, new Point(head.X, head.Y - 3 * k), 4.8 * k, 2.4 * k);
                dc.FillRectangle(Brush(0xFFC02A2A), new Rect(head.X - 4.2 * k, head.Y - 2.6 * k, 8.4 * k, 1.2 * k));
                if (!away) dc.FillRectangle(Brush(0xFF101010), new Rect(head.X - 4.6 * k + fx * 1.5 * k, head.Y - 1.6 * k, 9.2 * k, 1.1 * k));
                break;
            case Look.Prisoner:
                dc.DrawEllipse(Brush(Alpha(0xFF5A4A3A, 0.5)), null, new Point(head.X, head.Y - 2 * k), 3.8 * k, 2 * k);
                break;
            default:
            {
                // A helmet with its rim.
                uint helmet = look == Look.Grenadier ? 0xFF4A4030 : 0xFF3A4A5A;
                dc.DrawEllipse(Brush(helmet), dark, new Point(head.X, head.Y - 1.8 * k), 5.2 * k, (away ? 4.4 : 3.6) * k);
                dc.DrawLine(Pen(Alpha(0xFF000000, 0.5), 1 * k), new Point(head.X - 5.2 * k, head.Y - 0.6 * k), new Point(head.X + 5.2 * k, head.Y - 0.6 * k));
                break;
            }
        }
        if (!away)
        {
            // The eyes: two looking at us, or one in profile.
            if (side) dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + fx * 2.6 * k, head.Y + 0.6 * k), 0.8 * k, 0.8 * k);
            else
                foreach (int e in new[] { -1, 1 })
                    dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + e * 1.6 * k + fx * k, head.Y + 0.6 * k), 0.75 * k, 0.75 * k);
        }
    }

    private static void DrawSoldier(DrawingContext dc, SoldierKind kind, double x, double y, double facing, double walk, bool aiming, double k)
    {
        // The engine's angles have up the map positive; on the screen it's the other way.
        double f = -facing;
        var look = kind switch { SoldierKind.Grenadier => Look.Grenadier, SoldierKind.Officer => Look.Officer, _ => Look.Soldier };
        DrawPerson(dc, x, y, f, walk, look, look == Look.Officer ? k * 1.1 : k, aiming: aiming);
        // A soldier about to fire: a glint at the muzzle, as warning.
        if (aiming && look == Look.Soldier)
            dc.DrawEllipse(Brush(0xC0FFF0A0), null, new Point(x + Math.Cos(f) * 17 * k, y - 1 * k + Math.Sin(f) * 18 * k), 2 * k, 2 * k);
    }

    private static void DrawPrisoner(DrawingContext dc, double x, double y, double walk, bool free, double k) =>
        DrawPerson(dc, x, y, free ? Math.PI / 2 : 0, walk, Look.Prisoner, k);

    /// <summary>A motorbike and sidecar from the side, the rider and his passenger sat in it, both facing the way it's going.</summary>
    private static void DrawBike(DrawingContext dc, double x, double y, bool right)
    {
        double d = right ? 1 : -1, face = right ? 0 : Math.PI;
        dc.DrawEllipse(Brush(0x45000000), null, new Point(x + 2, y + 14), 28, 7);
        // The sidecar, behind the bike (further from us), then the bike.
        dc.DrawRectangle(Brush(0xFF4A5A3A), Pen(0xFF202020, 1), new Rect(x - 16, y - 8, 30, 12), 6, 6);
        DrawPerson(dc, x - d * 1, y - 10, face, 0, Look.Soldier, 0.85, seated: true);
        foreach (double wx in new[] { -15.0, 15.0 })
        {
            dc.DrawEllipse(Brush(0xFF1A1A1A), Pen(0xFF5A5A5A, 1), new Point(x + wx, y + 9), 6.5, 6.5);
            dc.DrawEllipse(Brush(0xFF7A7A7A), null, new Point(x + wx, y + 9), 1.8, 1.8);
        }
        Poly(dc, 0xFF5A6A4A, 0xFF202020, 1, new Point(x - 14, y + 4), new Point(x + 12, y + 4), new Point(x + 16 * d, y - 2), new Point(x - 10, y - 2));
        dc.DrawLine(Pen(0xFF303030, 2), new Point(x + 12 * d, y - 2), new Point(x + 16 * d, y - 9));
        DrawPerson(dc, x - d * 4, y - 6, face, 0, Look.Soldier, 0.95, seated: true);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, Sy(e.Y));
        if (e.Kind != EffectKind.Score && e.Kind != EffectKind.Word && !InGame) return;
        switch (e.Kind)
        {
            case EffectKind.Blast:
            {
                double r = (14 + 40 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF3A3A3A, 0.5 * (1 - t))), null, new Point(c.X, c.Y - t * 20), r * 1.1, r);
                dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 1 - t)), null, c, r * 0.8, r * 0.7);
                dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, r * 0.4, r * 0.35);
                break;
            }
            case EffectKind.Puff:
                dc.DrawEllipse(Brush(Alpha(0xFFE0D0B0, 0.7 * (1 - t))), null, c, 3 + 6 * t, 3 + 6 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xE0101410), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF5A6A3A), new Rect(0, Field.Hud - 2, Field.Width, 2));
        Text(dc, "SCORE", new Point(16, 14), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(90, 14), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 14), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 14), 28, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"AREA {g.Wave}  {g.Area.Name}", new Point(512, 14), 24, 0xFFB0D080, center: true);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawPerson(dc, 24 + i * 22, 38, Math.PI / 2, 0, Look.Commando, 0.6);
        // The grenades left.
        dc.DrawEllipse(Brush(0xFF3A5A2A), Pen(0xFF101010, 1), new Point(Field.Width - 90, 40), 6, 6);
        Text(dc, $"x {g.Player.Grenades}", new Point(Field.Width - 76, 40), 24, White);
        if (g.Rush) Text(dc, g.RushLeft > 0 ? $"AT THE GATE - {g.RushLeft} TO COME" : "AT THE GATE", new Point(512, 40), 20, Red, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 260;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0181C10), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(0xFF8A9A4A, 1.5), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF2A3A1A), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        for (int i = 0; i < 400; i++)
        {
            uint h = Hash(i, 2, 9);
            dc.FillRectangle(Brush(0xFF34481F), new Rect(h % 1024, Field.Hud + (h >> 10) % 712, 4, 3));
        }
        string title = "AVA COMMANDO";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 26 + 26 + i * 52, 116 + bob), 104, i % 2 == 0 ? 0xFFE0C04A : 0xFFFFE890, center: true);
        }
        Text(dc, "AFTER THE 1985 ARCADE CLASSIC", new Point(cx, 180), 28, 0xFFB0C090, center: true);
        // The commando, running and firing up the screen.
        double x = cx + Math.Sin(_clock * 0.9) * 140;
        DrawPerson(dc, x, 258, -Math.PI / 2, _clock, Look.Commando, 2.6);
        double shot = (_clock * 3) % 1;
        dc.DrawEllipse(Brush(0xFFFFFFC0), null, new Point(x + 13, 216 - shot * 30), 4, 4);
        Text(dc, "FIGHT YOUR WAY UP THE MAP TO THE ENEMY HQ - GRENADES FLY OVER COVER", new Point(cx, 326), 24, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("RIFLEMAN", "100", x => DrawSoldier(dc, SoldierKind.Rifleman, x, 400, -Math.PI / 2, _clock, false, 2.2)),
            ("GRENADIER", "150", x => DrawSoldier(dc, SoldierKind.Grenadier, x, 400, -Math.PI / 2, _clock, false, 2.2)),
            ("SIDECAR", "300", x => DrawBike(dc, x, 400, true)),
            ("PILLBOX", "500", x => { using (dc.PushTransform(Matrix.CreateTranslation(-x, -400) * Matrix.CreateScale(0.8, 0.8) * Matrix.CreateTranslation(x, 400))) DrawPillboxAt(dc, x, 400); }),
            ("PRISONER", "1000", x => DrawPrisoner(dc, x, 400, _clock, false, 2.2)),
            ("OFFICER", "2000", x => DrawSoldier(dc, SoldierKind.Officer, x, 400, -Math.PI / 2, _clock, false, 2.2)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double lx = cx - 375 + i * 150;
            legend[i].Draw(lx);
            Text(dc, legend[i].Name, new Point(lx, 452), 20, 0xFFB0C090, center: true);
            Text(dc, legend[i].Points, new Point(lx, 476), 22, Yellow, center: true);
        }
        Text(dc, "PILLBOXES TAKE A GRENADE - SHOOT BOTH GUARDS TO FREE A PRISONER", new Point(cx, 512), 21, 0xFFB0C090, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 580), 46, Yellow, center: true);
        Text(dc, "ARROWS: RUN (8 WAYS)    SPACE: FIRE THE WAY YOU FACE    X OR SHIFT: GRENADE", new Point(cx, 648), 22, 0xFFB0C090, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, A, B", new Point(cx, 678), 22, 0xFFB0C090, center: true);
    }

    private static void DrawPillboxAt(DrawingContext dc, double x, double y)
    {
        Poly(dc, 0xFF9A9A92, 0xFF4A4A44, 2, new Point(x - 14, y - 30), new Point(x + 14, y - 30), new Point(x + 30, y - 14), new Point(x + 30, y + 14),
            new Point(x + 14, y + 30), new Point(x - 14, y + 30), new Point(x - 30, y + 14), new Point(x - 30, y - 14));
        dc.DrawEllipse(Brush(0xFFB0B0A8), Pen(0xFF5A5A54, 1.5), new Point(x, y), 16, 16);
        dc.FillRectangle(Brush(0xFF101010), new Rect(x - 16, y + 18, 32, 6));
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1A2410), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE BRAVEST", new Point(cx, 140), 68, 0xFFE0C04A, center: true);
        Text(dc, "RANK NAME    SCORE    AREA", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1A2410), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
