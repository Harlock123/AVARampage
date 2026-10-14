namespace AVARampage.Engine;

public enum GameState { Title, Select, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>What is happening in the day.</summary>
public enum Phase
{
    /// <summary>The city's name, and the monsters stomping in.</summary>
    DayStart,
    Playing,
    /// <summary>The last building is down: the day's tally.</summary>
    DayClear,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Punch, Smash, Glass, Crumble, Eat, Burp, Roar, Shot, Cannon, Explode, Hurt, Shock, Coin, Jump, Land,
    Shrink, Copter, Siren, Select, DayClear,
    /// <summary>The rampaging music, looping.</summary>
    Music, MusicStop,
    TitleTune, StartTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it
/// runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the
/// same game.
/// </summary>
public sealed partial class GameEngine
{
    public const int StartingLives = 3;
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;

    private Random _rng;
    private readonly int _seed;
    private double _accumulator;
    private bool _startPrev;
    private bool _titleTunePending = true;
    private bool _music;

    public GameEngine(int seed = 1986, HighScoreTable? table = null)
    {
        _seed = seed;
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        NewGame(MonsterKind.Bruna, attract: true);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.DayStart;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    /// <summary>The monster the player is choosing on the select screen.</summary>
    public MonsterKind Choice { get; private set; } = MonsterKind.Bruna;

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, double seconds = 2.5)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls controls, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(controls, start);
        }
    }

    private Controls _menuPrev;

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (startPressed) { State = GameState.Select; StateTime = 0; Play(Sfx.Select); }
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Select:
                if (c.Dx != 0 && _menuPrev.Dx == 0)
                {
                    Choice = (MonsterKind)(((int)Choice + c.Dx + 3) % 3);
                    Play(Sfx.Select);
                }
                if (startPressed || c.Punch && !_menuPrev.Punch) StartGame(Choice);
                else if (StateTime > 30) ReturnToTitle();
                break;
            case GameState.Attract:
                Simulate(dt, default);
                if (startPressed) { ReturnToTitle(); State = GameState.Select; break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 3)
                {
                    if (HighScores.Qualifies(Player.Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Player.Score, Day);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) { State = GameState.Select; StateTime = 0; }
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
        _menuPrev = c;
    }

    public void StartGame(MonsterKind kind)
    {
        NewGame(kind, attract: false);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.StartTune);
    }

    private void StartAttract()
    {
        NewGame((MonsterKind)_rng.Next(3), attract: true);
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _titleTunePending = true;
        Music(false);
        Effects.Clear();
        Message = null;
        NewGame(MonsterKind.Bruna, attract: true);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Playing) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
