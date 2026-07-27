namespace ScanVault.Core.Models;

/// <summary>Pixel dimensions of one source image or texture map.</summary>
public readonly record struct ImageResolution
{
    public ImageResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int MaxDimension => Math.Max(Width, Height);
    public long PixelCount => (long)Width * Height;

    public string ToDisplayString()
    {
        var dimensions = $"{Width} × {Height}";
        return Width == Height && Width % 1024 == 0
            ? $"{Width / 1024}K ({dimensions})"
            : dimensions;
    }

    public string ToCompactString() =>
        Width == Height && Width % 1024 == 0
            ? $"{Width / 1024}K"
            : $"{Width} × {Height}";
}
