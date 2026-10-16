namespace AVACommando.Engine;

public sealed partial class GameEngine
{
    public const double RunSpeed = 170, BulletSpeed = 540, BulletLife = 0.55, FireGap = 0.13;
    public const int MaxBullets = 3, StartGrenades = 6;
    public const double ThrowRange = 230, ThrowTime = 0.7, ThrowGap = 0.45, BlastRadius = 50, EnemyBlast = 40;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, SafeSeconds = 2.5, ClearSeconds = 3.5, RushEvery = 0.55;
    public const int ExtraLifeEvery = 20000, FreedPoints = 1000, PillboxPoints = 500, BikePoints = 300, OfficerPoints = 2000;

    /// <summary>The area: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Area Area { get; private set; } = Area.Build(1);
    /// <summary>The world y at the bottom of the screen.</summary>
    public double Camera { get; private set; }
    public Commando Player { get; } = new();
    public List<Soldier> Soldiers { get; } = [];
    public List<Prisoner> Prisoners { get; } = [];
    public List<Bike> Bikes { get; } = [];
    public List<Pillbox> Pillboxes { get; } = [];
    public List<Bullet> Bullets { get; } = [];
    public List<Grenade> Grenades { get; } = [];
    public List<Crate> Crates { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public CommandoPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>At the fort's gate: the rush has begun.</summary>
    public bool Rush { get; private set; }
    /// <summary>Rushers still to come out of the gate.</summary>
    public int RushLeft { get; private set; }
    public bool OfficerOut { get; private set; }
    /// <summary>For tests: the enemy stands still and holds its fire, and no more come.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _spawnIndex;
    private double _ambientIn, _rushIn, _scrolled;
    private List<Spawn> _spawns = [];

    public double EnemyShotSpeed => Math.Min(340, 230 + 20 * (Wave - 1));
    public double SoldierSpeed => Math.Min(130, 85 + 8 * (Wave - 1));
    public double SoldierFireGap => Math.Max(0.9, 2.0 - 0.2 * (Wave - 1));
    public double AimError => Math.Max(0.03, 0.14 - 0.025 * (Wave - 1));
    /// <summary>A soldier comes on every so far up the map...</summary>
    public double SpawnSpacing => Math.Max(90, 170 - 12 * (Wave - 1));
    /// <summary>...and, slowly, every so often anyway, so standing still is no answer.</summary>
    public double AmbientEvery => Math.Max(2.2, 4.5 - 0.3 * (Wave - 1));
    public int MaxSoldiers => Math.Min(10, 5 + Wave);
    public int RushCount => 12 + 3 * Wave;
    public double PillboxGap => Math.Max(1.3, 2.6 - 0.2 * (Wave - 1));
    /// <summary>The top of the play area on the screen, in world y.</summary>
    public double ViewTop => Camera + Field.Height - Field.Hud;

    public static int PointsFor(SoldierKind kind) => kind switch { SoldierKind.Grenadier => 150, SoldierKind.Officer => OfficerPoints, _ => 100 };

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Player.Grenades = StartGrenades;
        Wreckage.Clear();
        Area = Area.Build(1);
        Camera = 0;
        PlacePlayer(Field.Width / 2, 120);
        SetPhase(Phase.Start);
    }

    private void PlacePlayer(double x, double y)
    {
        var p = Player;
        p.X = x;
        p.Y = y;
        Area.Push(ref p.X, ref p.Y, Commando.Radius);
        p.Facing = 0;
        p.Alive = true;
        p.Safe = SafeSeconds;
        p.FireIn = p.ThrowIn = 0;
    }

