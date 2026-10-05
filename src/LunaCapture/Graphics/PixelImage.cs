namespace LunaCapture.Graphics;

/// <summary>관리 메모리에 있는 BGRA 32bpp 이미지 (top-down).</summary>
internal sealed class PixelImage(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride => Width * 4;
    public byte[] Bgra { get; } = new byte[width * height * 4];
}
