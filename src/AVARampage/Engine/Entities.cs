namespace AVARampage.Engine;

/// <summary>The stick, jump and punch.</summary>
public record struct Controls(int Dx, int Dy, bool Jump = false, bool Punch = false);

public enum MonsterKind { Bruna, Krakk, Zip }

public enum MonsterState
{
    Standing,
    Walking,
    /// <summary>Clinging to a building's face.</summary>
    Climbing,
    /// <summary>On a rooftop.</summary>
    Roof,
    /// <summary>In the air: jumping or falling.</summary>
    Air,
    /// <summary>Turned back into a little person, running for cover.</summary>
    Human,
    /// <summary>Gone from the city until it comes back.</summary>
    Away,
}

/// <summary>Who each monster is: name, colour, and what it's good at.</summary>
public sealed record MonsterInfo(string Name, string What, uint Colour, uint Belly, double Speed, double Climb, int Punch, double Toughness, string Gift);

/// <summary>A giant monster loose in the city.</summary>
public sealed class Monster
{
    public MonsterKind Kind;
    public double X;
    /// <summary>Where its feet are.</summary>
    public double Y = Field.Ground;
    public double Vx, Vy;
    public MonsterState State;
    /// <summary>-1 facing left, +1 facing right.</summary>
    public int Facing = 1;
    public double Health = 100;
    public int Score;
    public int Lives;
    /// <summary>The building it's climbing or standing on, or -1.</summary>
    public int On = -1;
    public double PunchTime;
    /// <summary>Which way the punch went: 0 sideways, -1 up, +1 down.</summary>
    public int PunchDir;
    public bool PunchHeld;
    public bool JumpHeld;
    public double EatTime;
    public double Hurt;
    public double Stun;
    public double StateTime;
    public double WalkPhase;
    /// <summary>Played by the computer.</summary>
    public bool Ai;
    public MonsterBrain? Brain;

    public const double Height = 96, Width = 54;
    public MonsterInfo Info => Monsters.Of(Kind);
    public bool Active => State is not (MonsterState.Human or MonsterState.Away);
    public bool Punching => PunchTime > 0;
    public Vec Chest => new(X, Y - Height * 0.6);
}

public static class Monsters
{
    public static readonly MonsterInfo[] All =
    [
        new("BRUNA", "the great bear", 0xFF8A5A30, 0xFFD8B080, 150, 120, 2, 1.0, "PUNCHES HARDEST"),
        new("KRAKK", "the swamp crocodile", 0xFF4A8A3A, 0xFFC8D890, 140, 105, 1, 0.75, "TAKES THE MOST"),
        new("ZIP", "the sewer rat", 0xFF8A8A98, 0xFFE8B8C0, 175, 150, 1, 1.15, "CLIMBS FASTEST"),
    ];

    public static MonsterInfo Of(MonsterKind k) => All[(int)k];
}

/// <summary>What waits behind a window.</summary>
public enum Occupant { None, Person, Soldier, Food, Money, Bomb, Wire, Television }

/// <summary>One bay of a building: a window on one floor, and how badly it's been punched.</summary>
public sealed class Block
{
    /// <summary>0 whole, 1 the glass broken, 2 the wall cracked open, 3 a hole.</summary>
    public int Damage;
    public Occupant Occupant;
    /// <summary>Seconds the occupant has been showing (soldiers fire after a moment).</summary>
    public double Showing;
    public double ShotTimer = 1.5;
    public bool Lit;
}

/// <summary>A building on the street: columns of windows up its floors.</summary>
public sealed class Building
{
    public const double FloorHeight = 50;
    public double X;
    public double Width;
    public int Floors;
    public int Columns;
    public Block[,] Blocks = new Block[0, 0];
    public uint Colour;
    public int Style;
    /// <summary>Seconds into its collapse, or 0 while it stands.</summary>
    public double Collapse;
    public bool Gone;
    public int LastHitBy = -1;

    public double Top => Field.Ground - Floors * FloorHeight;
    public double ColumnWidth => Width / Columns;
    public bool Standing => Collapse <= 0 && !Gone;
    /// <summary>How far down it has sunk while collapsing.</summary>
    public double Sunk => Math.Min(Floors * FloorHeight, Collapse * Collapse * 140);

    public int Ruined
    {
        get
        {
            int n = 0;
            foreach (var b in Blocks) if (b.Damage >= 2) n++;
            return n;
        }
    }

    public (int Col, int Floor)? BlockAt(double x, double y)
    {
        if (x < X || x >= X + Width || y > Field.Ground || y < Top) return null;
        int col = Math.Clamp((int)((x - X) / ColumnWidth), 0, Columns - 1);
        int floor = Math.Clamp((int)((Field.Ground - y) / FloorHeight), 0, Floors - 1);
        return (col, floor);
    }
}

public enum ArmyKind { Soldier, Tank, Copter, Police }

/// <summary>The army and police: soldiers and cars on the street, tanks, helicopters overhead.</summary>
public sealed class Unit
{
    public ArmyKind Kind;
    public double X, Y;
    public double Vx, Vy;
    public int Facing = 1;
    public double FireTimer;
    public int Hits = 1;
    public double Age;
    public bool Dead;
    public double Flash;
    public double Width => Kind switch { ArmyKind.Tank => 76, ArmyKind.Copter => 80, ArmyKind.Police => 60, _ => 12 };
    public double HeightOf => Kind switch { ArmyKind.Tank => 34, ArmyKind.Copter => 30, ArmyKind.Police => 24, _ => 24 };
}

public enum ShotKind { Bullet, Shell }

public sealed class Shot
{
    public ShotKind Kind;
    public double X, Y, Vx, Vy;
    public bool Dead;
    public double Age;
}

/// <summary>Something falling from a broken building: rubble, or a dropped thing.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy;
    public double Spin;
    public double Age;
    public uint Colour;
    public bool Dead;
}

public enum EffectKind { Explosion, Dust, Score, Spark, Chomp }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y;
    public double Age;
    public double Life = 0.6;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
