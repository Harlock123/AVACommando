using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVACommando.Engine;
using AVACommando.Views;
using Controls = AVACommando.Engine.Controls;

namespace AVACommando.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set COMMANDO_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avacommando-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("COMMANDO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVACommando-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98500, 6);
        g.HighScores.Insert("AVA", 61200, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>An area, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 200)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Player.Alive && g.Player.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_beachhead() => Fight(1, "beachhead", g => g.Soldiers.Count(s => s.Y < g.ViewTop - 20) >= 3 && g.Bullets.Count(b => !b.Hostile) >= 1 && g.Bullets.Any(b => b.Hostile));

    [AvaloniaFact]
    public void A_pillbox_and_a_grenade() => Fight(1, "pillbox", g => g.Grenades.Any(n => !n.Hostile && n.T > n.Time * 0.5) && g.Pillboxes.Any(b => !b.Dead && b.Y > g.Camera + 100 && b.Y < g.ViewTop - 60));

    [AvaloniaFact]
    public void The_jungle_road() => Fight(2, "jungle", g => g.Camera > 600 && g.Soldiers.Count(s => s.Y < g.ViewTop - 20) >= 3 && g.Bullets.Count >= 2);

    [AvaloniaFact]
    public void A_prisoner_freed() => Fight(1, "prisoner", g => g.Prisoners.Any(p => p.Free && p.Y > g.Camera + 60), seconds: 300);

    [AvaloniaFact]
    public void The_river() => Fight(3, "river", g => g.Area.At(Field.ColOf(g.Player.X), Field.RowOf(g.Player.Y) + 3) == Ground.Water && g.Soldiers.Count >= 2, seconds: 300);

    [AvaloniaFact]
    public void The_fort_gate() => Fight(1, "gate", g => g.Rush && g.RushLeft < g.RushCount - 4 && g.Soldiers.Count(s => s.Kind == SoldierKind.Rusher) >= 3, seconds: 300);
}
