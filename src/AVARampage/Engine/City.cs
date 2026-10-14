namespace AVARampage.Engine;

/// <summary>
/// A day's city: a row of buildings along one street, each with its floors of windows and what is
/// behind them. The names are this game's own; the cities come round again, taller and better
/// defended.
/// </summary>
public static class City
{
    public static readonly string[] Names =
    [
        "HALVORSEN", "PIKE CITY", "GRIMSPORT", "LOWELL BAY", "NEW KESTREL", "ASHBY FALLS", "OXBRIDGE FLATS", "CORMORANT",
        "DUNMERE", "PELLHAM", "WESTON GAP", "IRONVALE", "SALT HARBOUR", "MARROWBY", "FENWICK", "BLACKWATER",
    ];

    public static string NameOf(int day) => Names[(day - 1) % Names.Length];

    private static readonly uint[] Walls = [0xFFA05A40, 0xFF8A8A92, 0xFFC0A070, 0xFF6A7A8A, 0xFFB07A5A, 0xFF7A6A5A, 0xFF9A9AA8, 0xFF5A6A60];

    public static List<Building> Make(int day, Random rng)
    {
        var list = new List<Building>();
        int count = 3 + Math.Min(2, (day - 1) / 2) + rng.Next(2);
        double margin = 70, street = Field.Width - margin * 2;
        double gapTotal = (count - 1) * 40;
        double each = (street - gapTotal) / count;
        double x = margin;
        for (int i = 0; i < count; i++)
        {
            int columns = Math.Clamp((int)(each / 46), 2, 5);
            double width = columns * 46;
            int floors = Math.Clamp(4 + rng.Next(5) + Math.Min(2, day / 3), 4, 10);
            var b = new Building
            {
                X = x + (each - width) / 2, Width = width, Floors = floors, Columns = columns,
                Colour = Walls[rng.Next(Walls.Length)], Style = rng.Next(3),
            };
            b.Blocks = new Block[columns, floors];
            for (int c = 0; c < columns; c++)
            for (int f = 0; f < floors; f++)
                b.Blocks[c, f] = new Block { Occupant = Pick(rng, day, f), Lit = rng.Next(3) > 0 };
            list.Add(b);
            x += each + 40;
        }
        return list;
    }

    /// <summary>What's behind a window: mostly nothing, often people, sometimes food or money, now and then a nasty surprise.</summary>
    private static Occupant Pick(Random rng, int day, int floor)
    {
        int r = rng.Next(100);
        if (floor == 0 && r < 30) return Occupant.None;
        return r switch
        {
            < 46 => Occupant.None,
            < 66 => Occupant.Person,
            < 72 => Occupant.Food,
            < 76 => Occupant.Money,
            < 80 => Occupant.Television,
            < 85 => Occupant.Bomb,
            < 90 => Occupant.Wire,
            _ => day >= 2 || r < 94 ? Occupant.Soldier : Occupant.Person,
        };
    }
}