    /// <summary>An area: its ground built, the player at the bottom, the enemy waiting in it.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Area = Area.Build(wave);
        Camera = 0;
        Soldiers.Clear();
        Prisoners.Clear();
        Bikes.Clear();
        Bullets.Clear();
        Grenades.Clear();
        Crates.Clear();
        Pillboxes.Clear();
        foreach (var (c, r) in Area.Bunkers) Pillboxes.Add(new Pillbox { Col = c, Row = r, FireIn = 1 + _rng.NextDouble() });
        _spawns = Area.Spawns.OrderBy(s => s.Y).ToList();
        _spawnIndex = 0;
        _ambientIn = 2;
        _scrolled = 0;
        Rush = OfficerOut = false;
        RushLeft = 0;
        PlacePlayer(Field.Width / 2, 120);
        SetPhase(Phase.Start);
        Say($"AREA {wave}", Area.Name, StartSeconds + 0.3);
        Play(Sfx.AreaStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                Run(dt, c);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Run(dt, c);
                Fire(c);
                Throw(c);
                Scroll();
                Reinforce(dt);
                MoveSoldiers(dt);
                MovePrisoners(dt);
                MoveBikes(dt);
                Pillboxes_(dt);
                MoveBullets(dt);
                MoveGrenades(dt);
                Contact();
                Tidy();
                if (Phase == Phase.Play && OfficerOut && !Soldiers.Any(s => s.Kind == SoldierKind.Officer)) Clear();
                break;
            case Phase.Dying:
                MoveSoldiers(dt);
                MovePrisoners(dt);
                MoveBikes(dt);
                MoveBullets(dt);
                MoveGrenades(dt);
                Tidy();
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Run(dt, c);
                MoveBullets(dt);
                MoveGrenades(dt);
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the player ─────────────────────────────

    /// <summary>One step of running: eight ways at one speed, out of anything solid, on the screen, and below the fort.</summary>
    public static void Step(Area area, Commando p, Controls c, double camera, double dt)
    {
        if (c.Dx == 0 && c.Dy == 0) return;
        p.Facing = Field.WayOf(Math.Sign(c.Dx), Math.Sign(c.Dy));
        double k = c.Dx != 0 && c.Dy != 0 ? Math.Sqrt(0.5) : 1;
        double x = p.X + Math.Sign(c.Dx) * RunSpeed * k * dt, y = p.Y + Math.Sign(c.Dy) * RunSpeed * k * dt;
        area.Push(ref x, ref y, Commando.Radius);
        p.X = Math.Clamp(x, Commando.Radius, Field.Width - Commando.Radius);
        p.Y = Math.Clamp(y, camera + Commando.Radius + 4, camera + Field.Height - Field.Hud - Commando.Radius);
        p.Walk += dt;
    }

    private void Run(double dt, Controls c)
    {
        var p = Player;
        if (!p.Alive) return;
        p.Safe = Math.Max(0, p.Safe - dt);
        p.FireIn -= dt;
        p.ThrowIn -= dt;
        Step(Area, p, c, Camera, dt);
    }

    private void Fire(Controls c)
    {
        var p = Player;
        if (!c.Fire || p.FireIn > 0 || !p.Alive || Bullets.Count(b => !b.Hostile) >= MaxBullets) return;
        p.FireIn = FireGap;
        var (dx, dy) = Field.Ways[p.Facing];
        double k = dx != 0 && dy != 0 ? Math.Sqrt(0.5) : 1;
        Bullets.Add(new Bullet { X = p.X + dx * k * 12, Y = p.Y + dy * k * 12, Vx = dx * k * BulletSpeed, Vy = dy * k * BulletSpeed, Life = BulletLife });
        Play(Sfx.Fire);
    }

    /// <summary>A grenade, always thrown forward (up the map), over whatever's in the way.</summary>
    private void Throw(Controls c)
    {
        var p = Player;
        if (!c.Grenade || p.ThrowIn > 0 || !p.Alive || p.Grenades <= 0) return;
        p.ThrowIn = ThrowGap;
        p.Grenades--;
        Grenades.Add(new Grenade { FromX = p.X, FromY = p.Y + 8, ToX = p.X, ToY = p.Y + ThrowRange, Time = ThrowTime });
        Play(Sfx.Throw);
    }

    /// <summary>The screen follows the player up the map (never back down), as far as the fort.</summary>
    private void Scroll()
    {
        double want = Player.Y - (Field.Height - Field.Hud) * 0.5;
        if (want <= Camera) return;
        double was = Camera;
        Camera = Math.Min(Area.CameraEnd, want);
        _scrolled += Camera - was;
    }

    /// <summary>Back in where the last one fell, a little further down the screen, safe for a moment.</summary>
    private void Respawn()
    {
        double x = Player.X, y = Math.Max(Camera + 110, Math.Min(Player.Y, Camera + 200));
        PlacePlayer(x, y);
        Player.Grenades = Math.Max(Player.Grenades, 3);
        Bullets.RemoveAll(b => b.Hostile);
        Grenades.RemoveAll(g => g.Hostile);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>
    /// The enemy waiting in the map comes on as the screen reaches it; more come in from the top and sides all the
    /// while; and at the fort, they pour out of the gate.
    /// </summary>
    private void Reinforce(double dt)
    {
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Y <= ViewTop + 40)
        {
            var s = _spawns[_spawnIndex++];
            if (!Calm) Bring(s);
        }
        if (Calm) return;
        if (!Rush && Camera >= Area.CameraEnd - 0.5)
        {
            Rush = true;
            RushLeft = RushCount;
            _rushIn = 1;
            Say("THE FORT GATE", "HOLD THEM OFF", 2.2);
            Play(Sfx.Gate);
        }
        if (Rush)
        {
            if (RushLeft > 0 && (_rushIn -= dt) <= 0)
            {
                _rushIn = RushEvery;
                RushLeft--;
                double x = Area.GateCol * Field.Cell + Field.Cell * (0.5 + _rng.NextDouble() * (Area.GateWidth - 1));
                Soldiers.Add(new Soldier { Kind = SoldierKind.Rusher, X = x, Y = Area.GateY + Field.Cell, Facing = -Math.PI / 2, FireIn = SoldierFireGap * (1 + _rng.NextDouble()) });
            }
            if (RushLeft == 0 && !OfficerOut && !Soldiers.Any(s => s.Kind == SoldierKind.Rusher))
            {
                // The officer makes a run for it.
                OfficerOut = true;
                bool left = _rng.Next(2) == 0;
                double y = Area.GateY - Field.Cell * 2;
                Soldiers.Add(new Soldier { Kind = SoldierKind.Officer, X = Field.Width / 2, Y = Area.GateY + Field.Cell, ToX = left ? -40 : Field.Width + 40, ToY = y, Facing = -Math.PI / 2 });
                Say("THE OFFICER!", "DON'T LET HIM GET AWAY", 1.6);
            }
            return;
        }
        bool due = (_ambientIn -= dt) <= 0;
        if (_scrolled >= SpawnSpacing) { _scrolled -= SpawnSpacing; due = true; }
        if (!due) return;
        _ambientIn = AmbientEvery * (0.7 + _rng.NextDouble() * 0.6);
        if (Soldiers.Count(s => s.Kind is SoldierKind.Rifleman or SoldierKind.Grenadier) >= MaxSoldiers) return;
        var kind = _rng.Next(5) == 0 ? SoldierKind.Grenadier : SoldierKind.Rifleman;
        for (int i = 0; i < 10; i++)
        {
            double x, y;
            if (_rng.Next(3) == 0)
            {
                // From a side, in the top half.
                x = _rng.Next(2) == 0 ? -14 : Field.Width + 14;
                y = Camera + (Field.Height - Field.Hud) * (0.5 + _rng.NextDouble() * 0.45);
                if (Area.Blocks(Field.ColOf(Math.Clamp(x, 1, Field.Width - 1)), Field.RowOf(y))) continue;
            }
            else
            {
                x = 30 + _rng.NextDouble() * (Field.Width - 60);
                y = ViewTop + 24;
                if (Area.Blocks(Field.ColOf(x), Field.RowOf(y))) continue;
            }
            Bring(new Spawn(kind == SoldierKind.Grenadier ? SpawnKind.Grenadier : SpawnKind.Rifleman, x, y));
            break;
        }
    }

    private void Bring(Spawn s)
    {
        switch (s.Kind)
        {
            case SpawnKind.Rifleman or SpawnKind.Grenadier:
            {
                var kind = s.Kind == SpawnKind.Grenadier ? SoldierKind.Grenadier : SoldierKind.Rifleman;
                var o = new Soldier { Kind = kind, X = s.X, Y = s.Y, Facing = -Math.PI / 2, FireIn = SoldierFireGap * (0.5 + _rng.NextDouble()) };
                Choose(o);
                Soldiers.Add(o);
                break;
            }
            case SpawnKind.Crate:
                Crates.Add(new Crate { X = s.X, Y = s.Y });
                break;
            case SpawnKind.Prisoner:
            {
                double dir = s.X < 0 ? 1 : -1;
                var p = new Prisoner { X = s.X, Y = s.Y, Vx = dir * 55 };
                Prisoners.Add(p);
                foreach (int side in new[] { -1, 1 })
                    Soldiers.Add(new Soldier { Kind = SoldierKind.Guard, X = s.X + side * 30, Y = s.Y, Escort = p, Facing = dir > 0 ? 0 : Math.PI, FireIn = 2 + _rng.NextDouble() * 2 });
                break;
            }
            case SpawnKind.Bike:
            {
                bool left = _rng.Next(2) == 0;
                double y = Camera + (Field.Height - Field.Hud) * (0.55 + _rng.NextDouble() * 0.3);
                Bikes.Add(new Bike { X = left ? -40 : Field.Width + 40, Y = y, Vx = (left ? 1 : -1) * (240 + 20 * Wave) });
                break;
            }
        }
    }

    /// <summary>Where a soldier runs to next: somewhere above the player and to one side, that he can stand on.</summary>
    private void Choose(Soldier s)
    {
        var p = Player;
        double above = s.Kind == SoldierKind.Grenadier ? 220 + _rng.NextDouble() * 120 : 130 + _rng.NextDouble() * 170;
        for (int i = 0; i < 10; i++)
        {
            double x = Math.Clamp(p.X + (_rng.NextDouble() - 0.5) * 460, 20, Field.Width - 20);
            double y = Math.Clamp(p.Y + above, Camera + 60, ViewTop - 20);
            if (Area.Blocks(Field.ColOf(x), Field.RowOf(y))) continue;
            s.ToX = x;
            s.ToY = y;
            s.T = 0;
            return;
        }
        s.ToX = s.X;
        s.ToY = s.Y;
    }

    private void MoveSoldiers(double dt)
    {
        var p = Player;
        foreach (var s in Soldiers.ToList())
        {
            if (Calm || s.Dead) continue;
            s.T += dt;
            s.FireIn -= dt;
            switch (s.Kind)
            {
                case SoldierKind.Guard:
                    if (s.Escort is { } e)
                    {
                        double side = s.X < e.X ? -1 : 1;
                        Walk(s, e.X + side * 30, e.Y, Math.Abs(e.Vx) + 30, dt);
                        if (Sees(s) && s.FireIn <= 0 && Field.Distance(s.X, s.Y, p.X, p.Y) < 300) { Shoot(s); s.FireIn = SoldierFireGap * 2; }
                    }
                    break;
                case SoldierKind.Officer:
                    Walk(s, s.ToX, s.ToY, 150, dt);
                    break;
                case SoldierKind.Rusher:
                    Walk(s, p.X, p.Y, SoldierSpeed * 1.1, dt);
                    if (Sees(s) && s.FireIn <= 0) { Shoot(s); s.FireIn = SoldierFireGap * 1.4; }
                    break;
                default:
                    if (s.Aiming)
                    {
                        s.Facing = Math.Atan2(p.Y - s.Y, p.X - s.X);
                        if ((s.Wait -= dt) > 0) break;
                        s.Aiming = false;
                        if (Sees(s))
                        {
                            if (s.Kind == SoldierKind.Grenadier) Lob(s);
                            else Shoot(s);
                            s.FireIn = SoldierFireGap * (s.Kind == SoldierKind.Grenadier ? 1.6 : 1);
                        }
                        Choose(s);
                        break;
                    }
                    double before = Field.Distance(s.X, s.Y, s.ToX, s.ToY);
                    Walk(s, s.ToX, s.ToY, SoldierSpeed, dt);
                    double after = Field.Distance(s.X, s.Y, s.ToX, s.ToY);
                    s.Stuck = before - after < SoldierSpeed * dt * 0.3 ? s.Stuck + dt : 0;
                    if (after < 6 || s.T > 2.6 || s.Stuck > 0.3)
                    {
                        if (s.FireIn <= 0 && p.Alive && Phase == Phase.Play) { s.Aiming = true; s.Wait = 0.45; }
                        else Choose(s);
                    }
                    break;
            }
        }
    }

    private void Walk(Soldier s, double tx, double ty, double speed, double dt)
    {
        double dx = tx - s.X, dy = ty - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1) return;
        double step = Math.Min(d, speed * dt);
        double x = s.X + dx / d * step, y = s.Y + dy / d * step;
        if (s.Kind is not (SoldierKind.Guard or SoldierKind.Officer)) Area.Push(ref x, ref y, Soldier.Radius);
        s.X = x;
        s.Y = y;
        s.Facing = Math.Atan2(dy, dx);
        s.Walk += dt;
    }

