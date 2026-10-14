namespace AVARampage.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec Unit => Length < 1e-9 ? new Vec(0, -1) : this * (1 / Length);
}

public static class Field
{
    public const double Step = 1.0 / 60;
    /// <summary>The whole screen is the city: the score bar across the top, the street along the bottom.</summary>
    public const double Width = 1024, Height = 768;
    /// <summary>The top of the street: where feet stand and buildings start.</summary>
    public const double Ground = 700;
    /// <summary>The bottom of the score bar.</summary>
    public const double Top = 70;
    public const double Gravity = 1500;
}
