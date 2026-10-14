namespace AVARampage.Engine;

public sealed partial class GameEngine
{
    public const double JumpSpeed = 600;
    public const double PunchSeconds = 0.22;

    /// <summary>The three monsters: the player's first, then the two rivals the computer plays.</summary>
    public List<Monster> Players { get; } = [];
    public Monster Player => Players[0];
    public List<Building> Buildings { get; private set; } = [];
    public List<Unit> Units { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Debris> Debris { get; } = [];
    public int Day { get; private set; } = 1;
    public string CityName => City.NameOf(Day);
    public int DayBonus { get; private set; }

    private double _armyTimer, _copterTimer;

    /// <summary>Test hook: no army arrives, and the rivals stay away.</summary>
    internal bool Calm { get; set; }

    private double Rand(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    private void NewGame(MonsterKind kind, bool attract)
    {
        _rng = new Random(_seed + (int)(StateTime * 997) + (int)kind);
        Players.Clear();
        Players.Add(new Monster { Kind = kind, Lives = StartingLives, Ai = attract });
        foreach (var other in Enum.GetValues<MonsterKind>().Where(k => k != kind))
            Players.Add(new Monster { Kind = other, Lives = 99, Ai = true });
        foreach (var m in Players.Where(m => m.Ai)) m.Brain = new MonsterBrain(_rng.Next());
        Effects.Clear();
        StartDay(1);
    }

    private void StartDay(int day)
    {
        Day = day;
        Buildings = City.Make(day, _rng);
        Units.Clear();
        Shots.Clear();
        Debris.Clear();
        _armyTimer = 3;
        _copterTimer = 18 - Math.Min(10, day * 2);
        for (int i = 0; i < Players.Count; i++)
        {
            var m = Players[i];
            if (m.State == MonsterState.Away && m.Lives <= 0 && !m.Ai) continue;
            m.State = MonsterState.Walking;
            m.Y = Field.Ground;
            m.Vx = m.Vy = 0;
            m.On = -1;
            m.X = i switch { 0 => -60, 1 => Field.Width + 60, _ => -140 };
            m.Facing = m.X < 0 ? 1 : -1;
            if (Calm && i > 0) m.State = MonsterState.Away;
        }
        SetPhase(Phase.DayStart);
        Say($"DAY {day} - {CityName}", 3);
    }

    internal void AddScore(Monster m, int points, double x, double y, bool popup = false)
    {
        m.Score += points;
        if (popup) Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 1, Colour = Monsters.Of(m.Kind).Belly });
    }

