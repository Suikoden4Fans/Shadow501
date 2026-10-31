# Genso Suikoden Saturn Dialogue Studio — VB2022 v0.2.2

**100% native VB.NET 8 WinForms.** No Python runtime. Windows 64-bit executable and full Visual Studio 2022 source.

## Translate all Japanese dialogues to English

1. Open the application and choose **OPEN DIALOGUE CSV**. Select your `Suikoden_Saturn_All_Dialogues_Manual.csv`. It can load the CSV directly: an ISO is not required for translating drafts.
2. Optional but recommended: choose **IMPORT PRIOR JP/EN** and select your previous `Suikoden_Saturn_Translation_Batch001_Imported.json`. Matching previous English drafts are imported **without replacing existing manual English edits**. Only verified native file/pointer/text/byte matches are imported.
3. Install <https://ollama.com/download/windows>, then run this command in PowerShell or Command Prompt to download a local multilingual model:

   `ollama pull qwen2.5:7b`

4. Press **TRANSLATE ALL**, select **Local Ollama**, use model `qwen2.5:7b`, and choose a **new output CSV filename**. The translations run on your own PC, and no API key is required.
5. The editor translates each **distinct unfilled Japanese message only once**, applies that draft to duplicates, and saves a separate output CSV checkpoint after every 25 distinct translations.
6. **STOP TRANSLATION** cancels the current batch safely. Reopen the saved output CSV and press **TRANSLATE ALL** to resume only missing entries.
7. Select **Over game limit** or **Needs review** from the filter to find translations that could not safely be fitted. Correct them manually and export another CSV.

An alternative provider **MyMemory (free online)** is available without a model download. Unlike Ollama, MyMemory sends your selected Japanese text to an online third-party translation service. A confirmation appears before starting. Their official usage limit is approximately **5,000 characters per day anonymously**, or **50,000 characters/day with a valid contact email**. It cannot translate the entire game in one day at those limits. If the quota stops, your completed entries remain in the saved CSV.

## What “same game limit” means

Each native Japanese message has a fixed byte allocation. The translation workflow:

- Preserves the original Japanese, message ID, native hex and pointer offsets.
- Keeps original bracket control tokens in their original order, including `[0D]`, `[0E]`, `[04 09]`, and `[05 00]`.
- Tries to produce compact English and can ask the local model to shorten over-limit lines twice.
- Calculates a **conservative but provisional byte-length estimate**, flagging `OVER LIMIT` and `REVIEW`; **no translation is silently truncated**.
- Saves `FitStatus`, `ReviewNote`, and `TranslationSource` in CSV columns.
- Does **NOT** automatically place draft text in `ReplacementHex`. Game insertion must be done only after you load a **verified Saturn English glyph table** and confirm exact encoded widths and control bytes.

The draft check is **not a guarantee of in-game compatibility**: English text rendering depends on the actual Saturn font encoding, glyph widths, newlines and placement. The game may need rewrapping or text relocation to accommodate a natural translation.

## Actual file coverage

The supplied CSV contains **11,090 Japanese records**, compared with **10,882 previously verified entries** from the indexed English Beta v1.24. There are 208 additional entries (119 in MURA_A1.BIN and 89 in MURA_Z1.BIN) whose game ownership needs validation. All rows can be used for translation drafts, but do not rebuild those unverified entries into a game image automatically.

## Backups and safety

- The input ISO/BIN/CUE and original CSV are **never overwritten** by automatic translation.
- A new output CSV is created, with frequent atomic-style checkpoints.
- Existing output CSV files receive a timestamped `.backup_...` before being overwritten.
- Native BIN/CUE rebuild remains separate, with original-byte verification and no expansion or pointer relocation. **Auto-generated English text alone is never automatically patched into the game.**
- All earlier source branches v0.2.1 / v0.2.0 / v0.1.2 / v0.1.1 / v0.1.0 remain preserved.

## Tested

Windows CI builds the VB.NET WinForms project and runs synthetic tests for Japanese decoding, control tags, byte estimates, CSV saving/reloading/backups, and ISO/MODE1 BIN/CUE rebuild/reopen. It does not call a live language model or emulator, so translation naturalness and real-game insertion are **not certified**.

Source modules: `MainFormModern.vb`, `AutoTranslateDialog.vb`, `TranslationEngine.vb`, `DialogueData.vb`, `SaturnDisc.vb`, `KnownSaturnDialogueLayout.vb`, `SaturnJapaneseDecoder.vb`, `AppSelfTests.vb`, `CdRomEccEdc.vb`, and `ResourceFolderPatcher.vb`.

**v0.2.2 — 2026-10-31**
