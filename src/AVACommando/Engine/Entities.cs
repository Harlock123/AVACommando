namespace AVACommando.Engine;

/// <summary>The controls: running (each -1, 0 or 1; up is +1), fire, and a grenade.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Grenade = false);

/// <summary>
/// The screen and the world. The world runs up the map from 0 at the start of an area, so y grows upwards; the
/// camera is the world y at the bottom of the screen, and only ever moves up.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Cell = 32;
    public const int Cols = 32;
    /// <summary>The score bar across the top of the screen.</summary>
    public const double Hud = 56;

    public static double ScreenY(double wy, double camera) => Height - (wy - camera);
    public static int ColOf(double x) => (int)Math.Floor(x / Cell);
    public static int RowOf(double y) => (int)Math.Floor(y / Cell);
    public static double CellX(int col) => (col + 0.5) * Cell;
    public static double CellY(int row) => (row + 0.5) * Cell;
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    /// <summary>The eight ways to face, from up (0) round clockwise.</summary>
    public static readonly (int Dx, int Dy)[] Ways = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];

    public static int WayOf(int dx, int dy) => Array.IndexOf(Ways, (dx, dy));
    public static double AngleOf(int way) => Math.Atan2(Ways[way].Dy, Ways[way].Dx);
}

/// <summary>What stands in a cell of the map.</summary>
public static class Ground
{
    public const char Open = '.', Rock = 'R', Sandbag = 'S', Wall = 'W', Water = '~', Bridge = '=', Tree = 'T', Bunker = 'X', Gate = 'G', Fort = 'F';

    /// <summary>Whether someone on foot can't go there.</summary>
    public static bool Blocks(char c) => c is Rock or Sandbag or Wall or Water or Bunker or Fort;

    /// <summary>Whether a bullet stops there (grenades fly over everything).</summary>
    public static bool Stops(char c) => c is Rock or Sandbag or Wall or Bunker or Fort;
}

public enum SpawnKind { Rifleman, Grenadier, Prisoner, Crate, Bike }

/// <summary>Something waiting in the map, that comes on when the top of the screen reaches it.</summary>
public sealed record Spawn(SpawnKind Kind, double X, double Y);

/// <summary>
/// One area of the battlefield: a map of cells, bottom row first, built from a run of stretches of ground
/// (rocks and trees, sandbag lines, rivers with bridges, bunkers, a prisoner's road) and ending at the gate of a
/// fort; where the enemy waits in it; and its bunkers.
/// </summary>
public sealed class Area
{
    public string Name { get; }
    public int Theme { get; }
    public List<char[]> Rows { get; } = [];
    public List<Spawn> Spawns { get; } = [];
    public List<(int Col, int Row)> Bunkers { get; } = [];
    /// <summary>The row of the fort's wall, with the gate in it.</summary>
    public int GateRow { get; private set; }
    public const int GateCol = 13, GateWidth = 6;

    public double GateY => GateRow * Field.Cell;
    /// <summary>The camera stops here, with the fort's wall near the top of the screen.</summary>
    public double CameraEnd => GateY + 3 * Field.Cell - (Field.Height - Field.Hud);

    public char At(int col, int row) => col < 0 || col >= Field.Cols ? Ground.Wall : row < 0 ? Ground.Open : row >= Rows.Count ? Ground.Fort : Rows[row][col];
    public char AtPoint(double x, double y) => At(Field.ColOf(x), Field.RowOf(y));
    public bool Blocks(int col, int row) => Ground.Blocks(At(col, row));

