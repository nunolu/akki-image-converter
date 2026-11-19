# Akki Image Tools roadmap

This is a proposed sequence, not a release commitment. Ship and validate each small update before expanding the scope. Keep image processing local and preserve originals by default.

## First update: image transformations

- Horizontal and vertical flips, usable independently or together with rotation, with an accurate preview and batch processing.
- Rename the app to Akki Image Tools in version 1.2.0, after confirming the new name is reserved for the existing Store product. Keep the existing package identity so installed Store users can receive updates.
- Test the app and prepare release notes, a Windows build, and Store listing changes before publishing.

## Later candidate updates

1. **Batch naming:** optional prefix, suffix, and numbering with a clear output-name preview and collision protection.
2. **Compression assistance:** target file size for suitable lossy formats, with clear feedback when a target cannot be reached and a visual quality comparison.
3. **Reusable presets:** save common conversion and resize settings for web, email, and other repeated tasks.
4. **Crop and framing:** crop or fit images to common aspect ratios, with a preview and an explicit batch policy.
5. **Watermarking:** optional text or image watermark with position, size, opacity, and preview.

Review scope and technical feasibility for each update before implementation, especially codec support, quality trade-offs, privacy, and accessibility. The ordering can change based on user feedback.
