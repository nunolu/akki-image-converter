using Akki.ImageConverter.Core;
using ImageMagick;
using Engine = Akki.ImageConverter.Core.ImageConverter;

var root = Path.Combine(Path.GetTempPath(), "AkkiConverterTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++; Console.WriteLine("PASS: " + name);
}
try
{
    var legacyPreferences = System.Text.Json.JsonSerializer.Deserialize<Preferences>("""
        {"Theme":2,"Updates":2,"ShareUsageStatistics":true,"StatisticsInstallationId":"old-id","StatisticsLastRunVersion":"0.1.5"}
        """)!;
    Check(legacyPreferences.Theme == AppTheme.Dark && legacyPreferences.Updates == UpdatePolicy.Manual,
        "Previous settings preserve theme and update preference");
    var savedPreferences = System.Text.Json.JsonSerializer.Serialize(legacyPreferences);
    Check(!savedPreferences.Contains("Statistics", StringComparison.OrdinalIgnoreCase) && !savedPreferences.Contains("old-id"),
        "Saving previous settings discards retired analytics fields and installation ID");
    var input = Path.Combine(root, "input");
    var output = Path.Combine(root, "output");
    Directory.CreateDirectory(Path.Combine(input, "nested"));
    var source = Path.Combine(input, "nested", "transparent.png");
    using (var image = new MagickImage(MagickColors.Transparent, 400, 200))
    {
        using var overlay = new MagickImage(MagickColors.Red, 200, 100);
        image.Composite(overlay, 0, 0, CompositeOperator.Over);
        image.Write(source);
    }
    var options = new ConversionOptions { OutputDirectory = output, Lossless = true };
    var engine = new Engine();
    var src = new ImageSource(source, input);
    Check(Engine.CalculateSize(400, 200, options with { Resize = ResizeKind.Pixels, Width = 100, Height = 100 }) == (100u, 50u), "Aspect-preserving fit");
    Check(Engine.CalculateSize(400, 200, options with { Resize = ResizeKind.Percent, Percent = 25 }) == (100u, 50u), "Percentage resize");
    Check(Engine.CalculateSize(400, 200, options with { Resize = ResizeKind.Pixels, Width = 800, Height = 800 }) == (400u, 200u), "Prevent upscaling");
    Check(Engine.CalculateSize(400, 200, options with { Resize = ResizeKind.Pixels, Width = 80, Height = 90, KeepAspectRatio = false }) == (80u, 90u), "Exact dimensions");
    var before = File.ReadAllBytes(source);
    foreach (var rotation in new[] { RotationKind.Clockwise90, RotationKind.Clockwise180, RotationKind.Clockwise270 })
    {
        var rotatedOptions = options with { Format = OutputKind.Png, Rotation = rotation };
        var rotated = engine.Convert(src, rotatedOptions, CancellationToken.None);
        using var actual = new MagickImage(rotated.Path);
        using var expected = new MagickImage(source);
        expected.Rotate((int)rotation * 90);
        Check(actual.Width == expected.Width && actual.Height == expected.Height &&
            actual.Compare(expected, ErrorMetric.Absolute) == 0, $"{rotation} conversion pixels");
        var rotatedPreview = engine.Preview(source, rotatedOptions);
        using var previewImage = new MagickImage(rotatedPreview.Converted);
        actual.Resize(700, 440);
        Check(rotatedPreview.Width == rotated.Width && rotatedPreview.Height == rotated.Height &&
            previewImage.Compare(actual, ErrorMetric.MeanAbsolute) < 0.01, $"{rotation} preview matches conversion");
    }
    var rotatedResized = engine.Convert(src, options with { Format = OutputKind.Png, Rotation = RotationKind.Clockwise90,
        Resize = ResizeKind.Pixels, Width = 100, Height = 100 }, CancellationToken.None);
    Check(rotatedResized.Width == 50 && rotatedResized.Height == 100, "Rotation precedes resizing");
    foreach (var (horizontal, vertical) in new[] { (true, false), (false, true), (true, true) })
    {
        var flipOptions = options with { Format = OutputKind.Png, FlipHorizontal = horizontal, FlipVertical = vertical };
        var flipped = engine.Convert(src, flipOptions, CancellationToken.None);
        using var actual = new MagickImage(flipped.Path);
        using var expected = new MagickImage(source);
        if (horizontal) expected.Flop();
        if (vertical) expected.Flip();
        Check(actual.Compare(expected, ErrorMetric.Absolute) == 0, $"Flip horizontal={horizontal}, vertical={vertical}");
        var flipPreview = engine.Preview(source, flipOptions);
        using var previewImage = new MagickImage(flipPreview.Converted);
        actual.Resize(700, 440);
        Check(previewImage.Compare(actual, ErrorMetric.MeanAbsolute) < 0.01, $"Flip preview horizontal={horizontal}, vertical={vertical}");
    }
    var combinedOptions = options with { Format = OutputKind.Png, Rotation = RotationKind.Clockwise90, FlipHorizontal = true };
    var combined = engine.Convert(src, combinedOptions, CancellationToken.None);
    using (var actual = new MagickImage(combined.Path))
    using (var expected = new MagickImage(source))
    {
        expected.Rotate(90);
        expected.Flop();
        Check(actual.Compare(expected, ErrorMetric.Absolute) == 0, "Rotation precedes horizontal flip");
    }
    var oldOptions = System.Text.Json.JsonSerializer.Deserialize<ConversionOptions>("{}");
    Check(oldOptions is { FlipHorizontal: false, FlipVertical: false }, "Existing preferences default to no flips");
    var restoredOptions = System.Text.Json.JsonSerializer.Deserialize<ConversionOptions>(System.Text.Json.JsonSerializer.Serialize(combinedOptions));
    Check(restoredOptions is { FlipHorizontal: true, FlipVertical: false }, "Flip preference persists");
    foreach (var format in Engine.SupportedOutputs)
    {
        var result = engine.Convert(src, options with { Format = format }, CancellationToken.None);
        using var decoded = new MagickImage(result.Path);
        Check(decoded.Width > 0 && decoded.Height > 0 && result.OutputBytes > 0, $"Encode and decode {format}");
        Check(Path.GetDirectoryName(result.Path) == Path.Combine(output, "nested"), $"Preserve folder structure {format}");
    }
    var webp = engine.Convert(src, options, CancellationToken.None);
    using (var decoded = new MagickImage(webp.Path))
    {
        Check(decoded.HasAlpha, "WebP preserves transparency");
        using var originalImage = new MagickImage(source);
        Check(decoded.Compare(originalImage, ErrorMetric.Absolute) == 0, "Lossless WebP pixel equality");
    }
    var collision = engine.Convert(src, options, CancellationToken.None);
    Check(collision.Path != webp.Path && File.Exists(webp.Path), "Collision renames instead of overwriting");
    var sameFolder = engine.Convert(src, options with { Format = OutputKind.Png, OutputDirectory = Path.GetDirectoryName(source)!, KeepFolderStructure = false }, CancellationToken.None);
    Check(sameFolder.Path != source && File.ReadAllBytes(source).SequenceEqual(before), "Same-format conversion preserves source");
    var jpeg = engine.Convert(src, options with { Format = OutputKind.Jpeg }, CancellationToken.None);
    using (var decoded = new MagickImage(jpeg.Path))
    using (var pixels = decoded.GetPixels())
    {
        var pixel = pixels.GetPixel(399, 199).ToColor()!;
        Check(pixel.R > 60000 && pixel.G > 60000 && pixel.B > 60000, "JPEG transparency uses white background");
    }
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try { engine.Convert(src, options, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "Cancellation honoured"); }
    }
    var animation = Path.Combine(input, "animation.gif");
    using (var frames = new MagickImageCollection())
    {
        frames.Add(new MagickImage(MagickColors.Red, 10, 10));
        frames.Add(new MagickImage(MagickColors.Blue, 10, 10));
        frames.Write(animation);
    }
    try { engine.Convert(new(animation), options, CancellationToken.None); throw new Exception("Animation was truncated"); }
    catch (NotSupportedException) { Check(true, "Animation explicitly rejected"); }
    try { engine.Convert(new(source, Path.Combine(input, "other")), options, CancellationToken.None); throw new Exception("Escaped root"); }
    catch (ArgumentException) { Check(true, "Folder traversal rejected"); }
    var preview = engine.Preview(source, options with { Resize = ResizeKind.Percent, Percent = 50 });
    Check(preview.Width == 200 && preview.Height == 100 && preview.EncodedBytes > 0, "Preview uses real encoding and resizing");
    Check(!Directory.EnumerateFiles(output, ".akki-*.tmp", SearchOption.AllDirectories).Any(), "Temporary files cleaned");
    Check(File.ReadAllBytes(source).SequenceEqual(before), "Original source unchanged after all conversions");
    Console.WriteLine($"\n{passed} checks passed. Supported output formats: {string.Join(", ", Engine.SupportedOutputs)}");
}
finally
{
    // The path is created by this test under the OS temporary directory, never user input.
    if (Path.GetDirectoryName(root)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(root).StartsWith("AkkiConverterTests-", StringComparison.Ordinal)) Directory.Delete(root, true);
}
