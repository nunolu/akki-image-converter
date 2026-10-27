# Microsoft Store build

The Store build is a Windows 11 x64 MSIX package. It uses the reserved identity `akki.lu.AkkiImageConverter` and publisher `akki.lu`. Microsoft Store handles installation and updates; the Store binary excludes Velopack and the standalone update feed.

Build on Windows with the prerequisites from the README:

```powershell
.\scripts\PackageStore.ps1 -Version 1.0.0
```

The script uses WinUI single-project MSIX tooling, includes the .NET and Windows App SDK runtimes, and validates the package identity, minimum Windows version, required resources, and dependency graph. Output is in `artifacts/store/1.0.0/`. The Store and direct-download builds have separate NuGet lock files.

Upload `Akki.ImageConverter_1.0.0.0_x64.msix` to Partner Center. This unsigned file is for Store submission; it is not an installer to distribute directly. Microsoft signs the package delivered through the Store. Store submission accepts MSIX directly; the script avoids the optional upload-container task that fails with the installed MSIX/.NET tools. A missing native-symbol conversion tool can produce an optional-symbol warning; it does not replace package validation or app testing.

Store versions use a nonzero major number and leave the fourth component at zero. Raise the version for subsequent submissions. Run Windows App Certification Kit and test the packaged app, not just the unpackaged executable, before submission. Store review and publication happen separately from building this package.

The standalone release and its update feed remain separate. Do not publish a Store-only MSIX as a standalone updater release. Native corresponding sources and third-party notices remain available with the app's GitHub binary releases.
