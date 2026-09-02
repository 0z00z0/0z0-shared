namespace ZeroZero.Win32;

/// <summary>
/// A rectangle in physical pixels: left and top edges inclusive, right and bottom exclusive, as
/// Win32 reports them. Plain numbers, so a caller on any UI framework can do its own arithmetic.
/// </summary>
public readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>
    /// The same size, moved to lie inside <paramref name="bounds"/>. One that is wider or taller
    /// than the bounds keeps their left or top edge, so its origin — the title bar and the first
    /// row — stays on screen.
    /// </summary>
    public NativeRect ClampInto(NativeRect bounds)
    {
        int left = Math.Max(bounds.Left, Math.Min(Left, bounds.Right - Width));
        int top = Math.Max(bounds.Top, Math.Min(Top, bounds.Bottom - Height));
        return new NativeRect(left, top, left + Width, top + Height);
    }
}