    // ───────────────────────────── the step ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.DayStart:
                // The monsters stomp in from the edges to their places.
                for (int i = 0; i < Players.Count; i++)
                {
                    var m = Players[i];
                    if (!m.Active) continue;
                    double spot = i switch { 0 => 140, 1 => Field.Width - 140, _ => Field.Width / 2 };
                    m.Facing = Math.Sign(spot - m.X) == 0 ? m.Facing : Math.Sign(spot - m.X);
                    if (Math.Abs(spot - m.X) > 4) { m.X += Math.Sign(spot - m.X) * m.Info.Speed * dt; m.WalkPhase += dt * 8; m.State = MonsterState.Walking; }
                    else m.State = MonsterState.Standing;
                }
                if (PhaseTime > 2.6)
                {
                    SetPhase(Phase.Playing);
                    Music(true);
                    Play(Sfx.Roar);
                }
                return;
            case Phase.DayClear:
                Settle(dt);
                if (PhaseTime > 4.5) StartDay(Day + 1);
                return;
        }
        foreach (var m in Players)
        {
            var controls = m.Ai ? m.Brain!.Decide(this, m) : c;
            Move(m, controls, dt);
        }
        if (!Calm) Army(dt);
        MoveUnits(dt);
        MoveShots(dt);
        Settle(dt);
        Windows(dt);
        if (Buildings.All(b => b.Gone)) ClearDay();
        if (Player.State == MonsterState.Away && Player.Lives <= 0 && !Player.Ai) GameOver();
        if (State == GameState.Attract && Player.State == MonsterState.Away && Player.Lives <= 0) GameOver();
    }

    private void ClearDay()
    {
        DayBonus = 1000 * Day;
        foreach (var m in Players.Where(m => m.Active)) m.Score += DayBonus;
        Units.Clear();
        Shots.Clear();
        Music(false);
        Play(Sfx.DayClear);
        Say("CITY FLATTENED!", 3);
        SetPhase(Phase.DayClear);
    }

    // ───────────────────────────── monsters ─────────────────────────────

    /// <summary>The building whose face is at x (with a little reach either side), if it still stands.</summary>
    private int FacadeAt(double x, double reach = 8)
    {
        for (int i = 0; i < Buildings.Count; i++)
        {
            var b = Buildings[i];
            if (b.Standing && x >= b.X - reach && x <= b.X + b.Width + reach) return i;
        }
        return -1;
    }

    private void Move(Monster m, Controls c, double dt)
    {
        m.StateTime += dt;
        if (m.PunchTime > 0) m.PunchTime -= dt;
        if (m.Hurt > 0) m.Hurt -= dt;
        if (m.EatTime > 0) m.EatTime -= dt;
        if (m.Stun > 0)
        {
            m.Stun -= dt;
            c = default;
        }
        var info = m.Info;
        bool jump = c.Jump && !m.JumpHeld, punch = c.Punch && !m.PunchHeld;
        m.JumpHeld = c.Jump;
        m.PunchHeld = c.Punch;
        // On the wall, a press the other way just turns to face it.
        bool turning = c.Dx != 0 && c.Dx != m.Facing;
        if (c.Dx != 0 && !m.Punching) m.Facing = c.Dx;
        switch (m.State)
        {
            case MonsterState.Human:
                // A little person again, running for the edge of the screen.
                m.X += (m.X < Field.Width / 2 ? -1 : 1) * 130 * dt;
                m.Facing = m.X < Field.Width / 2 ? -1 : 1;
                m.WalkPhase += dt * 14;
                if (m.StateTime > 2.6) { m.State = MonsterState.Away; m.StateTime = 0; }
                return;
            case MonsterState.Away:
                if (m.StateTime > (m.Ai ? 5 : 1.5) && (m.Lives > 0 || m.Ai) && !(Calm && m != Player)) Return(m);
                return;
            case MonsterState.Standing or MonsterState.Walking:
            {
                m.Vx = c.Dx * info.Speed;
                m.X = Math.Clamp(m.X + m.Vx * dt, 30, Field.Width - 30);
                m.State = c.Dx != 0 ? MonsterState.Walking : MonsterState.Standing;
                if (c.Dx != 0) m.WalkPhase += dt * 9;
                if (c.Dy < 0 && FacadeAt(m.X) is int b and >= 0) Grab(m, b);
                else if (jump) Leap(m, c.Dx * info.Speed * 0.8, -JumpSpeed);
                // Treading on soldiers.
                foreach (var u in Units.Where(u => u.Kind == ArmyKind.Soldier && !u.Dead && Math.Abs(u.X - m.X) < 22 && c.Dx != 0))
                    KillUnit(u, m, squashed: true);
                break;
            }
            case MonsterState.Climbing:
            {
                var b = Buildings[m.On];
                if (!b.Standing) { Fall(m); break; }
                double climb = info.Climb;
                m.Y = Math.Min(Field.Ground, m.Y + c.Dy * climb * dt);
                if (!turning) m.X += c.Dx * climb * 0.8 * dt;
                if (c.Dy != 0 || c.Dx != 0) m.WalkPhase += dt * 8;
                if (m.Y - Monster.Height * 0.35 < b.Top)
                {
                    m.State = MonsterState.Roof;
                    m.Y = b.Top;
                    break;
                }
                bool off = m.X < b.X + 8 || m.X > b.X + b.Width - 8;
                if (off)
                {
                    if (m.Y >= Field.Ground - 1) { m.State = MonsterState.Walking; m.On = -1; }
                    else Fall(m, c.Dx * 120);
                    break;
                }
                if (m.Y >= Field.Ground && c.Dy > 0) { m.State = MonsterState.Standing; m.On = -1; break; }
                if (jump) { Leap(m, m.Facing * 220, -JumpSpeed * 0.6); break; }
                break;
            }
            case MonsterState.Roof:
            {
                var b = Buildings[m.On];
                if (!b.Standing) { Fall(m); break; }
                m.X += c.Dx * info.Speed * 0.8 * dt;
                m.Y = b.Top;
                if (c.Dx != 0) m.WalkPhase += dt * 9;
                if (m.X < b.X - 4 || m.X > b.X + b.Width + 4) { Fall(m, c.Dx * 120); break; }
                if (c.Dy > 0) { m.State = MonsterState.Climbing; m.Y = b.Top + Monster.Height * 0.5; m.X = Math.Clamp(m.X, b.X + 14, b.X + b.Width - 14); break; }
                if (jump) Leap(m, c.Dx * info.Speed * 0.8, -JumpSpeed);
                break;
            }
            case MonsterState.Air:
            {
                double prevY = m.Y;
                m.Vy += Field.Gravity * dt;
                m.X = Math.Clamp(m.X + m.Vx * dt, 30, Field.Width - 30);
                m.Y += m.Vy * dt;
                if (c.Dx != 0) m.Vx += (c.Dx * info.Speed * 0.7 - m.Vx) * Math.Min(1, 3 * dt);
                // Catching hold of a building on the way past.
                if (c.Dy < 0 && FacadeAt(m.X, 0) is int b and >= 0 && m.Y > Buildings[b].Top + 20 && m.Y < Field.Ground - 10) { Grab(m, b); break; }
                // Landing on a roof.
                for (int i = 0; i < Buildings.Count; i++)
                {
                    var r = Buildings[i];
                    if (!r.Standing || m.Vy <= 0 || m.X < r.X || m.X > r.X + r.Width) continue;
                    if (prevY <= r.Top && m.Y >= r.Top)
                    {
                        m.State = MonsterState.Roof;
                        m.On = i;
                        m.Y = r.Top;
                        m.Vy = 0;
                        Play(Sfx.Land);
                        break;
                    }
                }
                if (m.State == MonsterState.Air && m.Y >= Field.Ground)
                {
                    m.Y = Field.Ground;
                    if (m.Vy > 1100) Damage(m, 6);
                    m.Vy = 0;
                    m.State = MonsterState.Standing;
                    m.On = -1;
                    Play(Sfx.Land);
                }
                break;
            }
        }
        if (punch && !m.Punching && m.Active) Punch(m, c.Dy);
    }

    private void Grab(Monster m, int building)
    {
        var b = Buildings[building];
        m.State = MonsterState.Climbing;
        m.On = building;
        m.X = Math.Clamp(m.X, b.X + 14, b.X + b.Width - 14);
        m.Vx = m.Vy = 0;
    }

    private void Leap(Monster m, double vx, double vy)
    {
        m.State = MonsterState.Air;
        m.On = -1;
        m.Vx = vx;
        m.Vy = vy;
        Play(Sfx.Jump);
    }

    private void Fall(Monster m, double vx = 0)
    {
        m.State = MonsterState.Air;
        m.On = -1;
        m.Vx = vx;
        m.Vy = 0;
    }

    /// <summary>A monster back for another go, stomping in from the nearest edge.</summary>
    private void Return(Monster m)
    {
        m.Health = 100;
        m.State = MonsterState.Walking;
        m.StateTime = 0;
        m.X = m.X < Field.Width / 2 ? 40 : Field.Width - 40;
        m.Y = Field.Ground;
        m.On = -1;
        m.Hurt = 1.5;
        Play(Sfx.Roar);
    }

    private void Damage(Monster m, double amount)
    {
        if (!m.Active || m.Hurt > 0.15) return;
        m.Health -= amount * m.Info.Toughness;
        m.Hurt = 0.25;
        Play(Sfx.Hurt);
        if (m.Health > 0) return;
        // Too hurt to go on: it shrinks back into a person and runs.
        m.Health = 0;
        m.State = MonsterState.Human;
        m.StateTime = 0;
        m.Y = Field.Ground;
        m.On = -1;
        Play(Sfx.Shrink);
        if (!m.Ai || State == GameState.Attract && m == Player) m.Lives--;
        if (m == Player && State == GameState.Playing) Say(m.Lives > 0 ? "ONLY HUMAN AGAIN..." : "THE CITY IS SAVED", 2.5);
    }

    // ───────────────────────────── punching ─────────────────────────────

    /// <summary>Where a punch lands: beside, above or below the monster.</summary>
    public static Vec PunchPoint(Monster m, int dir) => dir switch
    {
        < 0 => new Vec(m.X + m.Facing * 8, m.Y - Monster.Height - 14),
        > 0 => new Vec(m.X + m.Facing * 22, m.Y + (m.State == MonsterState.Climbing ? 18 : -6)),
        _ => new Vec(m.X + m.Facing * 44, m.Y - Monster.Height * 0.55),
    };

    private void Punch(Monster m, int dir)
    {
        m.PunchTime = PunchSeconds;
        m.PunchDir = dir;
        var at = PunchPoint(m, dir);
        // Another monster in the way takes it on the chin.
        foreach (var o in Players)
        {
            if (o == m || !o.Active || (o.Chest - at).Length > 40) continue;
            o.Stun = 1.0;
            Damage(o, 4);
            Play(Sfx.Punch);
            return;
        }
        // Soldiers, tanks, police cars, helicopters.
        foreach (var u in Units)
        {
            if (u.Dead || Math.Abs(u.X - at.X) > u.Width / 2 + 16 || Math.Abs(u.Y - u.HeightOf / 2 - at.Y) > u.HeightOf / 2 + 26) continue;
            u.Hits -= m.Info.Punch;
            u.Flash = 0.1;
            Play(Sfx.Punch);
            if (u.Hits <= 0) KillUnit(u, m, squashed: dir > 0 && u.Kind == ArmyKind.Soldier);
            return;
        }
        // The building: a sideways punch lands on the first window along its swing that isn't already a hole.
        int bi = -1;
        (int Col, int Floor)? cell = null;
        foreach (double r in dir == 0 ? new[] { 20.0, 32, 44 } : [0.0])
        {
            double x = dir == 0 ? m.X + m.Facing * r : at.X;
            int i = FacadeAt(x, 0);
            if (i < 0 || Buildings[i].BlockAt(x, at.Y) is not { } c) continue;
            bi = i;
            cell = c;
            if (Buildings[i].Blocks[c.Col, c.Floor].Damage < 3) break;
        }
        if (cell is null) { Play(Sfx.Punch); return; }
        var b = Buildings[bi];
        var block = b.Blocks[cell.Value.Col, cell.Value.Floor];
        double bx = b.X + (cell.Value.Col + 0.5) * b.ColumnWidth, by = Field.Ground - (cell.Value.Floor + 0.5) * Building.FloorHeight;
        b.LastHitBy = Players.IndexOf(m);
        if (block.Damage == 0)
        {
            block.Damage = 1;
            AddScore(m, 10, bx, by);
            Play(Sfx.Glass);
            Shatter(bx, by, 0xFFB8E0F0, 4);
            return;
        }
        if (block.Occupant != Occupant.None && block.Damage < 3)
        {
            Take(m, block, bx, by);
            return;
        }
        if (block.Damage >= 3) { Play(Sfx.Punch); return; }
        block.Damage = Math.Min(3, block.Damage + m.Info.Punch);
        AddScore(m, 25 * m.Info.Punch, bx, by);
        Play(Sfx.Smash);
        Shatter(bx, by, b.Colour, 6);
        if (b.Ruined >= b.Columns * b.Floors * 0.55) Topple(b, m);
    }

    /// <summary>A hand through the window: people and soldiers are eaten, food and money taken, bombs and live wires regretted.</summary>
    private void Take(Monster m, Block block, double x, double y)
    {
        var what = block.Occupant;
        block.Occupant = Occupant.None;
        switch (what)
        {
            case Occupant.Person:
                m.Health = Math.Min(100, m.Health + 8);
                m.EatTime = 0.5;
                AddScore(m, 150, x, y, popup: true);
                Play(Sfx.Eat);
                Effects.Add(new Effect { Kind = EffectKind.Chomp, X = x, Y = y, Life = 0.4 });
                break;
            case Occupant.Soldier:
                m.Health = Math.Min(100, m.Health + 5);
                m.EatTime = 0.5;
                AddScore(m, 200, x, y, popup: true);
                Play(Sfx.Eat);
                break;
            case Occupant.Food:
                m.Health = Math.Min(100, m.Health + 20);
                m.EatTime = 0.5;
                AddScore(m, 100, x, y, popup: true);
                Play(Sfx.Burp);
                break;
            case Occupant.Money:
                AddScore(m, 500, x, y, popup: true);
                Play(Sfx.Coin);
                break;
            case Occupant.Television:
                AddScore(m, 250, x, y, popup: true);
                Play(Sfx.Smash);
                break;
            case Occupant.Bomb:
                Effects.Add(new Effect { Kind = EffectKind.Explosion, X = x, Y = y, Life = 0.6 });
                Play(Sfx.Explode);
                Damage(m, 15);
                break;
            case Occupant.Wire:
                Effects.Add(new Effect { Kind = EffectKind.Spark, X = x, Y = y, Life = 0.5 });
                Play(Sfx.Shock);
                Damage(m, 10);
                m.Stun = 0.6;
                break;
        }
    }

    private void Shatter(double x, double y, uint colour, int n)
    {
        for (int i = 0; i < n; i++)
            Debris.Add(new Debris { X = x + Rand(-10, 10), Y = y + Rand(-10, 10), Vx = Rand(-160, 160), Vy = Rand(-260, -40), Spin = Rand(-8, 8), Colour = colour });
    }

    /// <summary>Too much of it is broken: the building comes down, and whoever broke it last gets the credit.</summary>
    private void Topple(Building b, Monster by)
    {
        if (b.Collapse > 0) return;
        b.Collapse = 0.001;
        int points = 500 + 100 * b.Floors;
        AddScore(by, points, b.X + b.Width / 2, b.Top + 30, popup: true);
        Play(Sfx.Crumble);
        // Whatever was in it is lost; whoever was on it falls.
        foreach (var m in Players.Where(m => m.On == Buildings.IndexOf(b) && m.State is MonsterState.Climbing or MonsterState.Roof)) Fall(m);
    }

    /// <summary>Collapsing buildings sink in a cloud of dust; debris falls.</summary>
    private void Settle(double dt)
    {
        foreach (var b in Buildings)
        {
            if (b.Collapse <= 0 || b.Gone) continue;
            b.Collapse += dt;
            if ((int)(b.Collapse * 10) % 2 == 0)
                Effects.Add(new Effect { Kind = EffectKind.Dust, X = b.X + Rand(0, b.Width), Y = Field.Ground - Rand(0, 30), Life = 1.0 });
            if (b.Sunk >= b.Floors * Building.FloorHeight) b.Gone = true;
        }
        foreach (var d in Debris)
        {
            d.Age += dt;
            d.Vy += Field.Gravity * dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            if (d.Y > Field.Ground + 10 || d.Age > 3) d.Dead = true;
        }
        Debris.RemoveAll(d => d.Dead);
    }

    // ───────────────────────────── the army ─────────────────────────────

    private void Army(double dt)
    {
        _armyTimer -= dt;
        _copterTimer -= dt;
        int soldiers = Units.Count(u => u.Kind == ArmyKind.Soldier), tanks = Units.Count(u => u.Kind == ArmyKind.Tank), police = Units.Count(u => u.Kind == ArmyKind.Police);
        if (_copterTimer <= 0 && Units.Count(u => u.Kind == ArmyKind.Copter) < 1 + Day / 3)
        {
            _copterTimer = Rand(14, 22) / (1 + Day * 0.1);
            int side = _rng.Next(2) == 0 ? -1 : 1;
            Units.Add(new Unit { Kind = ArmyKind.Copter, X = side < 0 ? -60 : Field.Width + 60, Y = Rand(180, 360), Facing = -side, Hits = 2, FireTimer = 2 });
            Play(Sfx.Copter);
        }
        if (_armyTimer > 0) return;
        _armyTimer = Rand(1.6, 3.2) / (1 + Day * 0.12);
        int from = _rng.Next(2) == 0 ? -1 : 1;
        double x = from < 0 ? -40 : Field.Width + 40;
        int r = _rng.Next(100);
        if (Day >= 2 && r < 20 && tanks < 1 + Day / 3)
            Units.Add(new Unit { Kind = ArmyKind.Tank, X = x - from * 30, Y = Field.Ground + 22, Facing = -from, Hits = 3, FireTimer = 2.5 });
        else if (r < 35 && police < 2 + Day / 4)
        {
            Units.Add(new Unit { Kind = ArmyKind.Police, X = x - from * 20, Y = Field.Ground + 18, Facing = -from, Hits = 2, FireTimer = 2 });
            Play(Sfx.Siren);
        }
        else if (soldiers < 4 + Day)
            Units.Add(new Unit { Kind = ArmyKind.Soldier, X = x, Y = Field.Ground + 22, Facing = -from, Hits = 1, FireTimer = Rand(1, 2) });
    }

    private Monster? Target(double x, double y)
    {
        Monster? best = null;
        double bestD = double.MaxValue;
        foreach (var m in Players)
        {
            if (!m.Active) continue;
            double d = Math.Abs(m.X - x) + Math.Abs(m.Chest.Y - y) * 0.5 + (m.Ai ? 80 : 0);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    private void Fire(double x, double y, Monster target, ShotKind kind, double speed, double spread = 0.08)
    {
        var aim = (target.Chest - new Vec(x, y)).Unit;
        double a = Math.Atan2(aim.Y, aim.X) + Rand(-spread, spread);
        Shots.Add(new Shot { Kind = kind, X = x, Y = y, Vx = Math.Cos(a) * speed, Vy = Math.Sin(a) * speed });
        Play(kind == ShotKind.Shell ? Sfx.Cannon : Sfx.Shot);
    }

    private void MoveUnits(double dt)
    {
        foreach (var u in Units)
        {
            u.Age += dt;
            if (u.Flash > 0) u.Flash -= dt;
            var t = Target(u.X, u.Y);
            switch (u.Kind)
            {
                case ArmyKind.Soldier:
                    if (t is not null && Math.Abs(t.X - u.X) > 170) { u.Facing = Math.Sign(t.X - u.X); u.X += u.Facing * 70 * dt; }
                    else if (t is not null)
                    {
                        u.Facing = Math.Sign(t.X - u.X);
                        u.FireTimer -= dt;
                        if (u.FireTimer <= 0) { u.FireTimer = Rand(1.6, 2.6) / (1 + Day * 0.08); Fire(u.X, u.Y - 18, t, ShotKind.Bullet, 380); }
                    }
                    break;
                case ArmyKind.Police:
                    if (t is not null && Math.Abs(t.X - u.X) > 260) u.X += u.Facing * 220 * dt;
                    else if (t is not null)
                    {
                        u.FireTimer -= dt;
                        if (u.FireTimer <= 0) { u.FireTimer = Rand(1.8, 2.6); Fire(u.X, u.Y - 20, t, ShotKind.Bullet, 380); }
                    }
                    break;
                case ArmyKind.Tank:
                    if (t is not null && Math.Abs(t.X - u.X) > 260) u.X += u.Facing * 70 * dt;
                    else if (t is not null)
                    {
                        u.Facing = Math.Sign(t.X - u.X);
                        u.FireTimer -= dt;
                        if (u.FireTimer <= 0) { u.FireTimer = Rand(2.6, 3.6) / (1 + Day * 0.06); Fire(u.X + u.Facing * 30, u.Y - 30, t, ShotKind.Shell, 330, 0.03); }
                    }
                    break;
                case ArmyKind.Copter:
                    if (u.Age > 22 || t is null)
                    {
                        u.X += u.Facing * 160 * dt;
                        u.Y -= 40 * dt;
                    }
                    else
                    {
                        double want = t.X - Math.Sign(t.X - u.X == 0 ? 1 : t.X - u.X) * 190;
                        u.Facing = Math.Sign(t.X - u.X);
                        u.X += Math.Clamp(want - u.X, -150 * dt, 150 * dt);
                        u.Y += Math.Clamp(t.Chest.Y - 40 - u.Y, -80 * dt, 80 * dt) + Math.Sin(u.Age * 3) * 20 * dt;
                        u.FireTimer -= dt;
                        if (u.FireTimer <= 0 && Math.Abs(t.X - u.X) < 280) { u.FireTimer = Rand(1.4, 2.0) / (1 + Day * 0.08); Fire(u.X + u.Facing * 30, u.Y, t, ShotKind.Bullet, 420); }
                    }
                    break;
            }
            if (u.X < -200 || u.X > Field.Width + 200) u.Dead = true;
        }
        Units.RemoveAll(u => u.Dead);
    }

    private void KillUnit(Unit u, Monster m, bool squashed = false)
    {
        u.Dead = true;
        int points = u.Kind switch { ArmyKind.Soldier => squashed ? 150 : 100, ArmyKind.Police => 300, ArmyKind.Tank => 500, _ => 500 };
        AddScore(m, points, u.X, u.Y - 30, popup: u.Kind != ArmyKind.Soldier);
        if (u.Kind == ArmyKind.Soldier)
        {
            if (squashed) { m.EatTime = 0.4; m.Health = Math.Min(100, m.Health + 3); Play(Sfx.Eat); }
            else Play(Sfx.Punch);
            return;
        }
        Effects.Add(new Effect { Kind = EffectKind.Explosion, X = u.X, Y = u.Y - u.HeightOf / 2, Life = 0.8 });
        Play(Sfx.Explode);
        if (u.Kind == ArmyKind.Copter) Shatter(u.X, u.Y, 0xFF4A5A3A, 8);
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.Age += dt;
            if (s.Kind == ShotKind.Shell) s.Vy += 120 * dt;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            if (s.X < -20 || s.X > Field.Width + 20 || s.Y < Field.Top - 20 || s.Y > Field.Ground + 10 || s.Age > 4) { s.Dead = true; continue; }
            foreach (var m in Players)
            {
                if (!m.Active || Math.Abs(s.X - m.X) > Monster.Width / 2 || s.Y < m.Y - Monster.Height || s.Y > m.Y) continue;
                s.Dead = true;
                Damage(m, s.Kind == ShotKind.Shell ? 10 : 3);
                if (s.Kind == ShotKind.Shell) Effects.Add(new Effect { Kind = EffectKind.Explosion, X = s.X, Y = s.Y, Life = 0.5 });
                break;
            }
        }
        Shots.RemoveAll(s => s.Dead);
    }

    /// <summary>Soldiers at the windows fire at a monster close by.</summary>
    private void Windows(double dt)
    {
        foreach (var b in Buildings)
        {
            if (!b.Standing) continue;
            for (int c = 0; c < b.Columns; c++)
            for (int f = 0; f < b.Floors; f++)
            {
                var block = b.Blocks[c, f];
                if (block.Occupant != Occupant.Soldier || block.Damage >= 3) continue;
                double x = b.X + (c + 0.5) * b.ColumnWidth, y = Field.Ground - (f + 0.5) * Building.FloorHeight;
                var t = Target(x, y);
                if (t is null || Math.Abs(t.X - x) > 240 || Math.Abs(t.Chest.Y - y) > 200) { block.Showing = 0; continue; }
                block.Showing += dt;
                block.ShotTimer -= dt;
                if (block.Showing > 0.8 && block.ShotTimer <= 0)
                {
                    block.ShotTimer = Rand(2.0, 3.0);
                    Fire(x, y, t, ShotKind.Bullet, 340);
                }
            }
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Test hook: skip the stomping in.</summary>
    internal void Go()
    {
        if (Phase != Phase.DayStart) return;
        for (int i = 0; i < Players.Count; i++)
        {
            var m = Players[i];
            if (Calm && i > 0) { m.State = MonsterState.Away; continue; }
            m.X = i switch { 0 => 140, 1 => Field.Width - 140, _ => Field.Width / 2 };
            m.State = MonsterState.Standing;
        }
        SetPhase(Phase.Playing);
        Message = null;
    }

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    internal Unit PutUnit(ArmyKind kind, double x, double y = Field.Ground + 22)
    {
        var u = new Unit { Kind = kind, X = x, Y = y, Hits = kind switch { ArmyKind.Tank => 3, ArmyKind.Copter => 2, ArmyKind.Police => 2, _ => 1 }, FireTimer = 99 };
        Units.Add(u);
        return u;
    }

    internal void PutShot(double x, double y, double vx, double vy, ShotKind kind = ShotKind.Bullet) =>
        Shots.Add(new Shot { Kind = kind, X = x, Y = y, Vx = vx, Vy = vy });

    /// <summary>Test hook: puts the player at the foot of a building's face.</summary>
    internal void StandAt(int building, double across = 0.5)
    {
        var b = Buildings[building];
        Player.X = b.X + b.Width * across;
        Player.Y = Field.Ground;
        Player.State = MonsterState.Standing;
        Player.On = -1;
    }
}