    /// <summary>Whether a bullet could fly straight from one point to the other.</summary>
    public bool Clear(double ax, double ay, double bx, double by)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 8));
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            if (Ground.Stops(AtPoint(ax + (bx - ax) * t, ay + (by - ay) * t))) return false;
        }
        return true;
    }

    /// <summary>Pushes someone (a circle) out of anything they can't stand in; true if it had to.</summary>
    public bool Push(ref double x, ref double y, double r)
    {
        bool pushed = false;
        int c0 = Field.ColOf(x - r), c1 = Field.ColOf(x + r), r0 = Field.RowOf(y - r), r1 = Field.RowOf(y + r);
        for (int row = r0; row <= r1; row++)
        for (int col = c0; col <= c1; col++)
        {
            if (!Blocks(col, row)) continue;
            double left = col * Field.Cell, bottom = row * Field.Cell;
            double nx = Math.Clamp(x, left, left + Field.Cell), ny = Math.Clamp(y, bottom, bottom + Field.Cell);
            double dx = x - nx, dy = y - ny, d = Math.Sqrt(dx * dx + dy * dy);
            if (d >= r) continue;
            if (d < 1e-6)
            {
                double l = x - left, rt = left + Field.Cell - x, b = y - bottom, t = bottom + Field.Cell - y, m = Math.Min(Math.Min(l, rt), Math.Min(b, t));
                if (m == l) x = left - r; else if (m == rt) x = left + Field.Cell + r; else if (m == b) y = bottom - r; else y = bottom + Field.Cell + r;
            }
            else
            {
                x = nx + dx / d * r;
                y = ny + dy / d * r;
            }
            pushed = true;
        }
        return pushed;
    }

    private static readonly string[] Names = ["THE BEACHHEAD", "THE JUNGLE ROAD", "THE RIVER FORT"];

    private Area(string name, int theme)
    {
        Name = name;
        Theme = theme;
    }

    /// <summary>
    /// Area <paramref name="number"/> (1 for the first): the same ground every time for the same number, the three
    /// kinds of country in turn, longer and busier as the numbers go up.
    /// </summary>
    public static Area Build(int number)
    {
        int theme = (number - 1) % 3;
        var a = new Area(Names[theme], theme);
        var rng = new Random(1985 + number * 7919);
        a.Open(10, rng, trees: 0.04, rocks: 0);
        int stretches = 11 + Math.Min(6, number);
        string[] plan = theme switch
        {
            0 => ["scatter", "sandbags", "scatter", "bunkers", "prisoner", "sandbags", "scatter", "wall"],
            1 => ["jungle", "river", "jungle", "prisoner", "bunkers", "jungle", "sandbags", "river"],
            _ => ["scatter", "wall", "river", "bunkers", "prisoner", "sandbags", "bunkers", "river"],
        };
        for (int i = 0; i < stretches; i++)
        {
            // Every area has its sandbags, its bunkers and its prisoner early on; after that, the country's own mix.
            string kind = i switch { 0 => "scatter", 1 => "sandbags", 3 => "bunkers", 5 => "prisoner", _ => plan[rng.Next(plan.Length)] };
            switch (kind)
            {
                case "scatter": a.Open(14, rng, trees: 0.03, rocks: 0.05, enemies: 2); break;
                case "jungle": a.Open(14, rng, trees: 0.12, rocks: 0.02, enemies: 2); break;
                case "sandbags": a.Sandbags(rng); break;
                case "river": a.River(rng); break;
                case "bunkers": a.BunkerField(rng); break;
                case "prisoner": a.PrisonerRoad(rng); break;
                case "wall": a.WallLine(rng); break;
            }
            if (rng.Next(3) == 0) a.Spawns.Add(new Spawn(SpawnKind.Bike, 0, (a.Rows.Count - 4) * Field.Cell));
        }
        a.Fortress(rng);
        return a;
    }

    public static int Count => Names.Length;

    private char[] Row(char fill = Ground.Open) => Enumerable.Repeat(fill, Field.Cols).ToArray();

    /// <summary>A free spot in a row, away from the edges.</summary>
    private int FreeCol(Random rng, int row)
    {
        for (int i = 0; i < 40; i++)
        {
            int c = 2 + rng.Next(Field.Cols - 4);
            if (!Ground.Blocks(Rows[row][c])) return c;
        }
        return Field.Cols / 2;
    }

    private void Enemy(Random rng, int row, SpawnKind kind = SpawnKind.Rifleman) =>
        Spawns.Add(new Spawn(kind, Field.CellX(FreeCol(rng, row)), Field.CellY(row)));

    private void Open(int rows, Random rng, double trees, double rocks, int enemies = 0)
    {
        int start = Rows.Count;
        for (int r = 0; r < rows; r++)
        {
            var row = Row();
            for (int c = 1; c < Field.Cols - 1; c++)
                if (rng.NextDouble() < trees) row[c] = Ground.Tree;
            Rows.Add(row);
        }
        // Rocks in clumps of one to four, never closing the way.
        int clumps = (int)(rows * Field.Cols * rocks / 2.5);
        for (int i = 0; i < clumps; i++)
        {
            int c = 1 + rng.Next(Field.Cols - 3), r = start + 1 + rng.Next(Math.Max(1, rows - 2));
            Rows[r][c] = Ground.Rock;
            if (rng.Next(2) == 0) Rows[r][c + 1] = Ground.Rock;
            if (rng.Next(3) == 0 && r + 1 < start + rows) Rows[r + 1][c] = Ground.Rock;
        }
        for (int i = 0; i < enemies; i++) Enemy(rng, start + rows / 2 + rng.Next(rows / 2), rng.Next(4) == 0 ? SpawnKind.Grenadier : SpawnKind.Rifleman);
        if (rng.Next(4) == 0) Spawns.Add(new Spawn(SpawnKind.Crate, Field.CellX(FreeCol(rng, start + rows / 2)), Field.CellY(start + rows / 2)));
    }

    /// <summary>A line of sandbags across, with gaps, and riflemen and a grenadier behind it.</summary>
    private void Sandbags(Random rng)
    {
        Open(5, rng, 0.02, 0);
        int line = Rows.Count;
        var row = Row(Ground.Sandbag);
        row[0] = row[Field.Cols - 1] = Ground.Open;
        for (int g = 0; g < 3; g++)
        {
            int c = 3 + g * 9 + rng.Next(5);
            for (int k = 0; k < 3; k++) row[Math.Min(Field.Cols - 2, c + k)] = Ground.Open;
        }
        Rows.Add(row);
        Open(6, rng, 0.02, 0.02);
        Enemy(rng, line + 1);
        Enemy(rng, line + 2);
        Enemy(rng, line + 3, SpawnKind.Grenadier);
    }

    /// <summary>A river across, three rows wide, with two bridges; a grenadier on the far bank.</summary>
    private void River(Random rng)
    {
        Open(4, rng, 0.04, 0.01);
        int b1 = 3 + rng.Next(8), b2 = 18 + rng.Next(9);
        for (int r = 0; r < 3; r++)
        {
            var row = Row(Ground.Water);
            for (int k = 0; k < 3; k++) row[b1 + k] = row[b2 + k] = Ground.Bridge;
            Rows.Add(row);
        }
        Open(6, rng, 0.05, 0.02);
        Enemy(rng, Rows.Count - 5, SpawnKind.Grenadier);
        Enemy(rng, Rows.Count - 3);
    }

    /// <summary>Two bunkers behind short sandbag wings: only grenades take them out. A crate of grenades nearby.</summary>
    private void BunkerField(Random rng)
    {
        Open(5, rng, 0.02, 0.02);
        int r = Rows.Count;
        Rows.Add(Row());
        Rows.Add(Row());
        Rows.Add(Row());
        foreach (int c in new[] { 4 + rng.Next(8), 19 + rng.Next(8) })
        {
            Rows[r][c] = Rows[r][c + 1] = Rows[r + 1][c] = Rows[r + 1][c + 1] = Ground.Bunker;
            Rows[r][c - 1] = Rows[r][c + 2] = Ground.Sandbag;
            Bunkers.Add((c, r));
        }
        Open(5, rng, 0.02, 0.02, enemies: 1);
        Spawns.Add(new Spawn(SpawnKind.Crate, Field.CellX(FreeCol(rng, r - 3)), Field.CellY(r - 3)));
    }

    /// <summary>A road across, along which guards march a prisoner.</summary>
    private void PrisonerRoad(Random rng)
    {
        Open(5, rng, 0.03, 0.01);
        int r = Rows.Count;
        Rows.Add(Row());
        Rows.Add(Row());
        Open(5, rng, 0.03, 0.01, enemies: 1);
        Spawns.Add(new Spawn(SpawnKind.Prisoner, rng.Next(2) == 0 ? -40 : Field.Width + 40, (r + 1) * Field.Cell));
    }

    /// <summary>A stone wall across with two openings, riflemen behind.</summary>
    private void WallLine(Random rng)
    {
        Open(5, rng, 0.02, 0.01);
        int line = Rows.Count;
        var row = Row(Ground.Wall);
        foreach (int c in new[] { 4 + rng.Next(8), 19 + rng.Next(8) })
            for (int k = 0; k < 3; k++) row[c + k] = Ground.Open;
        Rows.Add(row);
        Rows.Add((char[])row.Clone());
        Open(6, rng, 0.02, 0.02);
        Enemy(rng, line + 3);
        Enemy(rng, line + 4);
    }

    /// <summary>The fort: open ground up to its wall, the gate in the middle, and the fort behind.</summary>
    private void Fortress(Random rng)
    {
        Open(8, rng, 0.02, 0.0);
        GateRow = Rows.Count;
        for (int r = 0; r < 2; r++)
        {
            var row = Row(Ground.Wall);
            for (int k = 0; k < GateWidth; k++) row[GateCol + k] = Ground.Gate;
            Rows.Add(row);
        }
        for (int r = 0; r < 10; r++) Rows.Add(Row(Ground.Fort));
    }
}

