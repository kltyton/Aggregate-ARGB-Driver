namespace HardwareLite.Configuration;

public sealed record MediaEdit
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
    public int Rotation { get; init; }
    public double Start { get; init; }
    public double End { get; init; } = 1;

    public void Validate()
    {
        if (!double.IsFinite(X + Y + Width + Height + Start + End) || X < 0 || Y < 0 || Width <= 0 || Height <= 0
            || X + Width > 1.000001 || Y + Height > 1.000001 || Rotation is < 0 or > 359 || Start < 0 || End > 1 || End <= Start)
            throw new InvalidDataException("图片裁剪区域或 GIF 播放片段无效。");
    }
}
