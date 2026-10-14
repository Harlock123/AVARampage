using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARampage.Engine;
using AVARampage.Views;

namespace AVARampage.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set RAMPAGE_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avarampage-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("RAMPAGE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARampage-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer play the player's monster too until something worth a picture is on screen.</summary>
    private static void PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 240)
    {
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(default);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        c.Advance(0.016);
    }

    private static void Rampage(GameEngine g, MonsterKind kind, int seed)
    {
        g.StartGame(kind);
        g.Player.Ai = true;
        g.Player.Brain = new MonsterBrain(seed);
    }

    [AvaloniaFact]
    public void The_title_the_choice_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.Tick(default, start: true);
        g.Tick(new Engine.Controls(1, 0));
        g.Idle(2);
        c.Advance(0.3);
        Shot(w, c, "select");
        g.ReturnToTitle();
        g.HighScores.Insert("RAM", 61250, 6);
        g.HighScores.Insert("AVA", 42000, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Rampaging_through_the_city()
    {
        var (w, c, g) = Open(5);
        Rampage(g, MonsterKind.Bruna, 5);
        g.Idle(70);
        Shot(w, c, "day-start");
        PlayUntil(g, c, g => g.Phase == Phase.Playing && g.PhaseTime > 12 && g.Units.Count >= 3 && g.Players.Count(m => m.State == MonsterState.Climbing) >= 2);
        Shot(w, c, "climbing");
        PlayUntil(g, c, g => g.Buildings.Any(b => b.Collapse is > 0.4 and < 1.2));
        Shot(w, c, "collapse");
        PlayUntil(g, c, g => g.Phase == Phase.DayClear && g.PhaseTime > 1);
        Shot(w, c, "day-clear");
        PlayUntil(g, c, g => g.Day == 2 && g.Phase == Phase.Playing && g.Units.Any(u => u.Kind == ArmyKind.Copter && u.X is > 100 and < 900) && g.Units.Any(u => u.Kind == ArmyKind.Tank));
        Shot(w, c, "army");
        PlayUntil(g, c, g => g.Day == 4 && g.Phase == Phase.Playing && g.PhaseTime > 15);
        Shot(w, c, "night");
    }

    [AvaloniaFact]
    public void A_monster_shrunk_back_to_a_person()
    {
        var (w, c, g) = Open(8);
        Rampage(g, MonsterKind.Zip, 8);
        PlayUntil(g, c, g => g.Players.Any(m => m.State == MonsterState.Human && m.StateTime > 0.3));
        Shot(w, c, "human");
    }

    [AvaloniaFact]
    public void Pause_and_game_over()
    {
        var (w, c, g) = Open(3);
        g.StartGame(MonsterKind.Krakk);
        g.Idle(60 * 4);
        g.Pause();
        Shot(w, c, "paused");
        g.Resume();
        g.Player.Lives = 1;
        g.Player.Health = 0.5;
        g.PutShot(g.Player.X - 100, g.Player.Chest.Y, 400, 0);
        g.Idle(60 * 4);
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
