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
        if (options.Rotation != RotationKind.None)
            image.Rotate((int)options.Rotation * 90);
        if (options.FlipHorizontal) image.Flop();
        if (options.FlipVertical) image.Flip();
        if (options.Crop != CropKind.None)
        {
            double ratio = options.Crop switch { CropKind.Square => 1, CropKind.Landscape => 16.0 / 9, CropKind.Portrait => 4.0 / 5, _ => 1 };
            uint width = image.Width, height = image.Height;
            if ((double)width / height > ratio) width = Math.Max(1u, (uint)Math.Round(height * ratio));
            else height = Math.Max(1u, (uint)Math.Round(width / ratio));
            image.Crop(new MagickGeometry(width, height), Gravity.Center);
            image.ResetPage();
        }
        var size = CalculateSize(image.Width, image.Height, options);
        if (size != (image.Width, image.Height))
            image.Resize(new MagickGeometry(size.Width, size.Height) { IgnoreAspectRatio = true });
        if (options.WatermarkText.Length > 0 || options.WatermarkImage.Length > 0)
        {
            var gravity = options.WatermarkPosition switch { WatermarkPosition.BottomLeft => Gravity.Southwest, WatermarkPosition.TopRight => Gravity.Northeast, WatermarkPosition.TopLeft => Gravity.Northwest, WatermarkPosition.Center => Gravity.Center, _ => Gravity.Southeast };
            if (options.WatermarkImage.Length > 0)
            {
                using var mark = LoadStill(options.WatermarkImage);
                var maxWidth = Math.Max(1u, image.Width * options.WatermarkSize / 100);
                var maxHeight = Math.Max(1u, image.Height * options.WatermarkSize / 100);
                mark.Resize(new MagickGeometry(maxWidth, maxHeight));
                mark.Alpha(AlphaAction.On);
                mark.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, options.WatermarkOpacity / 100.0);
                image.Composite(mark, gravity, CompositeOperator.Over);
            }
            else
            {
                using var mark = new MagickImage(MagickColors.Transparent, image.Width, image.Height);
                mark.Settings.Font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
                mark.Settings.FontPointsize = Math.Max(1, image.Width * options.WatermarkSize / 100.0 / 4);
                mark.Settings.FillColor = MagickColors.White;
                mark.Annotate(options.WatermarkText, gravity);
                mark.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, options.WatermarkOpacity / 100.0);
                image.Composite(mark, CompositeOperator.Over);
            }
        }
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

    public static string OutputName(string sourcePath, ConversionOptions options, int sequence = 1)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        var stem = options.FilenamePrefix + Path.GetFileNameWithoutExtension(sourcePath) + options.FilenameSuffix +
            (options.NumberFiles ? $"-{sequence:D3}" : "");
        if (string.IsNullOrWhiteSpace(stem) || stem.EndsWith(' ') || stem.EndsWith('.') ||
            stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("The output filename is invalid or reserved by Windows.");
        return stem + options.Extension;
    }

    private static byte[] Encode(MagickImage image, ConversionOptions options, CancellationToken cancellationToken = default)
    {
        if (options.TargetKilobytes == 0) return image.ToByteArray();
        long limit = (long)options.TargetKilobytes * 1024;
        byte[]? best = null;
        uint low = 1, high = 100;
        while (low <= high)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var quality = (low + high) / 2;
            image.Quality = quality;
            var candidate = image.ToByteArray();
            if (candidate.LongLength <= limit) { best = candidate; low = quality + 1; }
            else { if (quality == 1) break; high = quality - 1; }
        }
        if (best is not null) return best;
        image.Quality = 1;
        return image.ToByteArray();
    }

    public ConversionResult Convert(ImageSource source, ConversionOptions options, CancellationToken cancellationToken, int sequence = 1)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var image = LoadStill(source.Path);
        Prepare(image, options);
        if (options.Format == OutputKind.Ico && (image.Width > 256 || image.Height > 256))
            image.Resize(256, 256);
        var directory = options.OutputToSourceFolder ? Path.GetDirectoryName(Path.GetFullPath(source.Path))! : Path.GetFullPath(options.OutputDirectory);
        if (!options.OutputToSourceFolder && options.KeepFolderStructure && source.ImportRoot is not null)
        {
            var relative = Path.GetRelativePath(source.ImportRoot, Path.GetDirectoryName(source.Path)!);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new ArgumentException("Image is outside its imported folder.");
            directory = Path.Combine(directory, relative);
        }
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(OutputName(source.Path, options, sequence));
        // Write to an exclusive temporary file, then atomically rename without overwriting anything.
        var temporary = Path.Combine(directory, $".akki-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (options.TargetKilobytes == 0) image.Write(stream);
                else stream.Write(Encode(image, options, cancellationToken));
            }
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
        var before = original.ToByteArray(MagickFormat.Png);
        Prepare(image, options);
        if (options.Format == OutputKind.Ico) image.Resize(256, 256);
        var encoded = Encode(image, options);
        using var decoded = new MagickImage(encoded);
        var width = decoded.Width;
        var height = decoded.Height;
        return (before, decoded.ToByteArray(MagickFormat.Png), encoded.LongLength, width, height);
    }
}
