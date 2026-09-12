namespace VideoOptimizer.Core;

public enum ColorKind { Sdr, Hdr, Unknown }

public sealed record AudioInfo(string Codec, int? Channels, int? SampleRate, long? Bitrate);

public sealed record VideoInfo(
    string InputPath, TimeSpan Duration, long SizeBytes,
    int Width, int Height, double Rotation, double? FramesPerSecond,
    double? NominalFramesPerSecond, bool MayBeVariableFrameRate,
    string VideoCodec, string PixelFormat, int? BitDepth, long? Bitrate,
    AudioInfo? Audio, string? ColorPrimaries, string? ColorTransfer,
    string? ColorMatrix, string? ColorRange, ColorKind Color, int VideoStreamIndex)
{
    public string FileName => Path.GetFileName(InputPath);
    public bool SwapsAxes => Math.Abs(Rotation % 180) is > 45 and < 135;
    public int DisplayWidth => SwapsAxes ? Height : Width;
    public int DisplayHeight => SwapsAxes ? Width : Height;
}
