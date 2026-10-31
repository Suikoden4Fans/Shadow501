# Genso Suikoden Saturn Dialogue Studio — VB2022 v0.1.1

Native Visual Basic .NET 8 Windows Forms source for Visual Studio 2022. No Python runtime or scripts.

## Open and run
1. Download the source branch ZIP from GitHub.
2. Extract it and open `GensoSaturnDialogueStudioVB2022/GensoSaturnDialogueStudio.vbproj` with Visual Studio 2022.
3. Install the .NET 8 desktop workload if prompted. Build and run.
4. Use **Open Dialogue Index** to select the existing `Suikoden_Saturn_Translation_Batch001_Imported.json` from your own files, or a previously exported CSV. JSON is supported *only as an input adapter* for the original 10,882-record index; the GUI exposes text fields and CSV files, not JSON editing.
5. Search the rows, edit English translations and save or import/export CSV. Text and line breaks are preserved.
6. **Original encoded hex** is shown for each row. **Manual replacement hex** is the only input actually written to copied MURA game files. A draft translation is not automatically converted into Saturn glyph bytes. To convert English safely, a complete verified English font/code table is required.
7. For binary patching select **Open MURA Folder**, enter complete replacement hex for selected rows including 0F terminator, choose **Build MURA Copy** and select a *different* output folder. The app checks the source file SHA where supplied, verifies original bytes at each offset and refuses strings longer than their original allocation. The original MURA files remain untouched.

## Implemented
- Indexed dialogue grid with search/filter (10,882-record data set supported).
- Japanese source and English text editors, manual hex patch field.
- CSV import/export with quoted multiline text, no external packages.
- SHA256 source verification and exact byte verification at each patch site.
- Validated fixed-size or shorter patched MURA output copies.
- Fully separate output path; refuses overwriting existing output file.
- Open ISO/BIN selector, but editing raw tracks **not yet supported**.

## Limitations — IMPORTANT
- **BIN/CUE rebuild not yet implemented**: the Build ISO 2048 Copy control deliberately reports unsupported rather than produce a corrupted image.
- **Saturn English font encoding not automatically solved**: translated text is a saved draft, not a game patch until a verified glyph mapping, control code handling and pointer relocation design are supplied.
- **Longer messages and pointer changes unsupported.** Only explicit equal-or-shorter raw byte replacements within the existing allocation are applied.
- Source was authored but **not compiled or emulator-tested** in this environment.
- Do not confuse `MURA` copy generation with a playable rebuilt disc.
- Do not commit your original game image or private translation dataset to a public repository.

## Backup
The entire original game directory stays unchanged. Export your translation CSV as a versioned backup before patching. Each Build MURA Copy operation outputs only to a distinct folder.

## v0.1.1 repairs
- Corrected ReadCsv path parameter so it does not shadow System.IO.File (VB is case-insensitive).
- Removed invalid ExpectedLength initialization that prevented compilation.

## Structure
- `GensoSaturnDialogueStudio.vbproj` — Visual Studio 2022 project
- `Program.vb` — GUI entry point
- `MainForm.vb` — WinForms UI and dialogue record handling
- `sample_glyph_map.csv` — placeholder format for future verified English encodings

Version: 0.1.1 | Source authoring: 2026-10-31
