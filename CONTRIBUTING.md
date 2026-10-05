# Contributing to XenonForge

Thanks for helping improve XenonForge. Keep changes focused, describe the Xbox image or GOD behavior being changed, and avoid committing copyrighted game data or disc images.

For engine changes, include a short explanation of the format behavior being implemented and, where possible, a reproducible test using synthetic data or metadata-only fixtures. Do not add external converter executables as runtime dependencies.

Before opening a pull request, run `build.bat` on Windows with .NET 8 SDK and verify that the self-contained `publish\XenonForge.exe` starts successfully.
