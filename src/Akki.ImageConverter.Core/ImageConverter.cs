using ImageMagick;

namespace Akki.ImageConverter.Core;

public sealed class ImageConverter
{
    public static readonly HashSet<string> InputExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".webp", ".avif", ".tif", ".tiff", ".bmp", ".ico", ".gif" };

    public static IReadOnlyList<OutputKind> SupportedOutputs => Enum.GetValues<OutputKind>()
        .Where(kind => MagickNET.SupportedFormats.Any(f => f.Format == ToMagickFormat(kind) && f.SupportsWriting)).ToArray();

    public static MagickFormat ToMagickFormat(OutputKind kind) => kind switch
    {
        OutputKind.Jpeg => MagickFormat.Jpeg, OutputKind.Png => MagickFormat.Png,
        OutputKind.Avif => MagickFormat.Avif, OutputKind.Tiff => MagickFormat.Tiff,
        OutputKind.Bmp => MagickFormat.Bmp, OutputKind.Ico => MagickFormat.Ico, _ => MagickFormat.WebP
    };

    public static (uint Width, uint Height) CalculateSize(uint width, uint height, ConversionOptions options)
    {
        if (options.Resize == ResizeKind.Original) return (width, height);
        double sx, sy;
        if (options.Resize == ResizeKind.Percent) sx = sy = options.Percent / 100;
        else
        {
            sx = (double)options.Width / width;
            sy = (double)options.Height / height;
            if (options.KeepAspectRatio) sx = sy = Math.Min(sx, sy);
        }
        if (options.PreventUpscale) { sx = Math.Min(1, sx); sy = Math.Min(1, sy); }
        return ((uint)Math.Max(1, Math.Round(width * sx)), (uint)Math.Max(1, Math.Round(height * sy)));
    }

    private static MagickImage LoadStill(string path)
    {
        // Ping the collection first: never silently discard animation frames or TIFF pages.
        using var info = new MagickImageCollection();
        info.Ping(path);
        if (info.Count > 1 && !path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Animated or multipage image: conversion is not supported in this version. Original kept.");
        var image = new MagickImage(path);
        image.AutoOrient();
        return image;
    }

    private static void Prepare(MagickImage image, ConversionOptions options)
    {
        var size = CalculateSize(image.Width, image.Height, options);
        if (size != (image.Width, image.Height))
            image.Resize(new MagickGeometry(size.Width, size.Height) { IgnoreAspectRatio = true });
        if (options.Format is OutputKind.Jpeg or OutputKind.Bmp)
        {
            image.BackgroundColor = new MagickColor(options.Background);
            image.Alpha(AlphaAction.Remove);
        }
        if (options.StripMetadata)
        {
            // Convert profiled images before removing profiles to retain their appearance.
            if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
            else image.ColorSpace = ColorSpace.sRGB;
            image.Strip();
        }
        image.Quality = options.Quality;
        if (options.Format == OutputKind.WebP)
        {
            image.Settings.SetDefine(MagickFormat.WebP, "lossless", options.Lossless ? "true" : "false");
            image.Settings.SetDefine(MagickFormat.WebP, "method", "5");
        }
        if (options.Format == OutputKind.Avif)
            image.Settings.SetDefine(MagickFormat.Heic, "lossless", options.Lossless ? "true" : "false");
        image.Format = ToMagickFormat(options.Format);
    }

    public ConversionResult Convert(ImageSource source, ConversionOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var image = LoadStill(source.Path);
        Prepare(image, options);
        if (options.Format == OutputKind.Ico && (image.Width > 256 || image.Height > 256))
            image.Resize(256, 256);
        var directory = Path.GetFullPath(options.OutputDirectory);
        if (options.KeepFolderStructure && source.ImportRoot is not null)
        {
            var relative = Path.GetRelativePath(source.ImportRoot, Path.GetDirectoryName(source.Path)!);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new ArgumentException("Image is outside its imported folder.");
            directory = Path.Combine(directory, relative);
        }
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(source.Path);
        // Write to an exclusive temporary file, then atomically rename without overwriting anything.
        var temporary = Path.Combine(directory, $".akki-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                image.Write(stream);
            cancellationToken.ThrowIfCancellationRequested();
            for (int suffix = 0; ; suffix++)
            {
                var path = Path.Combine(directory, stem + (suffix == 0 ? "" : $" ({suffix})") + options.Extension);
                try
                {
                    File.Move(temporary, path, false);
                    return new(path, new FileInfo(source.Path).Length, new FileInfo(path).Length, image.Width, image.Height);
                }
                catch (IOException) when (File.Exists(path)) { }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public (byte[] Original, byte[] Converted, long EncodedBytes, uint Width, uint Height) Preview(string path, ConversionOptions options)
    {
        options.Validate();
        using var image = LoadStill(path);
        using var original = image.Clone();
        original.Resize(700, 440);
        var before = original.ToByteArray(MagickFormat.Png);
        Prepare(image, options);
        if (options.Format == OutputKind.Ico) image.Resize(256, 256);
        var encoded = image.ToByteArray();
        using var decoded = new MagickImage(encoded);
        var width = decoded.Width;
        var height = decoded.Height;
        decoded.Resize(700, 440);
        return (before, decoded.ToByteArray(MagickFormat.Png), encoded.LongLength, width, height);
    }
}
