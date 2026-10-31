# Genso Suikoden Sega Saturn Dialogue Studio — Visual Basic 2022 v0.2.0

**Pure Visual Basic .NET 8 / Windows Forms.** No Python application, and no Python dependency.

## Major fixes from v0.1.2

1. **OPEN ISO / BIN / CUE scans immediately:** opens ISO9660, locates `MURA_*.BIN` files on the game image and searches for validated pointer-linked `DF ... 0F` native Saturn message records. Source data is read directly from the disc without extracting it manually. Raw Mode1/2352, 2448 and logical 2048-sector images are recognized; unsafe or unknown formats are rejected.
2. **New modern dark UI:** slate/charcoal windows, turquoise controls, full searchable dialogue table, English editor, native bytes, patch bytes, progress status, translation counts and filtering.
3. **Game copy rebuild implemented for compatible fixed-size patches:** copies ISO or MODE1/2352 BIN image, applies verified in-place text edits, recomputes CD-ROM Mode1 sector EDC and ECC P/Q, and generates a separately renamed CUE. Existing audio tracks are copied as-is.
4. **MURA folder scan and rebuild:** works if you have already extracted the Saturn files. Modified MURA files go in a separate output folder; originals remain untouched.
5. **CSV import/export and backup:** all exported rows retain source file, pointer/text offsets, native hex and translations. When exporting CSV over an existing file, a timestamped backup is saved.
6. **Legacy Japanese index overlay:** native Saturn dialogue glyphs are custom-encoded; for readable Japanese use **LOAD JP INDEX** on your previously recovered `Suikoden_Saturn_Translation_Batch001_Imported.json`. The app compares file, pointer, offset and raw bytes before attaching Japanese/English text. It also auto-detects that index if it is beside the game image or app. This is a compatibility input, not a JSON editing interface.

## How to run

Open `GensoSaturnDialogueStudio.vbproj` in **Visual Studio 2022** with .NET 8 Desktop installed. Press Start (F5). Or use the precompiled Windows executable attached to the successful GitHub Actions build of this branch.

Open your **GENSO SUIKODEN.cue**, `.bin` or `.iso`; the game should be scanned immediately and a list of pointer-linked messages loaded. Search text or filter results, select a record and edit the English field. Press **EXPORT CSV** periodically to save your translation work.

To put text inside the game:

- The native message format is custom-encoded. English draft text is **not** automatically written as ASCII or Shift-JIS.
- Option A: load a **verified glyph map CSV** mapping each English character to the exact bytes in the Saturn game's installed English font, then press **ENCODE ENGLISH** on a selected message.
- Option B: manually supply the fully encoded `DF ... 0F` replacement in the patch field.
- The new message cannot be longer than its original allocation. A patch requires exact original-byte matches at the known offsets. For long text requiring relocation, no unsafe or guessed pointer edits are attempted.
- Use **REBUILD GAME COPY** and choose a **different output folder**. A new BIN/ISO (plus CUE, if supplied) is built; the original files remain unchanged.

If the game file is modified from a different release, the native pointer scanner may find fewer or different entries than the previously indexed 10,882 messages. The old catalogue is used only after validation, never blindly applied.

## Technical constraints

- **Pointer table discovery:** scans contiguous big-endian `0x0020xxxx`-style Saturn text pointers using 2048-byte-aligned resource bank bases, minimum three consecutive records per group. Unidentified groups are deliberately not invented; the exact record count depends on the disc revision.
- **File format:** ISO9660 volume descriptor and file extents are required. Read-mode supports 2048, Mode1/2352 and 2448-sector containers. Write-mode supports 2048 and Mode1/2352/2448 with 16-byte Mode1 user-data offset. Unsupported Mode2 write scenarios are blocked. Data relocation, file expansion, new LBA tables and event-script rewrites are **not implemented**.
- **Integrity:** changed raw Mode1 sectors have EDC and ECC P/Q recomputed. Original data size, file extent, pointers, non-target bytes, and other tracks are unchanged.
- **Test scope:** the GitHub Actions workflow builds on Windows and runs synthetic ISO9660 and MODE1 BIN/CUE round-trip tests. A PASS on those fixtures is **not** an emulator validation of the commercial game.
- **No original commercial game image or translation data is stored in this public source branch.**

## Backup versions

The v0.1.2, v0.1.1 and v0.1.0 branches remain separate and unchanged. Keep a backup of your original Saturn game and your translation CSV. This v0.2.0 never overwrites an input BIN, CUE or MURA file.

## Source layout

- `Program.vb` — Visual Basic startup and command-line self-test switch
- `MainFormModern.vb` — modern WinForms UI, automatic scanning, manual translation and rebuild workflow
- `SaturnDisc.vb` — ISO9660 / raw CD sector reader and disc-copy patching
- `NativeDialogueScanner.vb` is implemented inside `DialogueData.vb`
- `DialogueData.vb` — pointer scanner, legacy index verification, CSV, glyph encoder
- `CdRomEccEdc.vb` — Mode1 integrity rebuilding
- `ResourceFolderPatcher.vb` — extracted resource safety workflow
- `AppSelfTests.vb` — regression tests
- `MainForm.vb` — historical v0.1.x source retained but excluded from compilation

**Version:** v0.2.0 — 2026-10-31
