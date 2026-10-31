# Genso Suikoden Saturn Dialogue Studio — VB2022 v0.2.1

**Native Visual Basic .NET 8 Windows Forms application, built for Visual Studio 2022. No Python runtime.**

## What is fixed in v0.2.1

- **Real Japanese text immediately after opening ISO/BIN/CUE.** The program now uses an embedded, independently validated native Saturn Japanese glyph lookup, rather than leaving the JP column blank. Unrecognized glyph/control bytes are shown explicitly in `[HH]` or `[HH HH]` form instead of inventing a character.
- **Verified message index for the original English Beta v1.24 game.** The built-in index covers 84 source banks, 281 verified message-pointer tables and **10,882 actual catalogue records** across 12 dialogue-bearing `MURA_*1.BIN` files. 373 known non-dialogue pointer slots are excluded. Native file SHA-256 checks prevent applying those static offsets to a different game revision.
- **No false repeated pointers** such as the screenshot where pointer offsets 15084 onward misleadingly shared text offset `00081828`. When source hashes differ, the fallback scanner rejects forward pointers and implausibly repetitive/unsorted groups.
- **Not every valid line starts with DF.** The verified scanner now supports unquoted text, control-prefixed text, and ordinary message bodies ending in `0F`.
- **Pointer offsets displayed in hexadecimal** and the full Japanese text shown in the right-hand editor pane.

The *optional* **LOAD JP INDEX** button can still import your older recovered Japanese/English catalogue (useful for earlier draft translations), but **it is no longer required to see Japanese text** in the target English Beta v1.24 game.

## Open and use

1. Download the compiled Windows 64-bit package from the successful build of this version or open `GensoSaturnDialogueStudio.vbproj` in Visual Studio 2022.
2. Choose **OPEN ISO / BIN / CUE** and select the English Beta v1.24 image.
3. Scan discovers all 36 MURA resources; the verified indexed text is read from the 12 dialogue-bearing files, with records in Japanese and original byte offsets.
4. Search and select a line. Edit the **MANUAL ENGLISH TRANSLATION** box. Use **EXPORT CSV** to save your progress. Import previously translated CSV later.
5. **Rebuild game copy** requires encoded translation bytes. Load a verified Saturn English character mapping CSV and select **ENCODE ENGLISH**, or supply complete `...0F` replacement bytes manually. The program rejects patches longer than the original byte allocation and refuses to touch the source image.

## Honest limits

- The program can display original Japanese text using its embedded decoder. Some unusual Kanji/glyph bytes remain explicitly tagged because no verified Unicode mapping was available.
- Native English font encoding is not automatically solved; typing English alone saves a CSV draft, not playable in-game text.
- Only fixed-size or shorter safe binary replacements are written. Pointer relocation, text bank expansion and truly arbitrary longer English script insertion remain unsupported.
- Disc rebuilding supports ISO 2048 and Mode1/2352 images (including 2448 subchannel container with Mode1 user data). Copies leave the original image and audio tracks unchanged. Changed sectors receive regenerated Mode1 EDC/ECC.
- The new 10,882-entry trusted layout is revision-specific and loaded only if the game resource hash matches. An unknown game revision can produce fewer results with the safe fallback scanner.
- **Tests:** Windows .NET compilation, Japanese byte decoding, 10,882 indexed-slot accounting, standalone ISO/Mode1 BIN/CUE synthetic fixtures, and fixed-size rebuild/reopen are tested through the GitHub Actions workflow. Emulator testing on the actual commercial disc remains unverified.

## Source

`MainFormModern.vb`, `SaturnDisc.vb`, `DialogueData.vb`, `SaturnJapaneseDecoder.vb`, `KnownSaturnDialogueLayout.vb`, `CdRomEccEdc.vb`, `ResourceFolderPatcher.vb`, `AppSelfTests.vb`, `Program.vb`, and the Visual Studio `.vbproj`. Legacy `MainForm.vb` is retained as history, but excluded from compilation.

**Backup:** v0.2.0, v0.1.2, v0.1.1, and v0.1.0 remain on their separate source branches.

v0.2.1, 2026-10-31.
