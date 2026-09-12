namespace VideoOptimizer.Core;

// The resolved geometry for one export. All dimensions are even (yuv420p requires even chroma).
// Source dimensions are display (rotation-corrected) values; FFmpeg auto-rotation is left on so the
// filter graph operates on already-uprighted frames.
public sealed record ExportGeometry(
    int SourceWidth, int SourceHeight,
    int OutputWidth, int OutputHeight,
    int ContentWidth, int ContentHeight,
    int CropWidth, int CropHeight, int CropX, int CropY,
    FramingMode Framing, bool Upscaled)
{
    public double OutputAspect => (double)OutputWidth / OutputHeight;

    // True when the output frame equals the source frame with no crop, scale, or pad — the pre-condition
    // for a filter-free stream copy.
    public bool IsPassthrough =>
        OutputWidth == SourceWidth && OutputHeight == SourceHeight
        && ContentWidth == SourceWidth && ContentHeight == SourceHeight
        && (Framing != FramingMode.CropToFill || (CropWidth == SourceWidth && CropHeight == SourceHeight));
}

public static class ExportPlanner
{
    public static ExportGeometry Plan(VideoInfo source, ExportPreset preset, FramingMode framing,
        bool allowUpscaling, CropAnchor? anchor = null)
    {
        // allowUpscaling is the already-resolved decision: the preset's preventUpscaling rule seeds the
        // UI default, and the user's explicit choice overrides it before reaching here.
        var upscalingAllowed = allowUpscaling;

        int sw = source.DisplayWidth, sh = source.DisplayHeight;
        int pw = preset.Video.PreferredWidth, ph = preset.Video.PreferredHeight;
        double targetAspect = (double)pw / ph;

        return framing == FramingMode.CropToFill
            ? PlanCrop(sw, sh, pw, ph, targetAspect, upscalingAllowed, (anchor ?? CropAnchor.Center).Clamped())
            : PlanFit(sw, sh, pw, ph, upscalingAllowed, framing);
    }

    private static ExportGeometry PlanCrop(int sw, int sh, int pw, int ph, double targetAspect,
        bool allowUpscaling, CropAnchor anchor)
    {
        // Largest centred rectangle of the target aspect that fits inside the source.
        int cropW, cropH;
        if ((double)sw / sh > targetAspect)
        {
            cropH = MakeEven(sh, sh);
            cropW = MakeEven(cropH * targetAspect, sw);
        }
        else
        {
            cropW = MakeEven(sw, sw);
            cropH = MakeEven(cropW / targetAspect, sh);
        }

        int cropX = MakeEven((sw - cropW) * anchor.X, sw - cropW);
        int cropY = MakeEven((sh - cropH) * anchor.Y, sh - cropH);

        int outW, outH;
        bool upscaled = false;
        if (allowUpscaling)
        {
            outW = pw;
            outH = MakeEven(pw * (double)cropH / cropW, int.MaxValue);
            upscaled = pw > cropW;
        }
        else if (cropW >= pw)
        {
            // Downscale the crop to the preset width, preserving the crop's aspect exactly (no stretch).
            outW = pw;
            outH = MakeEven(pw * (double)cropH / cropW, int.MaxValue);
        }
        else
        {
            outW = cropW;
            outH = cropH;
        }

        return new ExportGeometry(sw, sh, outW, outH, outW, outH, cropW, cropH, cropX, cropY, FramingMode.CropToFill, upscaled);
    }

    private static ExportGeometry PlanFit(int sw, int sh, int pw, int ph, bool allowUpscaling, FramingMode framing)
    {
        // Scale the whole source to fit inside the preset frame; never upscale unless the user opted in.
        double fit = Math.Min((double)pw / sw, (double)ph / sh);
        bool upscaled = fit > 1.0 && allowUpscaling;
        if (!allowUpscaling) fit = Math.Min(1.0, fit);

        int contentW = Math.Min(pw, MakeEven(sw * fit, pw));
        int contentH = Math.Min(ph, MakeEven(sh * fit, ph));

        // Canvas is always the preset frame so the export matches the platform's expected dimensions.
        return new ExportGeometry(sw, sh, pw, ph, contentW, contentH, 0, 0, 0, 0, framing, upscaled);
    }

    // Round to the nearest even integer, clamped to [2, max] (or [0, max] when max can be zero, e.g. crop offset).
    private static int MakeEven(double value, int max)
    {
        int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        rounded &= ~1;
        if (max <= 0) return 0;
        rounded = Math.Clamp(rounded, 0, max);
        // Content/crop dimensions must be at least 2; offsets may legitimately be 0.
        return rounded;
    }
}