    /// <summary>Whether a soldier (on the screen) has a clear shot at the player.</summary>
    private bool Sees(Soldier s) =>
        Player.Alive && Player.Safe <= 0 && Phase == Phase.Play && s.Y < ViewTop + 10 && s.Y > Camera && s.X > 0 && s.X < Field.Width
        && Area.Clear(s.X, s.Y, Player.X, Player.Y);

    private void Shoot(Soldier s)
    {
        double a = Math.Atan2(Player.Y - s.Y, Player.X - s.X) + (_rng.NextDouble() - 0.5) * 2 * AimError;
        s.Facing = a;
        Bullets.Add(new Bullet { X = s.X + Math.Cos(a) * 12, Y = s.Y + Math.Sin(a) * 12, Vx = Math.Cos(a) * EnemyShotSpeed, Vy = Math.Sin(a) * EnemyShotSpeed, Life = 2.2, Hostile = true });
        Play(Sfx.EnemyFire);
    }

    /// <summary>A grenade lobbed at where the player is, give or take.</summary>
    private void Lob(Soldier s)
    {
        double tx = Player.X + (_rng.NextDouble() - 0.5) * 48, ty = Player.Y + (_rng.NextDouble() - 0.5) * 48;
        Grenades.Add(new Grenade { FromX = s.X, FromY = s.Y, ToX = tx, ToY = ty, Time = Math.Clamp(Field.Distance(s.X, s.Y, tx, ty) / 300, 0.6, 1.2), Hostile = true });
        Play(Sfx.Throw);
    }

