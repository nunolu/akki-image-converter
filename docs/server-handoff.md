# Server handoff: update.akki.lu

Deploy static release files for Akki Image Converter. The DNS/subdomain already
exists; retain any existing root-page content and other sites on the server.
No conversion backend, database, Node.js service, or signing key is required.

## Application and current release

- Source: https://github.com/nunolu/akki-image-converter
- Release: https://github.com/nunolu/akki-image-converter/releases/tag/v0.1.3
- Public release API: https://api.github.com/repos/nunolu/akki-image-converter/releases/tags/v0.1.3
- Current release version: **0.1.3**, an **unsigned preview**. Do not label it signed.
- The installed Windows app already requests this exact feed:

```text
https://update.akki.lu/akki-image-converter/win-x64/stable/releases.win.json
```

The channel filename is **releases.win.json**, not releases.win-x64.json. The x64
architecture is already represented by the directory path and release packages.
The GitHub release is marked prerelease, but the existing app's configured feed
path is named `stable`. Deploying it advertises 0.1.3 to clients using that path.

## Files to download

Download all nine assets from the release API's `assets[].browser_download_url`,
or use these filenames under the download base URL:

```text
https://github.com/nunolu/akki-image-converter/releases/download/v0.1.3/
```

```text
lu.akki.ImageConverter-win-Setup.exe
lu.akki.ImageConverter-win-Portable.zip
lu.akki.ImageConverter-0.1.3-full.nupkg
lu.akki.ImageConverter-0.1.3-delta.nupkg
releases.win.json
assets.win.json
RELEASES
akki-image-converter-third-party-sources.zip
SHA256SUMS.txt
```

Verify **every downloaded asset** against SHA256SUMS.txt before deployment. The
source archive accompanies the native-library redistribution obligations; preserve
it with the matching binary release. It is about 315 MB and is not an app update
download. End users download only the required installer/update files.

## Suggested public layout

Keep an immutable copy of all nine files at:

```text
/akki-image-converter/releases/v0.1.3/<filename>
```

Serve the same files, or filesystem links to them, beneath:

```text
/akki-image-converter/win-x64/stable/<filename>
```

The feed contains relative package filenames, so its full and delta `.nupkg` files
must be reachable in the **same directory** as releases.win.json. Do not rename,
recompress, modify, sign, or rebuild any published binary/package on the server;
the manifest hashes describe the exact GitHub assets.

An optional landing page can link to the versioned installer, GitHub source,
source bundle and third-party licence audit. It must not intercept feed requests.

## HTTPS and response behaviour

- Valid publicly trusted HTTPS certificate for update.akki.lu, with automatic renewal.
- Public GET and HEAD requests, no login, cookies, JavaScript challenge or API key.
- Serve JSON as application/json; `.nupkg`/EXE as application/octet-stream and ZIP as application/zip.
- Feed JSON and the mutable `stable` index/installer links: Cache-Control: no-cache.
- Immutable version-directory files and versioned package filenames may use long-term caching.
- Unknown paths should return 404, not an HTML application fallback with HTTP 200.
- Avoid proxy buffering/size/time limits that break large downloads. Byte-range
  support is useful for downloads but the packages must retain their exact bytes.
- No browser CORS configuration is required by this native desktop updater.

## Publish order

1. Stage downloads outside the served directory and verify hashes.
2. Publish the immutable version directory and package files.
3. Verify the full package is downloadable from the stable directory.
4. Atomically replace releases.win.json, assets.win.json and RELEASES **last**.
5. Keep previous release packages and native source archives available when adding releases.

Do not set up unattended polling of GitHub Releases yet. Future promotion should
be an explicit operation that checks the selected release, downloads/verifies all
assets, and only then updates the feed. Signing/CI integration can be added later.

## Acceptance checks and report-back

Confirm:

1. GET/HEAD on the exact feed URL returns 200, JSON content type and an Assets array
   containing version 0.1.3; the response is JSON, not an HTML landing page.
2. Every FileName in that manifest returns 200 from the same URL directory.
3. Downloaded package bytes match the manifest SHA256 and Size fields, as well as SHA256SUMS.txt.
4. The installer, portable ZIP, source bundle and checksum links work in the versioned directory.
5. The HTTPS certificate validates normally and the manifests are not cached stale.

Report the feed URL, version-directory URL, deployed version, checksum results
and webserver configuration location back to the user. No server credentials need
to be sent to the Windows-app chat. After deployment, the Windows-app chat will
test Settings → Check for updates and the installed 0.1.2 → 0.1.3 upgrade path.
