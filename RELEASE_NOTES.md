# XenonForge v1.0.0

First public release of **XenonForge — Xbox 360 GOD Studio**.

## Highlights

- Brand-new native VB.NET ISO → GOD engine; no `iso2god.exe` is launched or required at runtime.
- XSF, XGD1, XGD2 and XGD3 game-partition detection.
- XDVDFS directory parsing and `default.xex` / `default.xbe` metadata extraction.
- Human-readable game identification from an embedded Title ID catalog.
- GOD DataNNNN generation with SHA-1 sub hash tables, master hash tables and cross-part MHT chaining.
- LIVE/STFS metadata header generation.
- Smart tail trimming and full-image compatibility mode.
- Multi-ISO queue, drag-and-drop and folder scanning.
- Direct USB deployment to `Content\0000000000000000` with free-space checking.
- Self-contained Windows x64 single-file publish workflow.
- Modern dark XenonForge UI and activity log.

## Notes

Some install/content discs and compatibility-sensitive games should be converted with **Smart trim disabled** so the entire game partition is retained.
