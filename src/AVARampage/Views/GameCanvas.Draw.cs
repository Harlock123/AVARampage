using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARampage.Engine;

namespace AVARampage.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Dictionary<(uint, double), Pen> RoundCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARampage/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    /// <summary>A thick pen with round ends, for limbs.</summary>
    private static Pen Limb(uint argb, double width)
    {
        if (!RoundCache.TryGetValue((argb, width), out var p))
            RoundCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Cyan = 0xFF4FD7FF, Dim = 0xFF9AA2B8, Orange = 0xFFFF9A2A, Green = 0xFF70E070;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xA0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Mix(uint a, uint b, double t)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t);
        return 0xFF000000 | (C(16) << 16) | (C(8) << 8) | C(0);
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, 1.5) : null, g);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawSky(dc);
        bool city = g.State is not (GameState.Title or GameState.Select or GameState.HighScores or GameState.EnterInitials);
        foreach (var b in g.Buildings) DrawBuilding(dc, b);
        DrawStreet(dc);
        if (city)
        {
            foreach (var u in g.Units) DrawUnit(dc, u);
            foreach (var m in g.Players) DrawMonster(dc, m);
            foreach (var s in g.Shots) DrawShot(dc, s);
            foreach (var d in g.Debris)
                using (dc.PushTransform(Matrix.CreateRotation(d.Age * d.Spin) * Matrix.CreateTranslation(d.X, d.Y)))
                    dc.FillRectangle(Brush(d.Colour), new Rect(-4, -3, 8, 6));
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.Select: DrawSelect(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver) Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
        {
            Text(dc, "DEMONSTRATION", new Point(Field.Width / 2, 140), 44, Yellow, center: true);
            Text(dc, "PRESS ENTER TO PLAY", new Point(Field.Width / 2, 178), 28, White, center: true);
        }
    }

    // ───────────────────────────── the city ─────────────────────────────

    /// <summary>The sky by the time of day (each day of rampage a different hour), and the far skyline.</summary>
    private void DrawSky(DrawingContext dc)
    {
        int day = Engine.Day;
        (uint top, uint low) = ((day - 1) % 4) switch
        {
            0 => (0xFF3A78C8u, 0xFFA8D8F0u),
            1 => (0xFF5A4A9Au, 0xFFF0A070u),
            2 => (0xFF1A2A5Au, 0xFFC86A5Au),
            _ => (0xFF060A20u, 0xFF1E2A4Au),
        };
        for (int i = 0; i < 24; i++)
            dc.FillRectangle(Brush(Mix(top, low, i / 23.0)), new Rect(0, i * 30, Field.Width, 31));
        bool night = (day - 1) % 4 == 3;
        if (night)
        {
            for (int i = 0; i < 60; i++) dc.FillRectangle(Brush(0xC0FFFFFF), new Rect(Hash(i, 1, 4) % 1024, Field.Top + Hash(i, 2, 4) % 300, 2, 2));
            dc.DrawEllipse(Brush(0xFFF0F0D8), null, new Point(820, 150), 30, 30);
        }
        else dc.DrawEllipse(Brush(day % 4 == 1 ? 0xFFFFF0B0 : 0xFFFFC070), null, new Point(860, 160), 40, 40);
        // The far skyline: blue-grey towers with a few lit windows.
        uint far = night ? 0xFF141A30 : Mix(low, 0xFF4A5A7A, 0.6);
        for (int i = 0; i < 26; i++)
        {
            uint h = Hash(i, day, 9);
            double w = 30 + h % 50, x = i * 42 - 20, tTop = 380 + (h >> 8) % 200;
            dc.FillRectangle(Brush(far), new Rect(x, tTop, w, Field.Ground - tTop));
            if (night)
                for (int k = 0; k < 6; k++)
                    if (Hash(i, k, 11) % 3 == 0) dc.FillRectangle(Brush(0xA0F0D070), new Rect(x + 6 + (Hash(i, k, 12) % (uint)(w - 12)), tTop + 10 + k * 22, 4, 6));
        }
    }

    private void DrawStreet(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF8A8A90), new Rect(0, Field.Ground, Field.Width, 12));
        dc.FillRectangle(Brush(0xFF3A3A42), new Rect(0, Field.Ground + 12, Field.Width, Field.Height - Field.Ground - 12));
        for (double x = 10; x < Field.Width; x += 80) dc.FillRectangle(Brush(0xFFE8D070), new Rect(x, Field.Ground + 40, 40, 4));
    }

    private void DrawBuilding(DrawingContext dc, Building b)
    {
        if (b.Gone) return;
        double sink = b.Sunk;
        double shake = b.Collapse > 0 ? Math.Sin(_clock * 60) * 3 : 0;
        using var clip = dc.PushClip(new Rect(0, 0, Field.Width, Field.Ground));
        using var move = dc.PushTransform(Matrix.CreateTranslation(shake, sink));
        double top = b.Top, w = b.Width;
        // Walls, a darker edge, and the roof's furniture.
        dc.FillRectangle(Brush(b.Colour), new Rect(b.X, top, w, b.Floors * Building.FloorHeight));
        dc.FillRectangle(Brush(Shade(b.Colour, 0.75)), new Rect(b.X + w - 6, top, 6, b.Floors * Building.FloorHeight));
        dc.FillRectangle(Brush(Shade(b.Colour, 0.6)), new Rect(b.X - 4, top - 8, w + 8, 10));
        switch (b.Style)
        {
            case 0:
                // A water tower.
                dc.FillRectangle(Brush(0xFF5A4A3A), new Rect(b.X + 16, top - 30, 4, 24));
                dc.FillRectangle(Brush(0xFF5A4A3A), new Rect(b.X + 42, top - 30, 4, 24));
                dc.DrawRectangle(Brush(0xFF8A6A4A), Pen(0xFF3A2A1A, 1.5), new Rect(b.X + 10, top - 58, 42, 30), 4, 4);
                break;
            case 1:
                // An antenna with a red light.
                dc.DrawLine(Pen(0xFF2A2A2A, 3), new Point(b.X + w - 30, top - 8), new Point(b.X + w - 30, top - 70));
                if ((int)(_clock * 2) % 2 == 0) dc.DrawEllipse(Brush(Red), null, new Point(b.X + w - 30, top - 72), 4, 4);
                break;
            default:
                // A billboard.
                dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(b.X + 20, top - 12, 4, 10));
                dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(b.X + w - 24, top - 12, 4, 10));
                dc.DrawRectangle(Brush(0xFFF0E0B0), Pen(0xFF3A2A1A, 2), new Rect(b.X + 10, top - 44, w - 20, 32), 2, 2);
                Text(dc, "EAT AT JO'S", new Point(b.X + w / 2, top - 28), 20, 0xFFC03020, center: true);
                break;
        }
        for (int c = 0; c < b.Columns; c++)
        for (int f = 0; f < b.Floors; f++)
        {
            var block = b.Blocks[c, f];
            double x = b.X + c * b.ColumnWidth, y = Field.Ground - (f + 1) * Building.FloorHeight;
            var win = new Rect(x + 9, y + 10, b.ColumnWidth - 18, Building.FloorHeight - 18);
            switch (block.Damage)
            {
                case 0:
                    dc.FillRectangle(Brush(block.Lit ? 0xFFF0D890 : 0xFF2A3A5A), win);
                    dc.FillRectangle(Brush(0x40FFFFFF), new Rect(win.X + 3, win.Y + 3, 5, win.Height - 6));
                    dc.DrawRectangle(Pen(Shade(b.Colour, 0.55), 2), win);
                    break;
                case 1:
                    dc.FillRectangle(Brush(0xFF1A1A22), win);
                    Poly(dc, 0xA0B8E0F0, null, new Point(win.X, win.Y), new Point(win.X + 8, win.Y), new Point(win.X, win.Y + 12));
                    Poly(dc, 0xA0B8E0F0, null, new Point(win.Right, win.Bottom), new Point(win.Right - 10, win.Bottom), new Point(win.Right, win.Bottom - 8));
                    dc.DrawRectangle(Pen(Shade(b.Colour, 0.55), 2), win);
                    break;
                case 2:
                {
                    var hole = win.Inflate(4);
                    dc.FillRectangle(Brush(0xFF14141A), hole);
                    uint h = Hash(c, f, 7);
                    dc.DrawLine(Pen(Shade(b.Colour, 0.5), 2), new Point(hole.X - 4, hole.Y - 2 + h % 8), new Point(hole.X + 6, hole.Y + 6));
                    dc.DrawLine(Pen(Shade(b.Colour, 0.5), 2), new Point(hole.Right + 4, hole.Bottom - h % 6), new Point(hole.Right - 6, hole.Bottom - 4));
                    break;
                }
                default:
                {
                    var hole = new Rect(x + 2, y + 2, b.ColumnWidth - 4, Building.FloorHeight - 4);
                    dc.FillRectangle(Brush(0xFF0C0C10), hole);
                    uint h = Hash(c, f, 3);
                    for (int k = 0; k < 4; k++)
                    {
                        double px = hole.X + (Hash(c, f + k, 5) % (uint)hole.Width);
                        Poly(dc, b.Colour, null, new Point(px - 6, hole.Y), new Point(px + 6, hole.Y), new Point(px, hole.Y + 6 + h % 8));
                    }
                    break;
                }
            }
            if (block.Damage < 3) DrawOccupant(dc, block, win);
        }
    }

    /// <summary>Who or what is at the window: people and soldiers can be seen through the glass, the rest only once it's broken.</summary>
    private void DrawOccupant(DrawingContext dc, Block block, Rect win)
    {
        var c = win.Center;
        switch (block.Occupant)
        {
            case Occupant.Person:
            {
                double wave = Math.Sin(_clock * 8 + c.X) * 4;
                dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(c.X, c.Y - 2), 6, 6);
                dc.FillRectangle(Brush(0xFF3A6AD8), new Rect(c.X - 6, c.Y + 4, 12, 10));
                dc.DrawLine(Limb(0xFFF0C090, 3), new Point(c.X - 6, c.Y + 6), new Point(c.X - 10, c.Y - 6 + wave));
                dc.DrawLine(Limb(0xFFF0C090, 3), new Point(c.X + 6, c.Y + 6), new Point(c.X + 10, c.Y - 6 - wave));
                break;
            }
            case Occupant.Soldier:
                dc.DrawEllipse(Brush(0xFFE0B080), null, new Point(c.X, c.Y - 1), 6, 6);
                Poly(dc, 0xFF4A5A2A, null, new Point(c.X - 8, c.Y - 3), new Point(c.X, c.Y - 11), new Point(c.X + 8, c.Y - 3));
                dc.FillRectangle(Brush(0xFF4A5A2A), new Rect(c.X - 6, c.Y + 5, 12, 10));
                if (block.Showing > 0.5) dc.DrawLine(Pen(0xFF202020, 3), new Point(c.X, c.Y + 6), new Point(c.X + 16, c.Y + 2));
                break;
            default:
                if (block.Damage == 0) return;
                switch (block.Occupant)
                {
                    case Occupant.Food:
                        dc.DrawEllipse(Brush(0xFFB0602A), Pen(0xFF5A2A10, 1.5), new Point(c.X - 2, c.Y), 9, 6);
                        dc.DrawLine(Limb(0xFFF0E8D8, 4), new Point(c.X + 5, c.Y + 2), new Point(c.X + 12, c.Y + 7));
                        break;
                    case Occupant.Money:
                        dc.DrawEllipse(Brush(0xFF6A9A3A), Pen(0xFF2A4A1A, 1.5), new Point(c.X, c.Y + 2), 9, 8);
                        Text(dc, "$", new Point(c.X, c.Y + 1), 18, Yellow, center: true);
                        break;
                    case Occupant.Television:
                        dc.DrawRectangle(Brush(0xFF3A3A3A), Pen(0xFF101010, 1.5), new Rect(c.X - 11, c.Y - 7, 22, 16), 2, 2);
                        dc.FillRectangle(Brush((int)(_clock * 6) % 2 == 0 ? 0xFF7AC0F0 : 0xFF9AD0F0), new Rect(c.X - 8, c.Y - 4, 16, 10));
                        break;
                    case Occupant.Bomb:
                        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(c.X, c.Y + 2), 8, 8);
                        dc.DrawLine(Pen(0xFF8A6A3A, 2), new Point(c.X + 4, c.Y - 5), new Point(c.X + 8, c.Y - 10));
                        if ((int)(_clock * 8) % 2 == 0) dc.DrawEllipse(Brush(Orange), null, new Point(c.X + 9, c.Y - 11), 3, 3);
                        break;
                    case Occupant.Wire:
                        Poly(dc, 0, Yellow, new Point(c.X - 10, c.Y - 8), new Point(c.X + 2, c.Y - 2), new Point(c.X - 4, c.Y + 2), new Point(c.X + 10, c.Y + 9));
                        if ((int)(_clock * 10) % 2 == 0) dc.DrawEllipse(Brush(0x80F0F0FF), null, c, 12, 12);
                        break;
                }
                break;
        }
    }

    // ───────────────────────────── monsters ─────────────────────────────

    private void DrawMonster(DrawingContext dc, Monster m)
    {
        if (m.State == MonsterState.Away) return;
        if (m.State == MonsterState.Human)
        {
            DrawHuman(dc, m);
            return;
        }
        if (m.Hurt > 0 && (int)(_clock * 20) % 2 == 0) return;
        var info = m.Info;
        uint body = m.Stun > 0 ? Mix(info.Colour, 0xFFFFFFFF, 0.3) : info.Colour, dark = Shade(body, 0.7), belly = info.Belly;
        using var place = dc.PushTransform(Matrix.CreateScale(m.Facing, 1) * Matrix.CreateTranslation(m.X, m.Y));
        double swing = Math.Sin(m.WalkPhase) * 10;
        bool climbing = m.State == MonsterState.Climbing;
        bool air = m.State == MonsterState.Air;
        // Tails first, behind the body.
        if (m.Kind == MonsterKind.Krakk)
            Poly(dc, dark, 0xFF101010, new Point(-14, -40), new Point(-58, -6 + swing * 0.3), new Point(-16, -22));
        if (m.Kind == MonsterKind.Zip)
            dc.DrawLine(Limb(0xFFE8B8C0, 4), new Point(-16, -32), new Point(-50, -10 + swing * 0.5));
        // Legs.
        double l1 = climbing ? swing * 0.5 : air ? -8 : swing, l2 = climbing ? -swing * 0.5 : air ? 10 : -swing;
        dc.DrawLine(Limb(dark, 14), new Point(-8, -36), new Point(-8 + l1, -2));
        dc.DrawLine(Limb(dark, 14), new Point(8, -36), new Point(8 + l2, -2));
        dc.DrawEllipse(Brush(Shade(dark, 0.8)), null, new Point(-6 + l1, -2), 10, 5);
        dc.DrawEllipse(Brush(Shade(dark, 0.8)), null, new Point(10 + l2, -2), 10, 5);
        // The back arm.
        var shoulder = new Point(4, -66);
        Point back = climbing ? new Point(-4, -100 - swing) : new Point(-10 - swing * 0.6, -36);
        dc.DrawLine(Limb(dark, 12), shoulder, back);
        // Body and belly.
        dc.DrawEllipse(Brush(body), Pen(0xFF101010, 2), new Point(0, -54), 25, 29);
        dc.DrawEllipse(Brush(belly), null, new Point(8, -50), 13, 19);
        // Head.
        switch (m.Kind)
        {
            case MonsterKind.Bruna:
                dc.DrawEllipse(Brush(body), Pen(0xFF101010, 2), new Point(-6, -100), 7, 7);
                dc.DrawEllipse(Brush(body), Pen(0xFF101010, 2), new Point(16, -100), 7, 7);
                dc.DrawEllipse(Brush(body), Pen(0xFF101010, 2), new Point(6, -86), 19, 17);
                dc.DrawEllipse(Brush(belly), null, new Point(18, -80), 10, 8);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(26, -82), 4, 3);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(12, -92), 3, 3);
                break;
            case MonsterKind.Krakk:
                dc.DrawEllipse(Brush(body), Pen(0xFF101010, 2), new Point(2, -86), 16, 15);
                dc.DrawRectangle(Brush(body), Pen(0xFF101010, 2), new Rect(6, -90, 34, 14), 6, 6);
                for (int t = 0; t < 4; t++) Poly(dc, 0xFFF8F8F0, null, new Point(12 + t * 7, -77), new Point(15 + t * 7, -71), new Point(18 + t * 7, -77));
                dc.DrawEllipse(Brush(0xFFF0E040), Pen(0xFF101010, 1.5), new Point(6, -98), 5, 5);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(7, -98), 2, 3);
                break;
            default:
                dc.DrawEllipse(Brush(0xFFE8B8C0), Pen(0xFF101010, 2), new Point(-8, -102), 9, 9);
                dc.DrawEllipse(Brush(body), Pen(0xFF101010, 2), new Point(4, -86), 16, 14);
                Poly(dc, body, 0xFF101010, new Point(14, -94), new Point(36, -84), new Point(14, -76));
                dc.DrawEllipse(Brush(0xFFF080A0), null, new Point(35, -84), 4, 4);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(10, -92), 3, 3);
                dc.DrawLine(Pen(0xFF202020, 1), new Point(30, -82), new Point(44, -78));
                dc.DrawLine(Pen(0xFF202020, 1), new Point(30, -86), new Point(44, -90));
                break;
        }
        // The front arm: a punch, a hand to the mouth, a grip on the building, or a swing.
        Point fist;
        if (m.Punching)
        {
            double t = 1 - m.PunchTime / GameEngine.PunchSeconds;
            double reach = Math.Sin(Math.PI * Math.Min(1, t * 1.6));
            fist = m.PunchDir switch
            {
                < 0 => new Point(6, -66 - 54 * reach),
                > 0 => new Point(16 + 6 * reach, -60 + 64 * reach),
                _ => new Point(4 + 46 * reach, -60),
            };
        }
        else if (m.EatTime > 0) fist = new Point(22, -80);
        else if (climbing) fist = new Point(14, -98 + swing);
        else fist = new Point(12 + swing * 0.6, -34);
        dc.DrawLine(Limb(body, 13), shoulder, fist);
        dc.DrawEllipse(Brush(dark), Pen(0xFF101010, 1.5), fist, 9, 9);
    }

    /// <summary>A monster turned back into a small, embarrassed person, running.</summary>
    private void DrawHuman(DrawingContext dc, Monster m)
    {
        using var place = dc.PushTransform(Matrix.CreateScale(m.Facing, 1) * Matrix.CreateTranslation(m.X, m.Y));
        double s = Math.Sin(m.WalkPhase) * 6;
        dc.DrawLine(Limb(0xFFF0C090, 4), new Point(0, -16), new Point(-4 + s, 0));
        dc.DrawLine(Limb(0xFFF0C090, 4), new Point(0, -16), new Point(4 - s, 0));
        dc.FillRectangle(Brush(Monsters.Of(m.Kind).Colour), new Rect(-5, -20, 10, 7));
        dc.DrawLine(Limb(0xFFF0C090, 5), new Point(0, -32), new Point(0, -16));
        dc.DrawLine(Limb(0xFFF0C090, 3), new Point(0, -28), new Point(8, -36 + s));
        dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(0, -38), 6, 6);
    }

    // ───────────────────────────── the army ─────────────────────────────

    private void DrawUnit(DrawingContext dc, Unit u)
    {
        uint flash = u.Flash > 0 ? 0xFFFFFFFF : 0;
        using var place = dc.PushTransform(Matrix.CreateScale(u.Facing, 1) * Matrix.CreateTranslation(u.X, u.Y));
        switch (u.Kind)
        {
            case ArmyKind.Soldier:
            {
                double s = Math.Sin(u.Age * 10) * 4;
                dc.DrawLine(Limb(0xFF3A4A20, 4), new Point(0, -12), new Point(-3 + s, 0));
                dc.DrawLine(Limb(0xFF3A4A20, 4), new Point(0, -12), new Point(3 - s, 0));
                dc.FillRectangle(Brush(flash != 0 ? flash : 0xFF4A5A2A), new Rect(-4, -24, 8, 13));
                dc.DrawEllipse(Brush(0xFFE0B080), null, new Point(0, -28), 4, 4);
                Poly(dc, 0xFF3A4A20, null, new Point(-6, -29), new Point(0, -35), new Point(6, -29));
                dc.DrawLine(Pen(0xFF202020, 2.5), new Point(0, -20), new Point(12, -30));
                break;
            }
            case ArmyKind.Police:
                dc.DrawRectangle(Brush(flash != 0 ? flash : 0xFFF0F0F0), Pen(0xFF101010, 2), new Rect(-30, -20, 60, 16), 5, 5);
                dc.FillRectangle(Brush(0xFF2A4AA8), new Rect(-30, -14, 60, 5));
                dc.DrawRectangle(Brush(0xFF6A9AC8), Pen(0xFF101010, 1.5), new Rect(-12, -30, 26, 11), 3, 3);
                dc.FillRectangle(Brush((int)(_clock * 6) % 2 == 0 ? Red : 0xFF3A6AF0), new Rect(-4, -34, 8, 4));
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(-18, -3), 6, 6);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(18, -3), 6, 6);
                break;
            case ArmyKind.Tank:
                dc.DrawRectangle(Brush(0xFF2A2A22), Pen(0xFF101010, 2), new Rect(-38, -12, 76, 12), 6, 6);
                dc.DrawRectangle(Brush(flash != 0 ? flash : 0xFF5A6A30), Pen(0xFF101010, 2), new Rect(-32, -26, 64, 15), 3, 3);
                dc.DrawRectangle(Brush(flash != 0 ? flash : 0xFF6A7A3A), Pen(0xFF101010, 2), new Rect(-14, -36, 28, 12), 4, 4);
                dc.DrawLine(Limb(0xFF3A4A20, 5), new Point(10, -32), new Point(40, -46));
                for (int k = 0; k < 5; k++) dc.DrawEllipse(Brush(0xFF4A4A40), null, new Point(-28 + k * 14, -6), 5, 5);
                break;
            case ArmyKind.Copter:
            {
                uint body = flash != 0 ? flash : 0xFF4A5A3A;
                dc.DrawLine(Limb(body, 6), new Point(-14, -14), new Point(-46, -18));
                dc.FillRectangle(Brush(body), new Rect(-50, -26, 6, 12));
                dc.DrawRectangle(Brush(body), Pen(0xFF101010, 2), new Rect(-20, -26, 44, 24), 12, 12);
                dc.DrawEllipse(Brush(0xFF7AC0E8), null, new Point(14, -15), 8, 7);
                dc.FillRectangle(Brush(0xFF202020), new Rect(-16, -2, 32, 3));
                double a = Math.Abs(Math.Cos(_clock * 30));
                dc.DrawLine(Pen(0xB0202020, 3), new Point(-40 * a, -30), new Point(40 * a, -30));
                break;
            }
        }
    }

    private static void DrawShot(DrawingContext dc, Shot s)
    {
        if (s.Kind == ShotKind.Shell)
        {
            dc.DrawEllipse(Brush(0xFF202020), Pen(0xFFFFA040, 1.5), new Point(s.X, s.Y), 5, 5);
            dc.DrawLine(Pen(0x80FFFFFF, 3), new Point(s.X, s.Y), new Point(s.X - s.Vx * 0.05, s.Y - s.Vy * 0.05));
        }
        else dc.DrawEllipse(Brush(0xFFFFE070), null, new Point(s.X, s.Y), 3, 3);
    }

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        var at = new Point(e.X, e.Y);
        double t = e.Age / e.Life;
        uint a = (uint)((1 - t) * 255) << 24;
        switch (e.Kind)
        {
            case EffectKind.Explosion:
            {
                double r = 40 * Math.Sqrt(t);
                dc.DrawEllipse(Brush(a | 0x00FF6020), null, at, r, r);
                dc.DrawEllipse(Brush(a | 0x00FFE060), null, at, r * 0.55, r * 0.55);
                break;
            }
            case EffectKind.Dust:
                dc.DrawEllipse(Brush((uint)((1 - t) * 0xB0) << 24 | 0x00B0A898), null, new Point(at.X, at.Y - t * 40), 16 + t * 30, 12 + t * 20);
                break;
            case EffectKind.Spark:
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * 1.05 + _clock * 20;
                    dc.DrawLine(Pen(a | 0x00FFFF80, 2), at, new Point(at.X + Math.Cos(ang) * 18, at.Y + Math.Sin(ang) * 18));
                }
                break;
            case EffectKind.Chomp:
                Text(dc, "CHOMP!", new Point(at.X, at.Y - t * 20), 24, White, center: true);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(at.X, at.Y - t * 36), 26, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── words ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xE0080A12), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFF3A4A5A), new Rect(0, Field.Top - 3, Field.Width, 3));
        bool showing = g.State is not (GameState.Title or GameState.Select or GameState.HighScores or GameState.EnterInitials);
        for (int i = 0; i < g.Players.Count; i++)
        {
            var m = g.Players[i];
            var info = m.Info;
            double x = 14 + i * 296;
            Text(dc, info.Name, new Point(x, 18), 30, info.Colour);
            Text(dc, i == 0 && g.State != GameState.Attract ? "PLAYER" : "CPU", new Point(x + 92, 20), 18, Dim);
            Text(dc, showing ? m.Score.ToString() : "", new Point(x + 280, 18), 30, White, right: true);
            var bar = new Rect(x, 40, 200, 12);
            dc.FillRectangle(Brush(0xFF2A2A2A), bar);
            if (showing && m.Active)
                dc.FillRectangle(Brush(m.Health > 50 ? Green : m.Health > 25 ? Yellow : Red), new Rect(bar.X, bar.Y, bar.Width * m.Health / 100, bar.Height));
            dc.DrawRectangle(Pen(Dim, 1), bar);
            if (i == 0 && showing && !m.Ai)
                for (int k = 0; k < Math.Min(5, m.Lives); k++) dc.DrawEllipse(Brush(info.Colour), Pen(0xFF101010, 1), new Point(x + 216 + k * 14, 46), 5, 5);
        }
        Text(dc, $"DAY {g.Day}", new Point(Field.Width - 14, 18), 30, Yellow, right: true);
        Text(dc, $"HI {Math.Max(g.HighScores.TopScore, g.Player.Score)}", new Point(Field.Width - 14, 46), 20, Dim, right: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        if (g.Message is { } msg) Text(dc, msg, new Point(cx, 230), msg.Length > 20 ? 52 : 70, Yellow, center: true);
        if (g.Phase == Phase.DayClear)
        {
            dc.FillRectangle(Brush(0xB0000010), new Rect(220, 280, 584, 200));
            Text(dc, $"{g.CityName} IS IN RUINS", new Point(cx, 316), 44, Yellow, center: true);
            Text(dc, $"DAY {g.Day} BONUS  {g.DayBonus}", new Point(cx, 376), 40, Cyan, center: true);
            Text(dc, "ON TO THE NEXT CITY...", new Point(cx, 432), 30, White, center: true);
        }
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x80000010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA RAMPAGE", new Point(cx, 140), 120, Yellow, center: true);
        Text(dc, "AFTER THE 1986 ARCADE CLASSIC", new Point(cx, 212), 28, Dim, center: true);
        for (int i = 0; i < 3; i++)
        {
            var fake = new Monster { Kind = (MonsterKind)i, X = cx - 260 + i * 260, Y = 470, Facing = i == 2 ? -1 : 1, WalkPhase = _clock * 4 + i, PunchTime = (int)(_clock * 2 + i) % 3 == 0 ? 0.1 : 0 };
            DrawMonster(dc, fake);
            Text(dc, Monsters.Of(fake.Kind).Name, new Point(fake.X, 500), 30, Monsters.Of(fake.Kind).Colour, center: true);
        }
        string[] lines =
        [
            "CLIMB THE BUILDINGS AND PUNCH THEM DOWN",
            "EAT WHAT YOU FIND IN THE WINDOWS - BUT NOT THE BOMBS",
            "SWAT THE HELICOPTERS, STAMP ON THE SOLDIERS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 560 + i * 32), 28, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 680), 44, Yellow, center: true);
    }

    private void DrawSelect(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC8000010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "CHOOSE YOUR MONSTER", new Point(cx, 150), 64, Yellow, center: true);
        for (int i = 0; i < 3; i++)
        {
            var info = Monsters.All[i];
            double x = cx - 300 + i * 300;
            bool chosen = (int)g.Choice == i;
            if (chosen) dc.DrawRectangle(Brush(0x40FFFFFF), Pen(info.Colour, 3), new Rect(x - 120, 230, 240, 360), 10, 10);
            var fake = new Monster { Kind = (MonsterKind)i, X = x, Y = 440, Facing = 1, WalkPhase = chosen ? _clock * 6 : 0, PunchTime = chosen && (int)(_clock * 2) % 2 == 0 ? 0.11 : 0 };
            DrawMonster(dc, fake);
            Text(dc, info.Name, new Point(x, 480), 40, info.Colour, center: true);
            Text(dc, info.What.ToUpperInvariant(), new Point(x, 518), 22, White, center: true);
            Text(dc, info.Gift, new Point(x, 556), 24, chosen ? Yellow : Dim, center: true);
        }
        Text(dc, "LEFT / RIGHT TO CHOOSE - ENTER OR PUNCH TO RAMPAGE", new Point(cx, 650), 30, White, center: true);
        Text(dc, "THE OTHER TWO WILL BE YOUR RIVALS", new Point(cx, 688), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC8000010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "MOST DESTRUCTIVE", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   DAY", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,2}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC8000010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Player.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
