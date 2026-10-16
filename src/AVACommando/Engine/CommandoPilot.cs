namespace AVACommando.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It fires the way it faces, as the player does, so to
/// shoot something it lines it up along one of the eight ways (across to the same column, or the same row, or a
/// diagonal), turns to face it and fires. With nothing to shoot it makes its way up the map along the open ground;
/// with a pillbox ahead it goes to stand below it, a grenade's throw away, and lobs one. Before it does anything it
/// looks at where the bullets, grenades, bikes and soldiers will be, and if that would get it killed, it takes
/// whichever way of moving keeps it clearest.
/// </summary>
public sealed class CommandoPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private const double Lined = 11, Close = 100, Engage = 160;

    private int _tick, _stuck, _wander, _lining, _giveUp;
    private double _lastX, _lastY;
    private (int Dx, int Dy) _wanderWay;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }
        var want = Plan(g);
        // Moving nowhere for a while when it means to: a few steps some other way.
        if ((want.Dx != 0 || want.Dy != 0) && Field.Distance(p.X, p.Y, _lastX, _lastY) < 0.5) { if (++_stuck > 25) { _stuck = 0; _wander = 20; _wanderWay = Field.Ways[(_tick / 7) % 8]; } }
        else _stuck = 0;
        _lastX = p.X;
        _lastY = p.Y;
        if (_wander > 0) { _wander--; want = want with { Dx = _wanderWay.Dx, Dy = _wanderWay.Dy }; Doing = "unsticking"; }
        return Safe(g, want);
    }

    /// <summary>What it would like to do, if nothing were coming at it.</summary>
    private Controls Plan(GameEngine g)
    {
        var p = g.Player;
        // A pillbox in range ahead: lob a grenade on it.
        var box = g.Pillboxes.Where(b => !b.Dead && b.Y > g.Camera && b.Y < g.ViewTop + 120 && b.Y > p.Y).OrderBy(b => b.Y).FirstOrDefault();
        if (box != null && p.Grenades > 0)
        {
            double tx = box.X, ty = box.Y - GameEngine.ThrowRange;
            if (Math.Abs(p.X - tx) < 14 && Math.Abs(p.Y - ty) < 24)
            {
                Doing = "grenade";
                return new Controls(Grenade: p.ThrowIn <= 0);
            }
        }
        var targets = Targets(g).ToList();
        // Something lined up: face it and fire.
        foreach (var (x, y, _) in targets)
            if (Way(g, p, x, y) is { } way)
            {
                Doing = "firing";
                if (way == p.Facing) return new Controls(Fire: true);
                var (dx, dy) = Field.Ways[way];
                return new Controls(dx, dy, true);
            }
        // Only the near ones are worth stopping for (or any, at the gate); the rest it shoots on the way.
        // (Not ones behind cover, and not one it's been trying to line up for too long.)
        var near = _giveUp > 0 ? [] : targets.Where(t => (g.Rush || Field.Distance(p.X, p.Y, t.X, t.Y) < Engage) && g.Area.Clear(p.X, p.Y, t.X, t.Y)).ToList();
        if (_giveUp > 0) _giveUp--;
        if (near.Count > 0 && ++_lining > 180) { _lining = 0; _giveUp = 90; }
        if (near.Count == 0) _lining = 0;
        if (near.Count > 0 && _giveUp == 0)
        {
            var (x, y, _) = near[0];
            double dx = x - p.X, dy = y - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < Close)
            {
                Doing = "backing off";
                return new Controls(-Math.Sign(dx), dy > -40 ? -1 : 1);
            }
            // Line it up the nearest way: the same column, the same row, or a diagonal.
            double col = Math.Abs(dx), row = Math.Abs(dy), diag = Math.Abs(col - row) / 1.4;
            Doing = "lining up";
            if (col <= row && col <= diag) return new Controls(Math.Sign(dx), 0);
            if (row <= diag) return new Controls(0, Math.Sign(dy));
            // Towards the diagonal: along whichever axis is the longer.
            return col > row ? new Controls(Math.Sign(dx), 0) : new Controls(0, Math.Sign(dy));
        }
        if (box != null && p.Grenades > 0)
        {
            Doing = "to the pillbox";
            return Towards(g, box.X, box.Y - GameEngine.ThrowRange);
        }
        if (g.Crates.FirstOrDefault(c => c.Y > g.Camera + 40 && c.Y < g.ViewTop - 30) is { } crate && p.Grenades < 8)
        {
            Doing = "to a crate";
            return Towards(g, crate.X, crate.Y);
        }
        if (g.Rush)
        {
            // At the fort: stand back from the gate and hold.
            Doing = "holding";
            return Towards(g, Field.Width / 2 + Math.Sin(_tick / 90.0) * 120, g.Camera + 160);
        }
        Doing = "advancing";
        return Towards(g, Field.Width / 2, g.ViewTop - 40);
    }

    /// <summary>Everything there is to shoot, nearest (and most dangerous) first.</summary>
    private static IEnumerable<(double X, double Y, double Cost)> Targets(GameEngine g)
    {
        var p = g.Player;
        var all = new List<(double X, double Y, double Cost)>();
        foreach (var s in g.Soldiers)
        {
            if (s.Dead || s.Y > g.ViewTop - 8 || s.Y < g.Camera + 4 || s.X < 4 || s.X > Field.Width - 4) continue;
            double d = Field.Distance(p.X, p.Y, s.X, s.Y);
            all.Add((s.X, s.Y, d * (s.Kind == SoldierKind.Officer ? 0.3 : s.Kind == SoldierKind.Guard ? 0.8 : 1)));
        }
        foreach (var b in g.Bikes)
            if (!b.Dead && b.X > 0 && b.X < Field.Width) all.Add((b.X, b.Y, Field.Distance(p.X, p.Y, b.X, b.Y) * 0.7));
        return all.OrderBy(t => t.Cost);
    }

    /// <summary>Which of the eight ways a shot now would hit something at (x, y), if any: near enough on the line, in range, nothing in the way.</summary>
    private static int? Way(GameEngine g, Commando p, double x, double y)
    {
        double dx = x - p.X, dy = y - p.Y, range = GameEngine.BulletSpeed * GameEngine.BulletLife;
        for (int w = 0; w < 8; w++)
        {
            var (wx, wy) = Field.Ways[w];
            double k = wx != 0 && wy != 0 ? Math.Sqrt(0.5) : 1, ux = wx * k, uy = wy * k;
            double along = dx * ux + dy * uy, off = Math.Abs(dx * uy - dy * ux);
            if (along > 10 && along < range * 0.95 && off < Lined && g.Area.Clear(p.X, p.Y, x, y)) return w;
        }
        return null;
    }

    /// <summary>The way to run to get towards a point, round what's in the way, by the open ground.</summary>
    private static Controls Towards(GameEngine g, double tx, double ty)
    {
        var p = g.Player;
        var area = g.Area;
        int r0 = Field.RowOf(g.Camera) + 1, r1 = Field.RowOf(g.ViewTop) - 1;
        int sc = Field.ColOf(p.X), sr = Field.RowOf(p.Y), gc = Math.Clamp(Field.ColOf(tx), 0, Field.Cols - 1), gr = Math.Clamp(Field.RowOf(ty), r0, r1);
        // A search over the cells on the screen, from the player out, for the cell nearest the goal.
        var from = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int, int)>();
        from[(sc, sr)] = (sc, sr);
        queue.Enqueue((sc, sr));
        (int C, int R) best = (sc, sr);
        double bestD = double.MaxValue;
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            double d = Math.Abs(c - gc) + Math.Abs(r - gr) * 1.0;
            if (d < bestD) { bestD = d; best = (c, r); }
            foreach (var (dc, dr) in Field.Ways)
            {
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nc >= Field.Cols || nr < r0 || nr > r1 || from.ContainsKey((nc, nr)) || area.Blocks(nc, nr)) continue;
                if (dc != 0 && dr != 0 && (area.Blocks(c + dc, r) || area.Blocks(c, r + dr))) continue;
                from[(nc, nr)] = (c, r);
                queue.Enqueue((nc, nr));
            }
        }
        // Back along the way to the first step or two, as far as it can run straight.
        var path = new List<(int C, int R)>();
        for (var at = best; at != (sc, sr); at = from[at]) path.Add(at);
        path.Reverse();
        double wx = best == (sc, sr) ? tx : Field.CellX(path[0].C), wy = best == (sc, sr) ? ty : Field.CellY(path[0].R);
        foreach (var (c, r) in path.Take(5))
        {
            double x = Field.CellX(c), y = Field.CellY(r);
            if (!Open(area, p.X, p.Y, x, y)) break;
            (wx, wy) = (x, y);
        }
        if (best == (sc, sr) && Field.Distance(p.X, p.Y, tx, ty) < 6) return default;
        double vx = wx - p.X, vy = wy - p.Y, len = Math.Max(1, Math.Sqrt(vx * vx + vy * vy));
        return new Controls(Math.Abs(vx) > len * 0.38 ? Math.Sign(vx) : 0, Math.Abs(vy) > len * 0.38 ? Math.Sign(vy) : 0);
    }

    private static bool Open(Area area, double ax, double ay, double bx, double by)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 6));
        double a = Math.Atan2(by - ay, bx - ax), px = -Math.Sin(a) * (Commando.Radius + 1), py = Math.Cos(a) * (Commando.Radius + 1);
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n, x = ax + (bx - ax) * t, y = ay + (by - ay) * t;
            if (Ground.Blocks(area.AtPoint(x + px, y + py)) || Ground.Blocks(area.AtPoint(x - px, y - py))) return false;
        }
        return true;
    }

    /// <summary>
    /// What it wanted, unless that walks into something: then of the nine ways to move (counting standing still),
    /// the one that keeps it clearest, still firing if it was.
    /// </summary>
    private Controls Safe(GameEngine g, Controls want)
    {
        double here = Clearance(g, want);
        if (here > 14) return want;
        Controls best = want;
        double most = here;
        foreach (var (dx, dy) in Field.Ways.Append((0, 0)))
        {
            var c = new Controls(dx, dy);
            double clear = Clearance(g, c);
            if (clear > most + 2) { most = clear; best = c; }
        }
        if (best == want) return want;
        Doing = "dodging";
        // Only fire on the move if that way still faces something.
        return best with { Fire = want.Fire && best.Dx == want.Dx && best.Dy == want.Dy };
    }

    /// <summary>How close the nearest danger comes to it over the next half second or so, moving like this.</summary>
    private static double Clearance(GameEngine g, Controls c)
    {
        var p = g.Player;
        var me = new Commando { X = p.X, Y = p.Y, Facing = p.Facing };
        double least = double.MaxValue;
        var shots = g.Bullets.Where(b => b.Hostile && Field.Distance(b.X, b.Y, p.X, p.Y) < 320).Select(b => (b.X, b.Y, b.Vx, b.Vy)).ToArray();
        var bombs = g.Grenades.Where(n => n.Hostile).ToList();
        var bikes = g.Bikes.Where(b => !b.Dead).ToList();
        var men = g.Soldiers.Where(s => !s.Dead && Field.Distance(s.X, s.Y, p.X, p.Y) < 120).ToList();
        if (shots.Length == 0 && bombs.Count == 0 && bikes.Count == 0 && men.Count == 0) return double.MaxValue;
        for (int t = 1; t <= 36; t++)
        {
            double dt = t * Field.Step;
            GameEngine.Step(g.Area, me, c, g.Camera, Field.Step);
            for (int i = 0; i < shots.Length; i++)
            {
                var (x, y, vx, vy) = shots[i];
                if (double.IsNaN(x)) continue;
                double sx = x + vx * dt, sy = y + vy * dt;
                if (Ground.Stops(g.Area.AtPoint(sx, sy))) { shots[i] = (double.NaN, 0, 0, 0); continue; }
                least = Math.Min(least, Field.Distance(sx, sy, me.X, me.Y) - Commando.Radius);
            }
            foreach (var n in bombs)
                if (Math.Abs(n.Time - n.T - dt) < Field.Step * 1.5) least = Math.Min(least, Field.Distance(n.ToX, n.ToY, me.X, me.Y) - GameEngine.EnemyBlast);
            foreach (var b in bikes) least = Math.Min(least, Field.Distance(b.X + b.Vx * dt, b.Y, me.X, me.Y) - Bike.Radius - Commando.Radius);
            foreach (var s in men) least = Math.Min(least, Field.Distance(s.X, s.Y, me.X, me.Y) - Soldier.Radius - Commando.Radius);
        }
        return least;
    }
}
