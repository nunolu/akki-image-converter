# Dependency and licence audit

Audited 2026-10-26 against the restored NuGet packages, their lock files and their
included licence/NOTICE files. The full texts in `third-party/licenses` are the
authoritative notices; this document is a navigational summary.

## Application licence

The original Akki Image Converter code and project artwork are offered under
MIT, copyright 2026 akki.lu (nunolu). Users may use, copy, change and redistribute
them, including commercially, while retaining the copyright and licence notice.
MIT does not require a visible About-screen credit in a derivative application.
Third-party libraries are covered by their own terms, not the app's MIT licence.

## Main components

| Component | Version | Licence / distribution terms |
| --- | --- | --- |
| Magick.NET-Q16-x64 / Magick.NET.Core | 15.0.0 | Apache-2.0; full native NOTICE also applies |
| Magick.Native | 2026.1010.1943 | Apache-2.0 wrapper plus ImageMagick and native dependencies |
| ImageMagick | 7.1.2-33 | ImageMagick licence |
| Windows App SDK / WinUI | 1.8.260921001 package | MIT upstream source; distributed NuGet runtime files also carry Microsoft Windows App SDK licence terms |
| .NET runtime | 10.0.14 | MIT plus runtime third-party notices |
| Velopack (direct-download edition only) | 1.2.161 | MIT |
| WebView2 SDK loader (transitive) | 1.0.3179.45 | Included Microsoft BSD-style licence and NOTICE; no WebView is used by this app |
| Windows SDK / BuildTools | 10.0.26100.4654 package | Microsoft SDK/tool terms; used for building, not licensed as original app code |
| Visual Studio Community | Build-time tool | Microsoft's Community licence, subject to eligibility; not shipped with this app |
| Git / GitHub CLI | Development and publishing tools | GPL-2.0 / MIT respectively; neither is bundled in the installer |

The entire original native NOTICE is retained because it also lists components
that are available in the native build beyond the formats exposed by our UI.

## Native library notices

| Native components | Licence family shown in bundled NOTICE |
| --- | --- |
| libheif, libde265, liblqr | LGPL-3.0 texts |
| FriBidi, GDK-Pixbuf, GLib, liblzma, LibRaw | LGPL-2.1 texts |
| libcroco, Pango, librsvg | GNU Library GPL version 2 texts |
| cairo | MPL-1.1 text |
| FreeType | FreeType Project licence; acknowledgement retained |
| libhwy / Highway | Apache-2.0 or BSD-3-Clause, with per-file exceptions in NOTICE |
| aom, OpenEXR, Imath, libjpeg-turbo, libjxl, OpenH264, OpenJPEG, OpenJPH, libwebp, libzip, zstd | BSD-style / IJG terms as reproduced in NOTICE |
| Brotli, libffi, fontconfig, HarfBuzz, lcms, pixman, libraqm, libxml2 | MIT-style terms as reproduced in NOTICE |
| libbzip2, libpng, libtiff, zlib | Their respective permissive licence texts |

Some upstream projects offer alternative licences or have individual files with
different terms. Preserve the complete texts, rather than substituting this table
for them. No GPL-only optional ImageMagick dependencies (FFTW, FLIF, JBIG) are
added by this application.

## Matching source bundle

Each public binary release is accompanied by `akki-image-converter-third-party-sources.zip`.
It contains source archives for the exact revisions in `third-party/sources.json`,
including the native wrapper, ImageMagick and the dependency source/build-script
repositories, the source manifest, SHA-256 checksums and these instructions.

The native revisions were resolved from:

- Magick.NET 15.0.0's `src/Magick.Native/Magick.Native.version` → `2026.1010.1943`.
- That Magick.Native tag's `src/ImageMagick/ImageMagick.commit` → `211e5b975a0019a88dcef5f84c1193f46738d815`.
- That ImageMagick revision's dependency download script → `2026.10.09.2225`.
- That Dependencies tag's `clone-dependencies.sh` → the individual source revisions.

The sources include upstream patches and build definitions, rather than only
unmodified upstream release tarballs. They are separate release assets to keep
the application's Git history small. Anyone redistributing the binary packages
must preserve the applicable notices and provide the required corresponding
source; keep the bundle alongside the installer and update packages.

To regenerate the bundle after restoring dependencies:

```powershell
.\scripts\CreateSourceBundle.ps1
```

## Rebuilding and replacing the native image library

The source bundle is a collection of upstream source archives; extract them to
separate working folders. Source archive directory names include their commit.
Each archive retains the project's build scripts and licence files.

Use the pinned Magick.Native source's `Building.md` and
`.github/workflows/main.yml`, specifically the **windows** job, as the build recipe.
It uses Visual Studio C++ tools, Windows SDK, Git and the ImageMagick Configure
utility; these extra tools are only needed to rebuild the native DLL, not to
build this C# application against its standard NuGet dependency.

The standard upstream checkout layout is:

```text
Magick.Native/
  src/ImageMagick/ImageMagick/       # pinned ImageMagick source
  src/ImageMagick/Dependencies/      # pinned Dependencies build scripts
    Dependencies/<component>/       # source repos from sources.json
  src/ImageMagick/Configure/         # Configure utility output/files
```

For upstream's default unmodified rebuild, clone the pinned Magick.Native tag,
then follow its Windows workflow:

```text
src\ImageMagick\checkout-release.cmd x64 noOpenMP
build\windows\build.cmd Release Q16 x64 noHdri noOpenMP
```

Run those commands from the directories specified in the upstream workflow.
The default checkout downloads prebuilt dependency archives. To modify/rebuild
a dependency, use the included Dependencies source tree and its Windows CI/build
scripts instead, replacing the corresponding prebuilt libraries before rebuilding
ImageMagick and Magick.Native. Preserve each project's licence and build options.
The Configure source archive is included for rebuilding that generator too.

Close Akki Image Converter and replace `Magick.Native-Q16-x64.dll` in the installed
app's `current` directory with a compatible Q16 x64 build. You may also replace
that DLL in a local published/portable folder. The app imposes no signature or
checksum gate preventing modified native libraries. Keep a backup; an app update
can replace the DLL. Users may debug/reverse engineer the library interaction for
the purpose of modifying LGPL-covered components. Microsoft's separate runtime
components remain under their own terms.

## Source references

- [Magick.NET 15.0.0](https://github.com/dlemstra/Magick.NET/tree/15.0.0)
- [Magick.Native 2026.1010.1943](https://github.com/dlemstra/Magick.Native/tree/2026.1010.1943)
- [Pinned ImageMagick](https://github.com/ImageMagick/ImageMagick/tree/211e5b975a0019a88dcef5f84c1193f46738d815)
- [Pinned dependency build scripts](https://github.com/ImageMagick/Dependencies/tree/2026.10.09.2225)
- [Windows App SDK source licence](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE)
- [Velopack licence](https://github.com/velopack/velopack/blob/develop/LICENSE)
