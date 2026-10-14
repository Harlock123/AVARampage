namespace AVARampage.Engine;

/// <summary>
/// The computer's monster: it swats helicopters and stamps on soldiers that come close, picks a
/// building, climbs it, and works its way along the windows punching, eating whoever it finds
/// (and leaving the bombs and live wires alone if it has seen them). When a building starts to go,
/// it jumps clear. Used for the rival monsters, the attract mode and the tests.
/// </summary>
public sealed class MonsterBrain(int seed)
{
    private readonly Random _rng = new(seed);
    private bool _punchHeld;
    private int _target = -1;
    private (int Col, int Floor)? _cell;
    private double _think, _cellTime;
    private int _lastScore;
    private readonly HashSet<(int, int, int)> _skip = [];
    private List<Building>? _city;

    private Controls Punch(int dx, int dy)
    {
        _punchHeld = !_punchHeld;
        return new Controls(dx, dy, false, _punchHeld);
    }

    public Controls Decide(GameEngine g, Monster m)
    {
        if (!m.Active || m.Stun > 0) return default;
        if (!ReferenceEquals(_city, g.Buildings))
        {
            // A new day, a new city.
            _city = g.Buildings;
            _target = -1;
            _cell = null;
            _skip.Clear();
        }
        _think -= Field.Step;
        // A helicopter or a tank within reach: hit it.
        foreach (var u in g.Units)
        {
            if (u.Dead) continue;
            double dx = u.X - m.X, dy = u.Y - u.HeightOf / 2 - m.Chest.Y;
            if (Math.Abs(dx) < 70 && Math.Abs(dy) < 40 && u.Kind != ArmyKind.Soldier)
                return Math.Sign(dx) != m.Facing ? new Controls(Math.Sign(dx), 0) : Punch(0, 0);
            if (u.Kind == ArmyKind.Soldier && m.State is MonsterState.Standing or MonsterState.Walking && Math.Abs(dx) < 40)
                return Math.Sign(dx) != m.Facing && Math.Abs(dx) > 4 ? new Controls(Math.Sign(dx), 0) : Punch(0, 1);
        }
        // The building is going: get off.
        if (m.On >= 0 && !g.Buildings[m.On].Standing) return new Controls(m.Facing, 0, true);
        if (_target < 0 || _target >= g.Buildings.Count || !g.Buildings[_target].Standing || _think <= 0)
        {
            _think = 4;
            _target = PickBuilding(g, m);
            _cell = null;
        }
        if (_target < 0) return new Controls(m.X < Field.Width / 2 ? 1 : -1, 0);
        var b = g.Buildings[_target];
        // A window to work on: the nearest that isn't a hole yet (and isn't known trouble).
        if (_cell is not { } cell || b.Blocks[cell.Col, cell.Floor].Damage >= 3 || Bad(b.Blocks[cell.Col, cell.Floor]))
        {
            _cell = PickCell(g, b, m);
            _cellTime = 0;
            if (_cell is null) { _think = 0; _target = -1; return default; }
            cell = _cell.Value;
        }
        // Getting nowhere with this window: leave it and try another.
        _cellTime = m.Score != _lastScore ? 0 : _cellTime + Field.Step;
        _lastScore = m.Score;
        if (_cellTime > 4)
        {
            _skip.Add((_target, cell.Col, cell.Floor));
            _cell = null;
            return default;
        }
        double cx = b.X + (cell.Col + 0.5) * b.ColumnWidth;
        double cy = Field.Ground - (cell.Floor + 0.5) * Building.FloorHeight;
        // Stand beside it, facing it, with the fist at its height; the ground floor is punched
        // downwards from the street.
        int face = cx >= m.X ? 1 : -1;
        int dir = 0;
        double reach = 44;
        double wantY = cy + Monster.Height * 0.55;
        if (wantY > Field.Ground - 2)
        {
            dir = 1;
            reach = 22;
            wantY = Field.Ground;
        }
        double wantX = Math.Clamp(cx - face * reach, b.X + 16, b.X + b.Width - 16);
        if (Math.Abs(wantX - cx) < reach * 0.6) face = cx >= wantX ? 1 : -1;
        switch (m.State)
        {
            case MonsterState.Standing or MonsterState.Walking:
                if (dir > 0)
                {
                    if (Math.Abs(wantX - m.X) > 6) return new Controls(Math.Sign(wantX - m.X), 0);
                    if (m.Facing != face) return new Controls(face, 0);
                    return m.Punching ? default : Punch(0, 1);
                }
                if (m.X < b.X + 4 || m.X > b.X + b.Width - 4) return new Controls(Math.Sign(b.X + b.Width / 2 - m.X), 0);
                return new Controls(0, -1);
            case MonsterState.Roof:
                return new Controls(0, 1);
            case MonsterState.Air:
                return new Controls(Math.Sign(b.X + b.Width / 2 - m.X), -1);
        }
        // Climbing: to the ground floor's punch, climb down and step off.
        if (dir > 0)
        {
            if (m.Y < Field.Ground - 1) return new Controls(0, 1);
            return new Controls(0, 1);
        }
        int mx = Math.Abs(wantX - m.X) > 6 ? Math.Sign(wantX - m.X) : 0;
        int my = Math.Abs(wantY - m.Y) > 6 ? Math.Sign(wantY - m.Y) : 0;
        if (mx != 0 || my != 0) return new Controls(mx, my);
        if (m.Facing != face) return new Controls(face, 0);
        return m.Punching ? default : Punch(0, dir);
    }

    private static bool Bad(Block b) => b.Damage >= 1 && b.Occupant is Occupant.Bomb or Occupant.Wire;

    private int PickBuilding(GameEngine g, Monster m)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        for (int i = 0; i < g.Buildings.Count; i++)
        {
            var b = g.Buildings[i];
            if (!b.Standing) continue;
            // Near, and not already crowded with monsters.
            double crowd = g.Players.Count(o => o != m && o.Active && o.On == i) * 260;
            double score = Math.Abs(b.X + b.Width / 2 - m.X) + crowd + _rng.Next(80);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    private (int Col, int Floor)? PickCell(GameEngine g, Building b, Monster m)
    {
        (int, int)? best = null;
        double bestScore = double.MaxValue;
        for (int c = 0; c < b.Columns; c++)
        for (int f = 0; f < b.Floors; f++)
        {
            var block = b.Blocks[c, f];
            if (block.Damage >= 3 || Bad(block) || _skip.Contains((g.Buildings.IndexOf(b), c, f))) continue;
            double x = b.X + (c + 0.5) * b.ColumnWidth, y = Field.Ground - (f + 0.5) * Building.FloorHeight;
            double score = Math.Abs(x - m.X) + Math.Abs(y - m.Chest.Y) * 0.8 - (block.Occupant is Occupant.Person or Occupant.Food or Occupant.Money ? 60 : 0);
            if (score < bestScore) { bestScore = score; best = (c, f); }
        }
        return best;
    }
}
