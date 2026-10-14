using AVARampage.Engine;

namespace AVARampage.Tests;

/// <summary>How the computer monsters get on over five minutes (set RAMPAGE_PROBE to run it, for tuning).</summary>
public sealed class RampageProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_rampages()
    {
        if (Environment.GetEnvironmentVariable("RAMPAGE_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "rampage-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame((MonsterKind)(seed % 3));
            g.Player.Ai = true;
            g.Player.Brain = new MonsterBrain(seed);
            int shrinks = 0;
            var was = g.Player.State;
            int t = 0;
            for (; t < 60 * 300 && g.State == GameState.Playing; t++)
            {
                g.Tick(default);
                if (g.Player.State == MonsterState.Human && was != MonsterState.Human) shrinks++;
                was = g.Player.State;
            }
            output.WriteLine($"seed {seed}: {t / 60}s day {g.Day} shrinks {shrinks} health {g.Player.Health:0} " +
                             string.Join(" | ", g.Players.Select(m => $"{m.Kind} {m.Score}")) +
                             $" standing {g.Buildings.Count(b => b.Standing)}/{g.Buildings.Count} units {g.Units.Count}");
        }
    }
}