    private void MovePrisoners(double dt)
    {
        foreach (var pr in Prisoners)
        {
            if (!Calm)
            {
                pr.X += pr.Vx * dt;
                pr.Walk += dt;
                if (pr.Free) pr.Y -= 40 * dt;
            }
            if (pr.X < -60 || pr.X > Field.Width + 60 || pr.Y < Camera - 40) pr.Gone = true;
            if (!pr.Free && !Soldiers.Any(s => s.Escort == pr && !s.Dead))
            {
                // Both guards down: he's free, and off to safety.
                pr.Free = true;
                pr.Vx = pr.X < Field.Width / 2 ? -90 : 90;
                Award(FreedPoints, pr.X, pr.Y);
                Effects.Add(new Effect { Kind = EffectKind.Word, X = pr.X, Y = pr.Y + 30, Text = "FREED", Life = 1.2, Colour = 0xFF8AFF8A });
                Play(Sfx.Freed);
            }
        }
        foreach (var pr in Prisoners.Where(p => p.Gone)) Soldiers.RemoveAll(s => s.Escort == pr);
        Prisoners.RemoveAll(p => p.Gone);
    }

    private void MoveBikes(double dt)
    {
        foreach (var b in Bikes)
        {
            if (Calm) continue;
            b.X += b.Vx * dt;
            if (!b.Thrown && Player.Alive && Phase == Phase.Play && Math.Abs(b.X - Player.X) < 90 && Player.Safe <= 0)
            {
                // The passenger drops a grenade on the way past.
                b.Thrown = true;
                Grenades.Add(new Grenade { FromX = b.X, FromY = b.Y, ToX = Player.X + b.Vx * 0.15, ToY = Player.Y, Time = 0.7, Hostile = true });
                Play(Sfx.Throw);
            }
        }
        Bikes.RemoveAll(b => b.Dead || b.X < -80 || b.X > Field.Width + 80);
    }