/// <summary>The player: a lone soldier who runs eight ways and fires the way he faces.</summary>
public sealed class Commando
{
    public double X, Y, Walk;
    /// <summary>Which way he faces: one of <see cref="Field.Ways"/>.</summary>
    public int Facing;
    public bool Alive = true;
    public double Safe, FireIn, ThrowIn;
    public int Grenades;
    public const double Radius = 10;
}

public enum SoldierKind
{
    /// <summary>Runs to a spot near you, stops, aims and fires.</summary>
    Rifleman,
    /// <summary>Keeps further off and lobs grenades over cover.</summary>
    Grenadier,
    /// <summary>Marches a prisoner along: shoot both guards and the prisoner goes free.</summary>
    Guard,
    /// <summary>Pours out of the fort's gate at the end of an area.</summary>
    Rusher,
    /// <summary>The fort's officer, running for it once the rush is over.</summary>
    Officer,
}

public sealed class Soldier
{
    public SoldierKind Kind;
    public double X, Y, ToX, ToY, Facing, T, Wait, FireIn, Walk, Stuck;
    /// <summary>Stood still, about to fire.</summary>
    public bool Aiming;
    public bool Dead;
    public Prisoner? Escort;
    public const double Radius = 10;
}

/// <summary>A prisoner being marched across the map by two guards.</summary>
public sealed class Prisoner
{
    public double X, Y, Vx, Walk;
    public bool Free, Gone;
}

