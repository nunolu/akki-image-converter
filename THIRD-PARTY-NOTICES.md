# Third-party software

Akki Image Converter is made by akki.lu: https://akki.lu

The application's own code is MIT licensed, copyright 2026 akki.lu (nunolu).
Third-party code and binaries retain their own licences; they are not relicensed
under the application's MIT licence. Full licence texts and notices are in
`third-party/licenses` in the source repository and `licenses` in the installer.

- Microsoft Windows App SDK / WinUI: upstream source is MIT; the distributed
  NuGet binaries also include Microsoft Windows App SDK software licence terms.
  Those terms permit redistribution of the runtime files binplaced with this app.
  https://github.com/microsoft/WindowsAppSDK
- .NET runtime: MIT licence. https://github.com/dotnet/runtime
- Magick.NET: Apache License 2.0. https://github.com/dlemstra/Magick.NET
- ImageMagick and bundled codecs: ImageMagick licence, BSD/MIT/Apache/zlib-style
  licences, FreeType licence, and LGPL/MPL-covered components. The complete
  Magick.NET 15.0.0 NOTICE is provided, including native component versions and
  licence texts. No native library modifications are made by this application.
- Velopack: MIT licence. https://github.com/velopack/velopack

## Native source and replacement

The release includes a third-party source bundle and a pinned source manifest,
with the Magick.NET, Magick.Native, ImageMagick and native dependency sources and
build scripts. See `docs/dependencies.md` for versions and rebuild information.
Keep this source bundle available alongside the corresponding binary release.

Magick.Native-Q16-x64.dll is a separate, replaceable library. You may rebuild and
replace the LGPL/MPL-covered library components and debug such modifications.
Nothing in this application's licence restricts those rights. Microsoft runtime
components are separate and remain governed by their own included terms.

This software uses the FreeType library. Copyright © The FreeType Project.

Application artwork was generated for this project using OpenAI image generation;
its source master and generation prompt are included in the repository. The
application's MIT licence also applies to that project artwork to the extent
licensable; no third-party library logos are included.
