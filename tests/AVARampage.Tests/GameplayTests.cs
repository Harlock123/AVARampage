using AVARampage.Engine;
using Engine = AVARampage.Engine;

namespace AVARampage.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avarampage-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game under way with the stomping-in skipped, no army and no rivals unless a test puts them there.</summary>
    private static GameEngine Game(MonsterKind kind = MonsterKind.Bruna, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame(kind);
        g.Go();
        g.Idle(1);
        return g;
    }

    private static Engine.Controls Press(int dx = 0, int dy = 0, bool jump = false, bool punch = false) => new(dx, dy, jump, punch);

    /// <summary>One punch: pressed for a tick, then released while it lands.</summary>
    private static void Punch(GameEngine g, int dy = 0)
    {
        g.Run(1, Press(dy: dy, punch: true));
        g.Idle((int)(GameEngine.PunchSeconds * 60) + 2);
    }

    /// <summary>Climbs the player up a building until its fist is level with the given floor.</summary>
    private static void ClimbTo(GameEngine g, int floor)
    {
        double want = Field.Ground - (floor + 0.5) * Building.FloorHeight + Monster.Height * 0.55;
        g.Run(1, Press(dy: -1));
        for (int i = 0; i < 600 && Math.Abs(g.Player.Y - want) > 3; i++) g.Run(1, Press(dy: Math.Sign(want - g.Player.Y)));
    }

    // ───────────────────────────── the city ─────────────────────────────

    [Fact]
    public void Every_city_fits_the_street_and_grows_with_the_days()
    {
        for (int day = 1; day <= 12; day++)
        {
            var city = City.Make(day, new Random(day));
            Assert.InRange(city.Count, 3, 6);
            for (int i = 0; i < city.Count; i++)
            {
                var b = city[i];
                Assert.True(b.X >= 0 && b.X + b.Width <= Field.Width);
                Assert.True(b.Top > Field.Top + 60, "the roof must be below the scores");
                if (i > 0) Assert.True(b.X > city[i - 1].X + city[i - 1].Width + 20, "buildings must not touch");
            }
        }
        double Floors(int day) => Enumerable.Range(0, 20).Average(s => City.Make(day, new Random(s)).Average(b => b.Floors));
        Assert.True(Floors(9) > Floors(1));
        Assert.Equal(City.NameOf(1), City.NameOf(1 + City.Names.Length));
    }

    [Fact]
    public void The_select_screen_chooses_the_monster_and_the_others_become_rivals()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Select, g.State);
        g.Tick(Press(dx: 1));
        g.Tick(default);
        g.Tick(Press(dx: 1));
        Assert.Equal(MonsterKind.Zip, g.Choice);
        g.Tick(default);
        g.Tick(Press(punch: true));
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(MonsterKind.Zip, g.Player.Kind);
        Assert.False(g.Player.Ai);
        Assert.Equal(3, g.Players.Count);
        Assert.Equal(3, g.Players.Select(m => m.Kind).Distinct().Count());
        Assert.All(g.Players.Skip(1), m => Assert.True(m.Ai));
    }

    [Fact]
    public void The_monsters_stomp_in_and_then_the_day_begins()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame(MonsterKind.Krakk);
        Assert.Equal(Phase.DayStart, g.Phase);
        Assert.True(g.Player.X < 0);
        g.Idle(60 * 3);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.InRange(g.Player.X, 120, 160);
    }

    // ───────────────────────────── climbing ─────────────────────────────

    [Fact]
    public void Up_at_a_wall_climbs_it_and_over_the_top_is_the_roof()
    {
        var g = Game();
        g.StandAt(0);
        g.Run(1, Press(dy: -1));
        Assert.Equal(MonsterState.Climbing, g.Player.State);
        Assert.Equal(0, g.Player.On);
        double y = g.Player.Y;
        g.Run(30, Press(dy: -1));
        Assert.True(g.Player.Y < y - 40);
        g.Run(60 * 8, Press(dy: -1));
        Assert.Equal(MonsterState.Roof, g.Player.State);
        Assert.Equal(g.Buildings[0].Top, g.Player.Y);
        g.Run(1, Press(dy: 1));
        Assert.Equal(MonsterState.Climbing, g.Player.State);
    }

    [Fact]
    public void Up_in_the_open_street_does_nothing_and_jump_leaps()
    {
        var g = Game();
        g.Player.X = (g.Buildings[0].X + g.Buildings[0].Width + g.Buildings[1].X) / 2;
        g.Run(10, Press(dy: -1));
        Assert.Equal(MonsterState.Standing, g.Player.State);
        g.Run(1, Press(jump: true));
        Assert.Equal(MonsterState.Air, g.Player.State);
        g.Idle(20);
        Assert.True(g.Player.Y < Field.Ground - 60);
        g.Idle(60);
        Assert.Equal(MonsterState.Standing, g.Player.State);
        Assert.Equal(Field.Ground, g.Player.Y);
    }

    [Fact]
    public void Climbing_off_the_side_of_a_building_falls()
    {
        var g = Game();
        g.StandAt(0, 0.1);
        ClimbTo(g, 4);
        g.Run(60, Press(dx: -1));
        Assert.Equal(-1, g.Player.On);
        g.Idle(90);
        Assert.Equal(MonsterState.Standing, g.Player.State);
        Assert.Equal(Field.Ground, g.Player.Y);
    }

    [Fact]
    public void A_leap_from_a_roof_lands_on_the_next_one()
    {
        var g = Game(seed: 11);
        var a = g.Buildings[0];
        var b = g.Buildings[1];
        b.Floors = a.Floors;
        b.Blocks = new Block[b.Columns, b.Floors];
        for (int c = 0; c < b.Columns; c++)
        for (int f = 0; f < b.Floors; f++)
            b.Blocks[c, f] = new Block();
        g.StandAt(0, 0.85);
        g.Run(60 * 8, Press(dy: -1));
        Assert.Equal(MonsterState.Roof, g.Player.State);
        g.Player.X = a.X + a.Width - 4;
        g.Run(1, Press(dx: 1, jump: true));
        for (int i = 0; i < 120 && g.Player.State == MonsterState.Air; i++) g.Run(1, Press(dx: 1));
        Assert.Equal(MonsterState.Roof, g.Player.State);
        Assert.Equal(1, g.Player.On);
    }

    // ───────────────────────────── punching ─────────────────────────────

    [Fact]
    public void A_punch_breaks_the_glass_then_the_wall_and_eats_whoever_is_inside()
    {
        var g = Game(MonsterKind.Krakk);
        g.StandAt(0, 0.1);
        ClimbTo(g, 3);
        g.Run(1, Press(dx: 1));
        var b = g.Buildings[0];
        var hit = Enumerable.Range(0, b.Columns).Select(c => b.Blocks[c, 3]).First(x => x.Damage == 0);
        foreach (var block in Enumerable.Range(0, b.Columns).Select(c => b.Blocks[c, 3])) block.Occupant = Occupant.None;
        hit.Occupant = Occupant.Person;
        g.Player.Health = 50;
        Punch(g);
        Assert.Equal(1, hit.Damage);
        Assert.Equal(10, g.Player.Score);
        Punch(g);
        Assert.Equal(Occupant.None, hit.Occupant);
        Assert.Equal(58, g.Player.Health);
        Assert.Equal(160, g.Player.Score);
        Punch(g);
        Assert.Equal(2, hit.Damage);
        Punch(g);
        Assert.Equal(3, hit.Damage);
    }

    [Fact]
    public void Bruna_punches_twice_as_hard()
    {
        var g = Game(MonsterKind.Bruna);
        g.StandAt(0, 0.1);
        ClimbTo(g, 2);
        g.Run(1, Press(dx: 1));
        var b = g.Buildings[0];
        foreach (var block in Enumerable.Range(0, b.Columns).Select(c => b.Blocks[c, 2])) block.Occupant = Occupant.None;
        Punch(g);
        Punch(g);
        Assert.Contains(Enumerable.Range(0, b.Columns), c => b.Blocks[c, 2].Damage == 3);
    }

    [Theory]
    [InlineData(Occupant.Bomb)]
    [InlineData(Occupant.Wire)]
    public void Bombs_and_live_wires_hurt(Occupant nasty)
    {
        var g = Game();
        g.StandAt(0, 0.1);
        ClimbTo(g, 3);
        g.Run(1, Press(dx: 1));
        var b = g.Buildings[0];
        foreach (var block in Enumerable.Range(0, b.Columns).Select(c => b.Blocks[c, 3])) block.Occupant = nasty;
        Punch(g);
        Punch(g);
        Assert.True(g.Player.Health < 100);
        if (nasty == Occupant.Wire) Assert.True(g.Player.Stun > 0);
        else Assert.Contains(g.Effects, e => e.Kind == EffectKind.Explosion);
    }

    [Fact]
    public void Money_scores_and_food_heals()
    {
        var g = Game();
        g.StandAt(0, 0.1);
        ClimbTo(g, 2);
        g.Run(1, Press(dx: 1));
        var b = g.Buildings[0];
        foreach (var block in Enumerable.Range(0, b.Columns).Select(c => b.Blocks[c, 2])) block.Occupant = Occupant.Money;
        Punch(g);
        int before = g.Player.Score;
        Punch(g);
        Assert.Equal(before + 500, g.Player.Score);
        foreach (var block in Enumerable.Range(0, b.Columns).Select(c => b.Blocks[c, 2])) block.Occupant = Occupant.Food;
        g.Player.Health = 40;
        Punch(g);
        Assert.Equal(60, g.Player.Health);
    }

    [Fact]
    public void Enough_damage_topples_a_building_and_whoever_is_on_it_falls()
    {
        var g = Game();
        var b = g.Buildings[0];
        int total = b.Columns * b.Floors;
        int n = 0;
        for (int f = 0; f < b.Floors && n < total * 0.55 - 1; f++)
        for (int c = 0; c < b.Columns && n < total * 0.55 - 1; c++, n++)
        {
            b.Blocks[c, f].Damage = 3;
        }
        // One more hole, punched by the player from the wall, brings it down.
        var last = Enumerable.Range(0, b.Floors).SelectMany(f => Enumerable.Range(0, b.Columns).Select(c => (c, f))).First(p => b.Blocks[p.c, p.f].Damage < 3);
        foreach (var block in b.Blocks) block.Occupant = Occupant.None;
        b.Blocks[last.c, last.f].Damage = 2;
        g.StandAt(0, (last.c + 0.5) / b.Columns);
        g.Player.X -= 30;
        ClimbTo(g, last.f);
        g.Run(1, Press(dx: 1));
        int score = g.Player.Score;
        Punch(g);
        Assert.False(b.Standing);
        Assert.True(g.Player.Score >= score + 500 + 100 * b.Floors);
        Assert.Equal(MonsterState.Air, g.Player.State);
        g.Idle(60 * 4);
        Assert.True(b.Gone);
    }

    [Fact]
    public void Flattening_the_whole_city_clears_the_day_with_a_bonus_and_moves_on()
    {
        var g = Game();
        foreach (var b in g.Buildings) b.Gone = true;
        int score = g.Player.Score;
        g.Idle(1);
        Assert.Equal(Phase.DayClear, g.Phase);
        Assert.Equal(score + 1000, g.Player.Score);
        g.Idle(60 * 5);
        Assert.Equal(2, g.Day);
        Assert.Equal(Phase.DayStart, g.Phase);
        Assert.All(g.Buildings, b => Assert.True(b.Standing));
    }

    // ───────────────────────────── the army ─────────────────────────────

    [Fact]
    public void Stamping_on_a_soldier_squashes_him()
    {
        var g = Game();
        g.Player.X = 500;
        var s = g.PutUnit(ArmyKind.Soldier, 530);
        g.Run(30, Press(dx: 1));
        Assert.True(s.Dead);
        Assert.True(g.Player.Score >= 150);
    }

    [Fact]
    public void A_helicopter_takes_two_punches()
    {
        var g = Game(MonsterKind.Zip);
        g.Player.X = 500;
        g.Player.Facing = 1;
        var copter = g.PutUnit(ArmyKind.Copter, 544, Field.Ground - Monster.Height * 0.55 + 15);
        Punch(g);
        Assert.False(copter.Dead);
        Assert.Equal(1, copter.Hits);
        Punch(g);
        Assert.DoesNotContain(copter, g.Units);
        Assert.Equal(500, g.Player.Score);
        Assert.Contains(g.Effects, e => e.Kind == EffectKind.Explosion);
    }

    [Fact]
    public void Shots_hurt_and_tank_shells_hurt_more()
    {
        var g = Game(MonsterKind.Bruna);
        g.Player.X = 500;
        g.PutShot(400, g.Player.Chest.Y, 400, 0);
        g.Idle(30);
        double bullet = 100 - g.Player.Health;
        Assert.True(bullet > 0);
        g.Player.Health = 100;
        g.Player.Hurt = 0;
        g.PutShot(400, g.Player.Chest.Y, 400, 0, ShotKind.Shell);
        g.Idle(30);
        Assert.True(100 - g.Player.Health > bullet * 2);
    }

    [Fact]
    public void Krakk_takes_less_harm_and_Zip_more()
    {
        double Harm(MonsterKind k)
        {
            var g = Game(k);
            g.Player.X = 500;
            g.PutShot(400, g.Player.Chest.Y, 400, 0, ShotKind.Shell);
            g.Idle(30);
            return 100 - g.Player.Health;
        }
        Assert.True(Harm(MonsterKind.Krakk) < Harm(MonsterKind.Bruna));
        Assert.True(Harm(MonsterKind.Zip) > Harm(MonsterKind.Bruna));
    }

    [Fact]
    public void A_soldier_in_a_window_fires_at_a_monster_close_by()
    {
        var g = Game();
        var b = g.Buildings[0];
        foreach (var block in g.Buildings.SelectMany(x => x.Blocks.Cast<Block>())) block.Occupant = Occupant.None;
        b.Blocks[b.Columns - 1, 2].Occupant = Occupant.Soldier;
        g.Player.X = b.X + b.Width + 80;
        g.Idle(60 * 2);
        Assert.True(g.Shots.Count > 0 || g.Player.Health < 100);
        // Punched out, he stops.
        b.Blocks[b.Columns - 1, 2].Occupant = Occupant.None;
        g.Shots.Clear();
        g.Player.Health = 100;
        g.Idle(60 * 4);
        Assert.Equal(100, g.Player.Health);
    }

    [Fact]
    public void The_army_turns_up_and_tanks_only_from_day_two()
    {
        var g = Game();
        g.Calm = false;
        g.Player.Hurt = 999;
        g.Idle(60 * 20);
        Assert.Contains(g.Units, u => u.Kind is ArmyKind.Soldier or ArmyKind.Police);
        Assert.DoesNotContain(g.Units, u => u.Kind == ArmyKind.Tank);
    }

    // ───────────────────────────── lives ─────────────────────────────

    [Fact]
    public void Out_of_health_the_monster_shrinks_to_a_person_runs_and_comes_back()
    {
        var g = Game();
        g.Player.X = 500;
        g.Player.Health = 2;
        g.PutShot(400, g.Player.Chest.Y, 400, 0);
        g.Idle(20);
        Assert.Equal(MonsterState.Human, g.Player.State);
        Assert.Equal(GameEngine.StartingLives - 1, g.Player.Lives);
        g.Idle(60 * 3);
        Assert.Equal(MonsterState.Away, g.Player.State);
        g.Idle(60 * 2);
        Assert.True(g.Player.Active);
        Assert.Equal(100, g.Player.Health);
    }

    [Fact]
    public void The_last_life_lost_ends_the_game_and_a_good_score_asks_for_initials()
    {
        var g = Game();
        g.Player.Lives = 1;
        g.Player.Score = 99999;
        g.Player.X = 500;
        g.Player.Health = 1;
        g.PutShot(400, g.Player.Chest.Y, 400, 0);
        g.Idle(60 * 4);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 3);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('R');
        g.Initials.Type('A');
        g.Initials.Type('M');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        Assert.Equal(new HighScoreEntry("RAM", 99999, 1), g.HighScores.Entries[0]);
    }

    [Fact]
    public void Punching_a_rival_stuns_it()
    {
        var g = Game();
        var rival = g.Players[1];
        rival.State = MonsterState.Standing;
        rival.X = 544;
        rival.Y = Field.Ground;
        g.Player.X = 500;
        g.Player.Facing = 1;
        Punch(g);
        Assert.True(rival.Stun > 0 || rival.Health < 100);
    }

    // ───────────────────────────── the computer ─────────────────────────────

    [Fact]
    public void The_computer_monster_flattens_a_city_on_its_own()
    {
        var g = Game(seed: 3);
        g.Player.Ai = true;
        g.Player.Brain = new MonsterBrain(3);
        for (int i = 0; i < 60 * 120 && g.Phase != Phase.DayClear; i++) g.Tick(default);
        Assert.Equal(Phase.DayClear, g.Phase);
    }

    [Fact]
    public void The_attract_mode_plays_itself_then_shows_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Players.Sum(m => m.Score) > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 2);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame(MonsterKind.Krakk);
            g.Player.Ai = true;
            g.Player.Brain = new MonsterBrain(9);
            g.Idle(60 * 40);
            return string.Join(",", g.Players.Select(m => $"{m.Score}:{m.X:0}"));
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void High_scores_persist_in_their_folder()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        t.Insert("BIG", 123456, 9);
        t.Save();
        Assert.Equal(new HighScoreEntry("BIG", 123456, 9), HighScoreTable.Load(folder).Entries[0]);
    }
}