/// <summary>An enemy motorbike and sidecar crossing the screen, the passenger dropping a grenade on the way.</summary>
public sealed class Bike
{
    public double X, Y, Vx;
    public bool Thrown, Dead;
    public const double Radius = 18;
}

/// <summary>A concrete pillbox in the map: it fires a spread of three, and only a grenade can take it out.</summary>
public sealed class Pillbox
{
    public int Col, Row;
    public double FireIn = 1;
    public bool Dead;
    public double X => (Col + 1) * Field.Cell;
    public double Y => (Row + 1) * Field.Cell;
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy, Life;
    public bool Hostile;
}

/// <summary>A grenade in the air: from where it was thrown to where it will land and go off.</summary>
public sealed class Grenade
{
    public double FromX, FromY, ToX, ToY, T, Time;
    public bool Hostile;
    public double X => FromX + (ToX - FromX) * Math.Min(1, T / Time);
    public double Y => FromY + (ToY - FromY) * Math.Min(1, T / Time);
    /// <summary>How high it is, for the drawing: up and down in an arc.</summary>
    public double Height => Math.Sin(Math.PI * Math.Min(1, T / Time)) * 60;
}

/// <summary>A crate of grenades on the ground.</summary>
public sealed class Crate
{
    public double X, Y;
    public const int Grenades = 4;
}

/// <summary>A fleck of something, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Blast, Word, Puff }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
