namespace TraceDeckFE.Models;

public readonly record struct PointD(double X, double Y);

public readonly record struct IntPoint(int X, int Y);

public readonly record struct IntRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ClientRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
}

public static class ClientAreaCalculator
{
    public static IntRect ToScreenRect(ClientRect clientRect, IntPoint screenOrigin) =>
        new(screenOrigin.X, screenOrigin.Y, clientRect.Width, clientRect.Height);
}
