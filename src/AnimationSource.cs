namespace BigBlueFish;

internal sealed record AnimationSource(string File, int Width, int Height)
{
    internal static AnimationSource Software(string original, bool highDefinition) => highDefinition
        ? new(original, 1280, 720)
        : new(Path.Combine(Path.GetDirectoryName(original)!, "compat", Path.GetFileName(original)), 640, 360);
}
