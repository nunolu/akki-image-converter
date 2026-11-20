# Akki Image Tools roadmap

This is a proposed sequence, not a release commitment. Ship and validate each small update before expanding the scope. Keep image processing local and preserve originals by default.

## First update: image transformations

- Horizontal and vertical flips, usable independently or together with rotation, with an accurate preview and batch processing.
- Rename the app to Akki Image Tools in version 1.2.0, after confirming the new name is reserved for the existing Store product. Keep the existing package identity so installed Store users can receive updates.
- Test the app and prepare release notes, a Windows build, and Store listing changes before publishing.

## In development

1. **Batch naming:** optional prefix, suffix, and numbering, output-name preview and collision protection.
2. **Compression assistance:** target file size for lossy JPEG and WebP, with feedback when a target cannot be reached and a comparison preview. Size targets are best effort; the quality slider is overridden when enabled.
3. **Reusable presets:** save and load named conversion settings.
4. **Crop:** centre-crop to square, 16:9 or 4:5, applied to every image before resizing.
5. **Watermarking:** optional text or image watermark with position, size, opacity, and preview.

These changes need combined Windows interface and batch-workflow testing before release. Continue to review codec support, quality trade-offs, privacy, and accessibility.

## Interface direction

Use the selected three-column workspace: thumbnail queue, draggable split comparison, and a hideable settings panel with Convert, Edit, and Output groups. Comparison is generated explicitly and should be refreshed after changing settings. Test the layout at small window sizes and in light and dark themes.

An initial optional compact drop window can hide the full workspace, accept files and folders into its queue, and move to the edge of the current display. It stays on top, and closing it restores the full workspace. It needs hands-on testing, including Explorer drag-and-drop, multiple displays and the Store edition. Explorer integration is in progress: the Store package contains a native Windows 11 context-menu command and Open with registration; standalone builds have an optional per-user classic context-menu registration script. Both require installed-edition testing, including multiple selections and update/uninstall behaviour. The classic command appears under Show more options in Windows 11. Do not present either as released until verified.
