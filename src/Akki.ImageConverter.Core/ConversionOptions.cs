namespace Akki.ImageConverter.Core;

public enum OutputKind { WebP, Jpeg, Png, Avif, Tiff, Bmp, Ico }
public enum ResizeKind { Original, Percent, Pixels }

public sealed record ConversionOptions
{
    public OutputKind Format { get; init; } = OutputKind.WebP;
    public uint Quality { get; init; } = 80;
    public bool Lossless { get; init; }
    public ResizeKind Resize { get; init; }
    public double Percent { get; init; } = 50;
    public uint Width { get; init; } = 1920;
    public uint Height { get; init; } = 1080;
    public bool KeepAspectRatio { get; init; } = true;
    public bool PreventUpscale { get; init; } = true;
    public bool StripMetadata { get; init; } = true;
    public string Background { get; init; } = "#FFFFFF";
    public string OutputDirectory { get; init; } = "";
    public bool KeepFolderStructure { get; init; } = true;

    public void Validate()
    {
        if (Quality > 100) throw new ArgumentException("Quality must be between 0 and 100.");
        if (Resize == ResizeKind.Percent && (!double.IsFinite(Percent) || Percent <= 0 || Percent > 1000))
            throw new ArgumentException("Resize percentage must be between 0.1 and 1000.");
        if (Resize == ResizeKind.Pixels && (Width == 0 || Height == 0 || Width > 32768 || Height > 32768))
            throw new ArgumentException("Width and height must be between 1 and 32768 pixels.");
        if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException("Choose an output folder.");
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
