using AVACommando.Engine;

namespace AVACommando.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avacommando-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Area 1 in play with the first stretch of the map cleared to open ground and nobody about: calm (the enemy
    /// put there stands still and holds its fire) unless <paramref name="calm"/> is false.
    /// </summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        for (int r = 0; r < 40; r++) Array.Fill(g.Area.Rows[r], Ground.Open);
        g.Soldiers.Clear();
        g.Pillboxes.Clear();
        g.Crates.Clear();
        g.PutPlayer(512, 200);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true), GrenadeButton = new(Grenade: true);

    private static void Set(GameEngine g, int col, int row, char what) => g.Area.Rows[row][col] = what;

    // ───────────────────────────── the map ─────────────────────────────

    [Fact]
    public void Every_area_is_a_way_up_through_its_ground_to_the_gate_of_a_fort()
    {
        for (int n = 1; n <= 6; n++)
        {
            var a = Area.Build(n);
            Assert.All(a.Rows, row => Assert.Equal(Field.Cols, row.Length));
            Assert.Equal(Ground.Gate, a.At(Area.GateCol, a.GateRow));
            Assert.Equal(Ground.Wall, a.At(2, a.GateRow));
            Assert.Equal(Ground.Fort, a.At(Area.GateCol, a.GateRow + 2));
            // On foot, from the start to the gate.
            var seen = new HashSet<(int, int)> { (16, 3) };
            var queue = new Queue<(int C, int R)>([(16, 3)]);
            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (!a.Blocks(c + dc, r + dr) && r + dr >= 0 && seen.Add((c + dc, r + dr))) queue.Enqueue((c + dc, r + dr));
            }
            Assert.Contains((Area.GateCol, a.GateRow), seen);
            Assert.NotEmpty(a.Bunkers);
            Assert.Contains(a.Spawns, s => s.Kind == SpawnKind.Prisoner);
        }
        Assert.Equal(new string(Area.Build(2).Rows.SelectMany(r => r).ToArray()), new string(Area.Build(2).Rows.SelectMany(r => r).ToArray()));
        Assert.Equal(["THE BEACHHEAD", "THE JUNGLE ROAD", "THE RIVER FORT", "THE BEACHHEAD"], Enumerable.Range(1, 4).Select(n => Area.Build(n).Name));
    }

    // ───────────────────────────── running, firing and grenades ─────────────────────────────

    [Fact]
    public void He_runs_eight_ways_at_one_speed_and_faces_the_way_he_last_ran()
    {
        var g = Game();
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(512 + GameEngine.RunSpeed / 2, g.Player.X, 1);
        Assert.Equal(Field.WayOf(1, 0), g.Player.Facing);
        double x = g.Player.X, y = g.Player.Y;
        g.Run(30, new Controls(Dx: -1, Dy: 1));
        Assert.Equal(GameEngine.RunSpeed / 2, Field.Distance(x, y, g.Player.X, g.Player.Y), 1);
        Assert.Equal(Field.WayOf(-1, 1), g.Player.Facing);
        g.Idle(10);
        Assert.Equal(Field.WayOf(-1, 1), g.Player.Facing);
    }

    [Fact]
    public void Rocks_and_water_stop_him_but_a_bridge_does_not()
    {
        var g = Game();
        Set(g, 16, 9, Ground.Rock);
        g.PutPlayer(Field.CellX(16), 200);
        g.Run(60, new Controls(Dy: 1));
        Assert.Equal(9 * Field.Cell - Commando.Radius, g.Player.Y, 0);
        Array.Fill(g.Area.Rows[12], Ground.Water);
        Set(g, 20, 12, Ground.Bridge);
        g.PutPlayer(Field.CellX(18), 300);
        g.Run(90, new Controls(Dy: 1));
        Assert.True(g.Player.Y < 12 * Field.Cell);
        g.PutPlayer(Field.CellX(20), 300);
        g.Run(90, new Controls(Dy: 1));
        Assert.True(g.Player.Y > 13 * Field.Cell);
    }

    [Fact]
    public void He_fires_the_way_he_faces_three_at_most_and_not_far()
    {
        var g = Game();
        g.Run(1, new Controls(Dx: 1, Dy: 1, Fire: true));
        var b = Assert.Single(g.Bullets);
        Assert.Equal(b.Vx, b.Vy, 3);
        Assert.Equal(GameEngine.BulletSpeed, Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy), 3);
        int most = 0;
        for (int i = 0; i < 40; i++) { g.Run(1, FireButton); most = Math.Max(most, g.Bullets.Count); }
        Assert.Equal(GameEngine.MaxBullets, most);
        g.Idle((int)(GameEngine.BulletLife * 60) + 2);
        Assert.Empty(g.Bullets);
    }

    [Fact]
    public void Sandbags_stop_bullets_but_water_does_not()
    {
        var g = Game();
        Array.Fill(g.Area.Rows[9], Ground.Sandbag);
        g.PutPlayer(512, 200);
        var hidden = g.PutSoldier(SoldierKind.Rifleman, 512, 340);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.False(hidden.Dead);
        Array.Fill(g.Area.Rows[9], Ground.Water);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.True(hidden.Dead);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void A_grenade_goes_up_the_screen_over_a_wall_and_takes_out_whoever_is_where_it_lands()
    {
        var g = Game();
        Array.Fill(g.Area.Rows[9], Ground.Wall);
        var a = g.PutSoldier(SoldierKind.Rifleman, 512 + 20, 200 + GameEngine.ThrowRange);
        var b = g.PutSoldier(SoldierKind.Grenadier, 512 - 25, 200 + GameEngine.ThrowRange + 10);
        var far = g.PutSoldier(SoldierKind.Rifleman, 512 + 120, 200 + GameEngine.ThrowRange);
        int before = g.Player.Grenades;
        g.Run(1, new Controls(Dx: 1, Grenade: true));
        Assert.Equal(before - 1, g.Player.Grenades);
        var n = Assert.Single(g.Grenades);
        Assert.Equal(g.Player.Y + GameEngine.ThrowRange, n.ToY, 0);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.True(a.Dead && b.Dead);
        Assert.False(far.Dead);
        Assert.Equal(250, g.Score);
    }

    [Fact]
    public void Out_of_grenades_none_are_thrown_and_a_crate_gives_four()
    {
        var g = Game();
        g.Player.Grenades = 0;
        g.Run(30, GrenadeButton);
        Assert.Empty(g.Grenades);
        g.Crates.Add(new Crate { X = 512, Y = 260 });
        g.Run(30, new Controls(Dy: 1));
        Assert.Equal(Crate.Grenades, g.Player.Grenades);
        Assert.Empty(g.Crates);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_pillbox_shrugs_off_bullets_fires_a_spread_and_only_a_grenade_takes_it_out()
    {
        var g = Game(calm: false);
        g.Area.Rows[12][15] = g.Area.Rows[12][16] = g.Area.Rows[13][15] = g.Area.Rows[13][16] = Ground.Bunker;
        var box = new Pillbox { Col = 15, Row = 12, FireIn = 0 };
        g.Pillboxes.Add(box);
        g.PutPlayer(box.X, box.Y - GameEngine.ThrowRange);
        g.Player.Safe = 10;
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.False(box.Dead);
        g.Player.Safe = 0;
        g.Idle(2);
        Assert.Equal(3, g.Bullets.Count(b => b.Hostile));
        g.Bullets.Clear();
        g.Player.Safe = 10;
        g.Run(1, GrenadeButton);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.True(box.Dead);
        Assert.Equal(GameEngine.PillboxPoints, g.Score);
        Assert.False(g.Area.Blocks(15, 12));
    }

    [Fact]
    public void A_rifleman_runs_stops_aims_and_fires_and_a_bullet_costs_a_life()
    {
        var g = Game(calm: false);
        g.SetLives(3);
        g.PutSoldier(SoldierKind.Rifleman, 512, 480);
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 600) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("shot", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Player.Alive);
        Assert.True(g.Player.Safe > 0);
        Assert.DoesNotContain(g.Bullets, b => b.Hostile);
    }

    [Fact]
    public void A_grenadier_lobs_one_over_cover_onto_the_player()
    {
        var g = Game(calm: false);
        g.PutSoldier(SoldierKind.Grenadier, 512, 470).ToY = 470;
        int t = 0;
        while (g.Grenades.Count == 0 && t++ < 600) g.Idle(1);
        var n = Assert.Single(g.Grenades);
        Assert.True(n.Hostile);
        Assert.InRange(Field.Distance(n.ToX, n.ToY, g.Player.X, g.Player.Y), 0, 40);
        while (g.Phase == Phase.Play && t++ < 900) g.Idle(1);
        Assert.Equal("grenade", g.Cause);
    }

    [Fact]
    public void Running_into_a_soldier_or_a_sidecar_is_the_end_and_a_sidecar_can_be_shot()
    {
        var g = Game();
        g.PutSoldier(SoldierKind.Rifleman, 512, 214);
        g.Idle(1);
        Assert.Equal("run into", g.Cause);
        var h = Game();
        var bike = h.PutBike(300, 200, 0);
        h.Run(1, new Controls(Dx: -1, Fire: true));
        h.Idle(30);
        Assert.True(bike.Dead || !h.Bikes.Contains(bike));
        Assert.Equal(GameEngine.BikePoints, h.Score);
        var k = Game(calm: false);
        k.PutBike(-40, 200, 300);
        k.Player.Safe = 0.5;
        int t = 0;
        while (k.Phase == Phase.Play && t++ < 300) k.Idle(1);
        Assert.Contains(k.Cause, new[] { "run over", "grenade" });
    }

    [Fact]
    public void Shooting_both_guards_frees_their_prisoner_and_bullets_pass_him_by()
    {
        var g = Game();
        var p = g.PutPrisoner(512, 400, 0);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.False(p.Free);
        Assert.Equal(0, g.Score);
        foreach (var guard in g.Soldiers.Where(s => s.Escort == p).ToList())
        {
            g.PutPlayer(guard.X, 200);
            g.Run(1, FireButton);
            g.Idle(40);
        }
        Assert.True(p.Free);
        Assert.Equal(200 + GameEngine.FreedPoints, g.Score);
    }

    // ───────────────────────────── the screen, the gate, and the whole game ─────────────────────────────

    [Fact]
    public void The_screen_follows_him_up_but_never_back_down_and_he_cannot_run_off_the_bottom()
    {
        var g = Game();
        g.Run(120, new Controls(Dy: 1));
        double camera = g.Camera;
        Assert.True(camera > 100);
        Assert.Equal(g.Player.Y - (Field.Height - Field.Hud) / 2, camera, 1);
        g.Run(240, new Controls(Dy: -1));
        Assert.Equal(camera, g.Camera);
        Assert.Equal(camera + Commando.Radius + 4, g.Player.Y, 1);
    }

    [Fact]
    public void The_enemy_in_the_map_comes_on_as_the_screen_reaches_it()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        var first = g.Area.Spawns.Where(s => s.Kind is SpawnKind.Rifleman && s.Y > g.ViewTop + 100).OrderBy(s => s.Y).First();
        Assert.True(first.Y > g.ViewTop + 40);
        g.Player.Safe = 1000;
        for (int i = 0; i < 60 * 60 && g.ViewTop + 40 < first.Y; i++) g.Run(1, new Controls(Dy: 1));
        g.Run(1, new Controls(Dy: 1));
        Assert.Contains(g.Soldiers, s => s.Kind == SoldierKind.Rifleman && Field.Distance(s.X, s.Y, first.X, first.Y) < 20);
    }

    [Fact]
    public void At_the_fort_the_rush_comes_out_of_the_gate_then_the_officer_runs_and_the_area_is_taken()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        g.MoveCamera(g.Area.CameraEnd);
        g.Soldiers.Clear();
        g.Player.Safe = 1000;
        g.Idle(2);
        Assert.True(g.Rush);
        Assert.Equal(g.RushCount, g.RushLeft);
        int t = 0;
        while (!g.Soldiers.Any(s => s.Kind == SoldierKind.Rusher) && t++ < 200) g.Idle(1);
        var r = g.Soldiers.First(s => s.Kind == SoldierKind.Rusher);
        Assert.Equal(Ground.Gate, g.Area.AtPoint(r.X, r.Y));
        // Every rusher shot as he comes.
        for (t = 0; t < 60 * 60 && !g.OfficerOut; t++)
        {
            foreach (var s in g.Soldiers.Where(s => s.Kind == SoldierKind.Rusher).ToList()) g.Soldiers.Remove(s);
            g.Idle(1);
        }
        Assert.True(g.OfficerOut);
        var officer = Assert.Single(g.Soldiers, s => s.Kind == SoldierKind.Officer);
        officer.ToX = officer.X;
        g.PutPlayer(officer.X, officer.Y - 150);
        g.Player.Safe = 1000;
        int score = g.Score;
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Equal(score + GameEngine.OfficerPoints, g.Score);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(2000, g.Bonus);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal("THE JUNGLE ROAD", g.Area.Name);
        Assert.Equal(0, g.Camera);
    }

    [Fact]
    public void An_extra_life_every_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 50);
        g.PutSoldier(SoldierKind.Rifleman, 512, 400);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_life_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutSoldier(SoldierKind.Rifleman, 512, 214);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "POW") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("POW", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_tour_the_areas()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var names = new List<string>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            names.Add(g.Area.Name);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(["THE BEACHHEAD", "THE JUNGLE ROAD"], names);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, GameEngine.StartGrenades), (g.Wave, g.Lives, g.Player.Grenades));
    }

    [Fact]
    public void The_computer_player_takes_the_first_area()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        for (int i = 0; i < 60 * 300 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still in area {g.Wave}, at {g.Camera:0} of {g.Area.CameraEnd:0}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(2);
            for (int i = 0; i < 60 * 30; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Player.X:0.00} {g.Player.Y:0.00} {g.Camera:0.00} {g.Soldiers.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game(calm: false);
        g.Run(30, new Controls(Dy: 1));
        double y = g.Player.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal(y, g.Player.Y);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