    /// <summary>Pillboxes on the screen fire a spread of three at the player when they can see him.</summary>
    private void Pillboxes_(double dt)
    {
        var p = Player;
        foreach (var b in Pillboxes)
        {
            if (b.Dead || Calm || b.Y < Camera || b.Y > ViewTop) continue;
            if ((b.FireIn -= dt) > 0 || !p.Alive || p.Safe > 0) continue;
            double a = Math.Atan2(p.Y - b.Y, p.X - b.X), sx = b.X + Math.Cos(a) * 38, sy = b.Y + Math.Sin(a) * 38;
            if (Field.Distance(b.X, b.Y, p.X, p.Y) > 420 || !Area.Clear(sx, sy, p.X, p.Y)) continue;
            b.FireIn = PillboxGap;
            foreach (double off in new[] { -0.2, 0, 0.2 })
                Bullets.Add(new Bullet { X = sx, Y = sy, Vx = Math.Cos(a + off) * EnemyShotSpeed, Vy = Math.Sin(a + off) * EnemyShotSpeed, Life = 2, Hostile = true });
            Play(Sfx.EnemyFire);
        }
    }

    // ───────────────────────────── bullets, grenades, and what they hit ─────────────────────────────

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            for (int k = 0; k < 2 && b.Life > 0; k++)
            {
                b.X += b.Vx * dt / 2;
                b.Y += b.Vy * dt / 2;
                if (b.X < -10 || b.X > Field.Width + 10 || b.Y < Camera - 10 || b.Y > ViewTop + 10) { b.Life = 0; break; }
                if (Ground.Stops(Area.AtPoint(b.X, b.Y))) { b.Life = 0; Puff(b.X, b.Y); break; }
                if (b.Hostile) HitPlayer(b);
                else HitEnemy(b);
            }
            b.Life -= dt;
        }
        Bullets.RemoveAll(b => b.Life <= 0);
    }

    private void HitEnemy(Bullet b)
    {
        foreach (var s in Soldiers)
        {
            if (s.Dead || Field.Distance(b.X, b.Y, s.X, s.Y) > Soldier.Radius + 2) continue;
            b.Life = 0;
            Down(s);
            return;
        }
        foreach (var k in Bikes)
        {
            if (k.Dead || Field.Distance(b.X, b.Y, k.X, k.Y) > Bike.Radius) continue;
            b.Life = 0;
            Wreck(k);
            return;
        }
    }

    private void Down(Soldier s)
    {
        s.Dead = true;
        Award(PointsFor(s.Kind), s.X, s.Y);
        Sparks(s.X, s.Y, 0xFFB0A080, 6, 0.5);
        Play(Sfx.EnemyDown);
    }

    private void Wreck(Bike k)
    {
        k.Dead = true;
        Award(BikePoints, k.X, k.Y);
        Blast(k.X, k.Y, 1.2);
    }

    private void HitPlayer(Bullet b)
    {
        var p = Player;
        if (!p.Alive || Field.Distance(b.X, b.Y, p.X, p.Y) > Commando.Radius) return;
        b.Life = 0;
        if (p.Safe > 0) return;
        Kill("shot");
    }

    private void MoveGrenades(double dt)
    {
        foreach (var g in Grenades)
        {
            g.T += dt;
            if (g.T < g.Time) continue;
            Blast(g.ToX, g.ToY, g.Hostile ? 0.8 : 1);
            if (g.Hostile)
            {
                if (Player.Alive && Player.Safe <= 0 && Phase == Phase.Play && Field.Distance(g.ToX, g.ToY, Player.X, Player.Y) < EnemyBlast) Kill("grenade");
                continue;
            }
            foreach (var s in Soldiers.Where(s => !s.Dead && Field.Distance(s.X, s.Y, g.ToX, g.ToY) < BlastRadius).ToList()) Down(s);
            foreach (var k in Bikes.Where(k => !k.Dead && Field.Distance(k.X, k.Y, g.ToX, g.ToY) < BlastRadius + 10).ToList()) Wreck(k);
            foreach (var b in Pillboxes.Where(b => !b.Dead && Math.Abs(b.X - g.ToX) < BlastRadius && Math.Abs(b.Y - g.ToY) < BlastRadius))
            {
                // Its walls come down: the cells it stood on are open ground again.
                b.Dead = true;
                for (int dc = 0; dc < 2; dc++) for (int dr = 0; dr < 2; dr++) Area.Rows[b.Row + dr][b.Col + dc] = Ground.Open;
                Award(PillboxPoints, b.X, b.Y);
                Sparks(b.X, b.Y, 0xFFA0A0A0, 14, 1);
                Play(Sfx.BunkerDown);
            }
        }
        Grenades.RemoveAll(g => g.T >= g.Time);
    }

    /// <summary>Running into a soldier or a bike is the end; a crate of grenades is picked up.</summary>
    private void Contact()
    {
        var p = Player;
        if (!p.Alive) return;
        foreach (var c in Crates.Where(c => Field.Distance(c.X, c.Y, p.X, p.Y) < 24).ToList())
        {
            Crates.Remove(c);
            p.Grenades += Crate.Grenades;
            Effects.Add(new Effect { Kind = EffectKind.Word, X = c.X, Y = c.Y + 24, Text = $"+{Crate.Grenades} GRENADES", Life = 1, Colour = 0xFFFFE14A });
            Play(Sfx.Pickup);
        }
        if (p.Safe > 0) return;
        if (Soldiers.Any(s => !s.Dead && Field.Distance(s.X, s.Y, p.X, p.Y) < Soldier.Radius + Commando.Radius - 2)) { Kill("run into"); return; }
        if (Bikes.Any(b => !b.Dead && Field.Distance(b.X, b.Y, p.X, p.Y) < Bike.Radius + Commando.Radius - 4)) Kill("run over");
    }

    /// <summary>The dead and whatever has fallen off the bottom of the screen go.</summary>
    private void Tidy()
    {
        Soldiers.RemoveAll(s => s.Dead || s.Y < Camera - 40 || (s.Kind == SoldierKind.Officer && (s.X < -30 || s.X > Field.Width + 30)));
        Crates.RemoveAll(c => c.Y < Camera - 30);
    }

    private void Kill(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        Lives--;
        Sparks(p.X, p.Y, 0xFFE0C090, 14, 1.2);
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = p.X, Y = p.Y, Life = 0.5, Size = 0.8 });
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Blast(double x, double y, double size)
    {
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = x, Y = y, Life = 0.55, Size = size });
        Sparks(x, y, 0xFFFFB04A, 8, 0.6);
        Play(Sfx.Blast);
    }

    private void Puff(double x, double y) => Effects.Add(new Effect { Kind = EffectKind.Puff, X = x, Y = y, Life = 0.3 });

    private void Clear()
    {
        Bonus = 2000 * Wave;
        Bullets.RemoveAll(b => b.Hostile);
        Grenades.RemoveAll(g => g.Hostile);
        Music(false);
        Play(Sfx.AreaClear);
        Say($"AREA {Wave} TAKEN", $"BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y + 16, Text = points.ToString(), Life = 0.7, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Sparks(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 30 + _rng.NextDouble() * 150;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vx *= 1 - 2 * dt;
            d.Vy *= 1 - 2 * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into an area, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    internal Soldier PutSoldier(SoldierKind kind, double x, double y)
    {
        var s = new Soldier { Kind = kind, X = x, Y = y, ToX = x, ToY = y, FireIn = 0 };
        Soldiers.Add(s);
        return s;
    }

    internal Bike PutBike(double x, double y, double vx)
    {
        var b = new Bike { X = x, Y = y, Vx = vx };
        Bikes.Add(b);
        return b;
    }

    internal Prisoner PutPrisoner(double x, double y, double vx)
    {
        var p = new Prisoner { X = x, Y = y, Vx = vx };
        Prisoners.Add(p);
        foreach (int side in new[] { -1, 1 })
            Soldiers.Add(new Soldier { Kind = SoldierKind.Guard, X = x + side * 30, Y = y, Escort = p, FireIn = 99 });
        return p;
    }

    internal void PutPlayer(double x, double y, int facing = 0)
    {
        Player.X = x;
        Player.Y = y;
        Player.Facing = facing;
        Player.Alive = true;
        Player.Safe = 0;
    }

    /// <summary>The screen moved straight to a point up the map (the player with it).</summary>
    internal void MoveCamera(double camera)
    {
        Camera = Math.Min(camera, Area.CameraEnd);
        Player.Y = Camera + 120;
        Area.Push(ref Player.X, ref Player.Y, Commando.Radius);
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Y < Camera) _spawnIndex++;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
