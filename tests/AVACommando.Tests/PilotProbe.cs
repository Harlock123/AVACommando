using AVACommando.Engine;

namespace AVACommando.Tests;

/// <summary>How the computer player gets on, area by area: the march to the gate + the fight there (set COMMANDO_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that just runs up the screen firing ahead).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("COMMANDO_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "commando-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0, gate = -1;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 6 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 240 < 120 ? 1 : -1, Dy: 1, Fire: true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (g.Rush && gate < 0) gate = t;
                if (g.Wave != wave) { waves.Add($"W{wave}:{(gate - start) / 60:0}+{(t - gate) / 60:0}s"); wave = g.Wave; start = t; gate = -1; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   lives {g.Lives} grenades {g.Player.Grenades} camera {g.Camera:0}/{g.Area.CameraEnd:0}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
