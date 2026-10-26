# Akki Image Converter 0.1.3 preview

First public source release, made by [akki.lu](https://akki.lu).

## Features

- Native WinUI 3 interface for Windows 11 x64.
- Batch files/folders, subfolders, drag and drop, and always-on-top.
- WebP, JPEG, PNG, AVIF, TIFF, BMP and ICO output.
- Quality, lossless WebP/AVIF, resizing, transparency background and metadata removal.
- Real conversion previews, safe duplicate naming, progress and cancellation.
- Light, Dark and Windows theme settings, with matching app artwork.
- Installer with shortcuts and configurable update support.

## Source and licences

The application source is MIT licensed, copyright 2026 akki.lu (nunolu).
Reuse and modification are allowed while retaining the copyright/licence notice.
Third-party components keep their own licences. This release includes full
third-party notices, a dependency audit, and a bundle of the matching native
library sources and build scripts.

## Download

Use `lu.akki.ImageConverter-win-Setup.exe` for the installer.
The portable ZIP can also be extracted and run without installation.
The third-party source bundle is intended for developers/re-distributors.

## Known limits

- Unsigned preview build; publisher signing is not configured.
- Animated images and multipage TIFF are explicitly rejected.
- ICO output is a single image up to 256 × 256.
- Live automatic updates require the update feed to be deployed to update.akki.lu.

Validation: clean Release build, 29 conversion integration checks, and installed
startup/theme/artwork verification on the preceding preview.
