namespace Akki.ImageConverter.Core;

public enum OutputKind { WebP, Jpeg, Png, Avif, Tiff, Bmp, Ico }
public enum ResizeKind { Original, Percent, Pixels }
public enum RotationKind { None, Clockwise90, Clockwise180, Clockwise270 }
public enum CropKind { None, Square, Landscape, Portrait }
public enum WatermarkPosition { BottomRight, BottomLeft, TopRight, TopLeft, Center }

public sealed record ConversionOptions
{
    public OutputKind Format { get; init; } = OutputKind.WebP;
    public uint Quality { get; init; } = 80;
    public bool Lossless { get; init; }
    public uint TargetKilobytes { get; init; }
    public ResizeKind Resize { get; init; }
    public RotationKind Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public CropKind Crop { get; init; }
    public double Percent { get; init; } = 50;
    public uint Width { get; init; } = 1920;
    public uint Height { get; init; } = 1080;
    public bool KeepAspectRatio { get; init; } = true;
    public bool PreventUpscale { get; init; } = true;
    public bool StripMetadata { get; init; } = true;
    public string Background { get; init; } = "#FFFFFF";
    public string OutputDirectory { get; init; } = "";
    public bool OutputToSourceFolder { get; init; }
    public bool KeepFolderStructure { get; init; } = true;
    public string FilenamePrefix { get; init; } = "";
    public string FilenameSuffix { get; init; } = "";
    public bool NumberFiles { get; init; }
    public string WatermarkText { get; init; } = "";
    public string WatermarkImage { get; init; } = "";
    public uint WatermarkSize { get; init; } = 20;
    public uint WatermarkOpacity { get; init; } = 70;
    public WatermarkPosition WatermarkPosition { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Rotation)) throw new ArgumentException("Choose a valid rotation.");
        if (!Enum.IsDefined(Crop)) throw new ArgumentException("Choose a valid crop.");
        if (!Enum.IsDefined(WatermarkPosition) || WatermarkSize is < 1 or > 100 || WatermarkOpacity > 100)
            throw new ArgumentException("Choose valid watermark position, size and opacity.");
        if (WatermarkText is null || WatermarkImage is null || (WatermarkText.Length > 0 && WatermarkImage.Length > 0))
            throw new ArgumentException("Use either a text or an image watermark.");
        if (Quality > 100) throw new ArgumentException("Quality must be between 0 and 100.");
        if (TargetKilobytes > 0 && (Format is not (OutputKind.Jpeg or OutputKind.WebP) || (Format == OutputKind.WebP && Lossless)))
            throw new ArgumentException("Target file size requires JPEG or lossy WebP.");
        if (TargetKilobytes > 1000000) throw new ArgumentException("Target size must not exceed 1000000 KB.");
        if (Resize == ResizeKind.Percent && (!double.IsFinite(Percent) || Percent <= 0 || Percent > 1000))
            throw new ArgumentException("Resize percentage must be between 0.1 and 1000.");
        if (Resize == ResizeKind.Pixels && (Width == 0 || Height == 0 || Width > 32768 || Height > 32768))
            throw new ArgumentException("Width and height must be between 1 and 32768 pixels.");
        if (!OutputToSourceFolder && string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException("Choose an output folder.");
        if (FilenamePrefix is null || FilenameSuffix is null ||
            FilenamePrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || FilenameSuffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The filename prefix and suffix cannot contain invalid filename characters.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(Background, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Background colour must use #RRGGBB, for example #FFFFFF.");
    }

    public string Extension => Format switch
    {
        OutputKind.Jpeg => ".jpg", OutputKind.Png => ".png", OutputKind.Avif => ".avif",
        OutputKind.Tiff => ".tiff", OutputKind.Bmp => ".bmp", OutputKind.Ico => ".ico", _ => ".webp"
    };
}

public sealed record ImageSource(string Path, string? ImportRoot = null);
public sealed record ConversionResult(string Path, long OriginalBytes, long OutputBytes, uint Width, uint Height);
