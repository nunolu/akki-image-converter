# Next release draft

This draft describes the current development version. The release version and final contents will be confirmed before publication.

- A redesigned workspace with a thumbnail queue, grouped settings, and a hideable options panel.
- Compare the original and processed image with a draggable divider, zoom controls, and output details.
- Batch naming with prefixes, suffixes, and sequence numbers; save and load presets.
- Centre-crop, resize, rotate, flip, and add a text or image watermark.
- Set a target size for lossy JPEG or WebP as a best-effort compression goal.
- Use the optional compact edge drop window to queue images while working in other apps.
- Optional Explorer integration: a classic context-menu command for standalone builds; a native command is included in the Store package and awaits installed-package verification.

Images are processed locally and originals are preserved. Update checks and downloads use the network according to the user's settings.

## Checks before publication

- Complete combined batch, preview, compact-window, and installed-edition testing, including update and uninstall behaviour.
- Verify the Store package's native Explorer command and Open with activation in a Microsoft-signed installed package before advertising those features.
- Build release packages with a new version greater than 1.2.0 and validate the Store package, installer, update feed, checksums, and third-party source bundle.
- Update the GitHub README and release, Microsoft Store listing, and the existing standalone update feed. Keep server connection details in private operator notes, not this public repository.
- Update the website article and download links after the release is available.
