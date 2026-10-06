# XenonForge Android

Experimental Android/APK frontend for XenonForge.

## Current features

- Select ISO or ZIP through Android's system file picker.
- ZIP archives are extracted automatically and the contained ISO is loaded.
- Reads Xbox/Xbox 360 metadata using the same XenonForge core parser.
- Native ISO to Games on Demand (GOD) conversion.
- Full XDVDFS extraction to an XEX/XBE game folder.
- Progress display and cancellation.
- App-private Android storage; no broad storage permission required.

## Storage

Outputs are written below the app's external Documents area:

`Android/data/no.xenonforge.mobile/files/Documents/XenonForge`

This avoids legacy all-files storage permissions. A future UI can add Storage Access Framework export to user-selected USB/OTG and shared folders.

## Build

Install .NET 8 and the Android workload, prepare XenonForge assets, then:

```
dotnet workload install android
powershell -ExecutionPolicy Bypass -File prepare-assets.ps1
dotnet publish XenonForge.Android/XenonForge.Android.csproj -c Release -f net8.0-android
```

The GitHub Actions workflow `android-build.yml` also builds an APK artifact automatically.
