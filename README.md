# XenonForge

> **Xbox 360 GOD Studio** — inspect Redump ISOs, identify the game, convert natively to Games on Demand, and deploy the finished package straight to USB.

![XenonForge main window](screenshots/main.png)

XenonForge is a modern Windows utility written in **VB.NET / .NET 8**. The conversion engine is part of the application itself: there is **no `iso2god.exe` process, no Rust runtime, and no external converter binary at runtime**.

## Why XenonForge

- **Native ISO → GOD engine** — XGD1, XGD2, XGD3 and XSF partition detection, XDVDFS parsing, XEX/XBE execution metadata, GOD partition creation, SHA-1 subhash/master hash tables, cross-part MHT chaining and LIVE/STFS header generation.
- **Game identification** — reads Title ID, Media ID, disc number/count, platform and image type directly from the ISO, then resolves the human-readable game name from an embedded title catalog.
- **Modern queue UI** — drag/drop multiple Redump ISOs, scan folders, inspect metadata, batch convert and follow live progress.
- **Smart trim or full image** — smart mode trims unused tail space; full-image mode keeps all game-partition padding for compatibility-sensitive discs.
- **Direct USB deploy** — copies completed GOD content to `Content\0000000000000000\<TitleID>\...` on the selected drive with free-space checks and progress.
- **Single-file release** — the GitHub workflow publishes a self-contained `XenonForge.exe` for Windows x64.
- **No destructive source operations** — XenonForge never deletes or modifies the source ISO.

## USB workflow

![XenonForge USB deploy](screenshots/usb-deploy.png)

1. Add one or more `.iso` files.
2. XenonForge reads the disc metadata and shows the detected game.
3. Choose **Smart trim** or full-image conversion.
4. Click **Convert Queue** or **Convert + USB**.
5. XenonForge writes the GOD package and, when requested, installs it to:

```text
<USB>\Content\0000000000000000\<TitleID>\00007000\...
```

Original Xbox content uses `00005000` instead of `00007000`.

## Build

Requirements:

- Windows 10/11 x64
- .NET 8 SDK
- Internet connection for the **build step only** so `prepare-assets.ps1` can fetch the pinned title database and blank LIVE/STFS template.

Run:

```bat
build.bat
```

The finished program is created at:

```text
publish\XenonForge.exe
```

The final EXE is self-contained and does not download or launch `iso2god.exe`.

## GitHub release flow

The repository contains `.github/workflows/release.yml`. Pushing a version tag such as `v1.0.0` builds Windows x64, creates a ZIP + SHA-256 file, and publishes a GitHub Release.

If GitHub CLI is installed and authenticated, `publish-github.bat` can bootstrap the repository `Darschnid479/XenonForge`, push the source, tag `v1.0.0`, and let Actions create the release.

## Engine layout

```text
XenonForge/
├─ Engine/
│  ├─ XdvdfsReader.vb          # XGD + XDVDFS reader
│  ├─ XboxExecutableParser.vb  # default.xex / default.xbe metadata
│  ├─ GodConverter.vb          # native GOD/STFS writer
│  ├─ GameCatalog.vb           # Title ID → game name
│  └─ Models.vb
├─ Services/
│  ├─ UsbDeploymentService.vb
│  └─ AppSettings.vb
├─ MainForm.vb                 # modern WinForms UI
└─ ThemeControls.vb
```

## Compatibility note

Some Xbox 360 content/install discs and a small number of games can be sensitive to aggressive padding removal. When in doubt, turn **Smart trim** off and convert the full game partition. The UI defaults to smart tail trimming, not a filesystem rebuild.

## Legal / preservation use

XenonForge is intended for managing game images you are legally entitled to use, such as personal archival backups. It does not bypass console security, obtain game images, or download copyrighted game content.

## Credits

XenonForge's application and VB.NET conversion implementation are new code. Format behavior was researched against public Xbox/XDVDFS/STFS information and the MIT-licensed `iso2god-rs` project. Two upstream data assets are fetched at build time and embedded into the finished application: the blank LIVE/STFS template and title catalog. See `THIRD-PARTY-NOTICES.txt`.

## License

MIT — see [LICENSE](LICENSE).
