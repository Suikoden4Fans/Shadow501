Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Windows.Forms
Imports System.Globalization
Imports System.Threading

Public Class MainForm
    Inherits Form

    Private ReadOnly canvasColor As Color = Color.FromArgb(14, 20, 32)
    Private ReadOnly cardColor As Color = Color.FromArgb(26, 35, 51)
    Private ReadOnly softColor As Color = Color.FromArgb(37, 49, 68)
    Private ReadOnly ink As Color = Color.FromArgb(230, 238, 249)
    Private ReadOnly muted As Color = Color.FromArgb(143, 162, 185)
    Private ReadOnly accent As Color = Color.FromArgb(61, 189, 218)

    Private ReadOnly grid As New DataGridView()
    Private ReadOnly binding As New BindingSource()
    Private ReadOnly search As New TextBox()
    Private ReadOnly viewMode As New ComboBox()
    Private ReadOnly originalText As New TextBox()
    Private ReadOnly englishText As New TextBox()
    Private ReadOnly originalHex As New TextBox()
    Private ReadOnly replacementHex As New TextBox()
    Private ReadOnly info As New Label()
    Private ReadOnly summary As New Label()
    Private ReadOnly status As New ToolStripStatusLabel("Open ISO / BIN / CUE to start scanning.")
    Private ReadOnly progressBar As New ToolStripProgressBar()
    Private ReadOnly buttons As New List(Of Button)
    Private stopTranslateButton As Button
    Private translating As CancellationTokenSource

    Private messages As New List(Of DialogueEntry)()
    Private chosen As DialogueEntry
    Private glyphMap As New Dictionary(Of Char, Byte())()
    Private currentSource As String = ""
    Private currentFolder As Boolean = False
    Private lastIndex As String = ""
    Private lastProjectCsv As String = ""
    Private modified As Boolean = False
    Private loading As Boolean = False
    Private fileCount As Integer

    Public Sub New()
        Text = "Genso Suikoden • Saturn Dialogue Studio | VB2022 v0.2.2"
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(1510, 915)
        MinimumSize = New Size(1080, 720)
        Font = New Font("Segoe UI", 10.0F)
        BackColor = canvasColor
        ForeColor = ink
        SetStyle(ControlStyles.OptimizedDoubleBuffer, True)

        Dim header As New Panel With {.Dock = DockStyle.Top, .Height = 90, .BackColor = Color.FromArgb(10, 16, 27)}
        Dim side As New Panel With {.Dock = DockStyle.Left, .Width = 6, .BackColor = accent}
        header.Controls.Add(side)
        Dim title As New Label With {.Text = "GENSO SUIKODEN  /  SATURN DIALOGUE STUDIO", .Font = New Font("Segoe UI Semibold", 19.0F, FontStyle.Bold), .ForeColor = ink, .AutoSize = True, .Location = New Point(27, 14)}
        Dim subTitle As New Label With {.Text = "VB.NET 2022  •  ISO/BIN/CUE SCANNER  •  MANUAL LOCALIZATION  •  NONDESTRUCTIVE REBUILD", .Font = New Font("Segoe UI", 9.5F), .ForeColor = muted, .AutoSize = True, .Location = New Point(30, 56)}
        header.Controls.Add(title)
        header.Controls.Add(subTitle)

        Dim toolbar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 93, .Padding = New Padding(13, 8, 10, 5), .BackColor = cardColor, .WrapContents = True, .AutoScroll = True}
        AddButton(toolbar, "OPEN ISO / BIN / CUE", AddressOf PickImage, True, 188)
        AddButton(toolbar, "OPEN DIALOGUE CSV", AddressOf OpenCsvStandalone, False, 183)
        AddButton(toolbar, "SCAN MURA FOLDER", AddressOf PickFolder, False, 175)
        AddButton(toolbar, "IMPORT PRIOR JP/EN", AddressOf PickIndex, False, 177)
        AddButton(toolbar, "IMPORT CSV", AddressOf ImportCsv, False, 130)
        AddButton(toolbar, "EXPORT CSV", AddressOf ExportCsv, False, 130)
        AddButton(toolbar, "LOAD GLYPH MAP", AddressOf PickGlyphs, False, 160)
        AddButton(toolbar, "ENCODE ENGLISH", AddressOf EncodeSelected, False, 167)
        AddButton(toolbar, "TRANSLATE ALL", AddressOf TranslateAll, True, 157)
        AddButton(toolbar, "STOP TRANSLATION", AddressOf StopTranslation, False, 168)
        stopTranslateButton = buttons.Last()
        stopTranslateButton.Enabled = False
        AddButton(toolbar, "REBUILD GAME COPY", AddressOf BuildCopy, True, 182)

        Dim metrics As New Panel With {.Dock = DockStyle.Top, .Height = 54, .BackColor = canvasColor, .Padding = New Padding(17, 5, 10, 0)}
        summary.Dock = DockStyle.Fill
        summary.Text = "NO IMAGE LOADED      |      0 FILES      |      0 DIALOGUES"
        summary.ForeColor = accent
        summary.Font = New Font("Segoe UI Semibold", 11.0F, FontStyle.Bold)
        summary.TextAlign = ContentAlignment.MiddleLeft
        metrics.Controls.Add(summary)

        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .BackColor = canvasColor, .SplitterWidth = 7}
        split.Panel1.BackColor = canvasColor
        split.Panel2.BackColor = cardColor

        Dim leftHeader As New Panel With {.Dock = DockStyle.Top, .Height = 51, .BackColor = canvasColor}
        Dim findLabel As New Label With {.Text = "SEARCH", .Location = New Point(15, 15), .AutoSize = True, .ForeColor = muted, .Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)}
        search.Location = New Point(100, 9)
        search.Size = New Size(415, 32)
        search.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        search.BorderStyle = BorderStyle.FixedSingle
        search.BackColor = softColor : search.ForeColor = ink
        search.Font = New Font("Segoe UI", 11.0F)
        viewMode.DropDownStyle = ComboBoxStyle.DropDownList
        viewMode.Items.AddRange(New String() {"All dialogues", "Untranslated", "English drafts", "Patch ready", "Japanese available", "Over game limit", "Needs review"})
        viewMode.SelectedIndex = 0
        viewMode.Size = New Size(180, 30)
        viewMode.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        viewMode.BackColor = softColor : viewMode.ForeColor = ink : viewMode.FlatStyle = FlatStyle.Flat
        AddHandler leftHeader.Resize, Sub()
                                          viewMode.Location = New Point(Math.Max(540, leftHeader.ClientSize.Width - 195), 9)
                                          search.Width = Math.Max(140, viewMode.Left - 110)
                                      End Sub
        leftHeader.Controls.Add(findLabel) : leftHeader.Controls.Add(search) : leftHeader.Controls.Add(viewMode)
        leftHeader.PerformLayout()

        grid.Dock = DockStyle.Fill
        grid.BackgroundColor = canvasColor
        grid.BorderStyle = BorderStyle.None
        grid.EnableHeadersVisualStyles = False
        grid.ColumnHeadersHeight = 44
        grid.ColumnHeadersDefaultCellStyle = New DataGridViewCellStyle With {.BackColor = softColor, .ForeColor = ink, .Font = New Font("Segoe UI Semibold", 10.0F, FontStyle.Bold), .SelectionBackColor = softColor, .SelectionForeColor = ink}
        grid.DefaultCellStyle = New DataGridViewCellStyle With {.BackColor = canvasColor, .ForeColor = ink, .SelectionBackColor = Color.FromArgb(20, 101, 131), .SelectionForeColor = Color.White, .Font = New Font("Segoe UI", 10.0F), .Padding = New Padding(5, 3, 4, 3)}
        grid.AlternatingRowsDefaultCellStyle = New DataGridViewCellStyle With {.BackColor = Color.FromArgb(19, 29, 43), .ForeColor = ink, .SelectionBackColor = Color.FromArgb(20, 101, 131), .SelectionForeColor = Color.White, .Font = New Font("Segoe UI", 10.0F)}
        grid.GridColor = Color.FromArgb(43, 58, 76)
        grid.RowHeadersVisible = False
        grid.RowTemplate.Height = 37
        grid.AllowUserToResizeRows = False
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.ReadOnly = True
        grid.MultiSelect = False
        grid.AutoGenerateColumns = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.Columns.Add(MakeColumn("RESOURCE", "FileName", 137))
        grid.Columns.Add(MakeColumn("POINTER", "PointerText", 100))
        grid.Columns.Add(MakeColumn("TEXT OFFSET", "OffsetText", 100))
        grid.Columns.Add(MakeColumn("JAPANESE / SOURCE", "Japanese", 250))
        grid.Columns.Add(MakeColumn("ENGLISH", "English", 290))
        grid.Columns.Add(MakeColumn("STATE", "StatusText", 125))
        grid.DataSource = binding
        split.Panel1.Controls.Add(grid)
        split.Panel1.Controls.Add(leftHeader)

        Dim rightHeader As New Panel With {.Dock = DockStyle.Top, .Height = 67, .BackColor = softColor, .Padding = New Padding(12)}
        Dim editTitle As New Label With {.Text = "DIALOGUE EDITOR", .Font = New Font("Segoe UI Semibold", 13.0F, FontStyle.Bold), .AutoSize = True, .ForeColor = accent, .Location = New Point(15, 10)}
        info.Text = "Select a record on the left."
        info.ForeColor = muted : info.AutoSize = True : info.Location = New Point(17, 39)
        rightHeader.Controls.Add(editTitle) : rightHeader.Controls.Add(info)

        Dim editor As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 8, .Padding = New Padding(13, 8, 13, 10), .BackColor = cardColor}
        editor.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        editor.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        editor.RowStyles.Add(New RowStyle(SizeType.Percent, 27))
        editor.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        editor.RowStyles.Add(New RowStyle(SizeType.Percent, 33))
        editor.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        editor.RowStyles.Add(New RowStyle(SizeType.Percent, 20))
        editor.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        editor.RowStyles.Add(New RowStyle(SizeType.Percent, 20))
        editor.Controls.Add(FieldLabel("DECODED ORIGINAL JAPANESE  /  READ ONLY"), 0, 0)
        ConfigureEditor(originalText, True, False) : editor.Controls.Add(originalText, 0, 1)
        editor.Controls.Add(FieldLabel("MANUAL ENGLISH TRANSLATION"), 0, 2)
        ConfigureEditor(englishText, False, False) : editor.Controls.Add(englishText, 0, 3)
        editor.Controls.Add(FieldLabel("NATIVE SATURN BYTES  /  ... 0F"), 0, 4)
        ConfigureEditor(originalHex, True, True) : editor.Controls.Add(originalHex, 0, 5)
        editor.Controls.Add(FieldLabel("PATCH BYTES  /  SAFE FIXED LENGTH"), 0, 6)
        ConfigureEditor(replacementHex, False, True) : editor.Controls.Add(replacementHex, 0, 7)

        Dim hint As New Label With {.Dock = DockStyle.Bottom, .Height = 45, .ForeColor = muted, .Text = "Japanese is decoded directly from Saturn game bytes. Unknown glyphs appear as [HEX]. English insertion requires a verified font mapping.", .Padding = New Padding(16, 4, 10, 0), .Font = New Font("Segoe UI", 8.6F)}
        split.Panel2.Controls.Add(editor) : split.Panel2.Controls.Add(hint) : split.Panel2.Controls.Add(rightHeader)

        Dim strip As New StatusStrip With {.BackColor = Color.FromArgb(10, 16, 27), .ForeColor = muted, .SizingGrip = False}
        status.Spring = True : status.TextAlign = ContentAlignment.MiddleLeft
        progressBar.Style = ProgressBarStyle.Marquee : progressBar.MarqueeAnimationSpeed = 32 : progressBar.Visible = False : progressBar.Width = 135
        strip.Items.Add(status) : strip.Items.Add(progressBar)
        Controls.Add(split) : Controls.Add(strip) : Controls.Add(metrics) : Controls.Add(toolbar) : Controls.Add(header)

        AddHandler Me.Shown, Sub()
                                 split.Panel1MinSize = 400
                                 split.Panel2MinSize = 300
                                 Dim desired = CInt(split.ClientSize.Width * 0.67)
                                 split.SplitterDistance = Math.Max(400, Math.Min(desired, split.ClientSize.Width - 307))
                             End Sub
        AddHandler grid.SelectionChanged, AddressOf GridSelection
        AddHandler search.TextChanged, Sub() RefreshGrid()
        AddHandler viewMode.SelectedIndexChanged, Sub() RefreshGrid()
        AddHandler englishText.TextChanged, AddressOf EditEnglish
        AddHandler replacementHex.TextChanged, AddressOf EditHex
        AddHandler FormClosing, AddressOf CloseRequested
        RefreshMetrics()
    End Sub

    Private Function MakeColumn(heading As String, prop As String, width As Integer) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {.HeaderText = heading, .DataPropertyName = prop, .Width = width, .SortMode = DataGridViewColumnSortMode.Automatic, .MinimumWidth = 74}
    End Function

    Private Function FieldLabel(value As String) As Label
        Return New Label With {.Text = value, .Dock = DockStyle.Fill, .Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold), .ForeColor = muted, .TextAlign = ContentAlignment.BottomLeft}
    End Function

    Private Sub ConfigureEditor(tb As TextBox, readOnlyFlag As Boolean, hexFont As Boolean)
        tb.Multiline = True : tb.ReadOnly = readOnlyFlag
        tb.BorderStyle = BorderStyle.FixedSingle
        tb.Dock = DockStyle.Fill : tb.ScrollBars = ScrollBars.Vertical
        tb.BackColor = If(readOnlyFlag, Color.FromArgb(20, 29, 42), softColor)
        tb.ForeColor = ink : tb.Font = New Font(If(hexFont, "Consolas", "Segoe UI"), If(hexFont, 10.0F, 11.0F))
    End Sub

    Private Sub AddButton(parent As FlowLayoutPanel, title As String, click As EventHandler, primary As Boolean, width As Integer)
        Dim b As New Button With {.Text = title, .Width = width, .Height = 36, .Margin = New Padding(4, 3, 4, 5), .FlatStyle = FlatStyle.Flat, .ForeColor = If(primary, canvasColor, ink), .BackColor = If(primary, accent, softColor), .Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold), .Cursor = Cursors.Hand}
        b.FlatAppearance.BorderSize = 0
        AddHandler b.Click, click
        buttons.Add(b)
        parent.Controls.Add(b)
    End Sub

    Private Sub SetBusy(value As Boolean, label As String)
        loading = value
        For Each b In buttons
            b.Enabled = Not value
        Next
        If stopTranslateButton IsNot Nothing Then stopTranslateButton.Enabled = translating IsNot Nothing
        progressBar.Visible = value
        status.Text = label
        Cursor = If(value, Cursors.WaitCursor, Cursors.Default)
    End Sub

    Private Sub RefreshMetrics()
        Dim draft = messages.Where(Function(x) x.English.Length > 0).Count()
        Dim patchCount = messages.Where(Function(x) x.ReplacementHex.Length > 0).Count()
        Dim over = messages.Where(Function(x) x.TranslationFit = "OVER LIMIT").Count()
        summary.Text = $"FILES  {fileCount:N0}       |       DIALOGUES  {messages.Count:N0}       |       ENGLISH DRAFTS  {draft:N0}       |       OVER LIMIT  {over:N0}       |       PATCH READY  {patchCount:N0}    " & If(currentSource.Length = 0, "", "    |    " & Path.GetFileName(currentSource))
    End Sub

    Private Sub RefreshGrid()
        If loading OrElse viewMode.SelectedIndex < 0 Then Return
        Dim q = search.Text.Trim()
        Dim candidates As IEnumerable(Of DialogueEntry) = messages
        If q.Length > 0 Then
            candidates = candidates.Where(Function(x) x.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) OrElse x.Japanese.Contains(q, StringComparison.OrdinalIgnoreCase) OrElse x.English.Contains(q, StringComparison.OrdinalIgnoreCase) OrElse x.Key.Contains(q, StringComparison.OrdinalIgnoreCase) OrElse x.OffsetText.Contains(q, StringComparison.OrdinalIgnoreCase))
        End If
        Select Case viewMode.SelectedIndex
            Case 1 : candidates = candidates.Where(Function(x) x.English.Length = 0)
            Case 2 : candidates = candidates.Where(Function(x) x.English.Length > 0)
            Case 3 : candidates = candidates.Where(Function(x) x.ReplacementHex.Length > 0)
            Case 4 : candidates = candidates.Where(Function(x) x.Japanese.Length > 0)
            Case 5 : candidates = candidates.Where(Function(x) x.TranslationFit = "OVER LIMIT")
            Case 6 : candidates = candidates.Where(Function(x) x.TranslationFit = "REVIEW")
        End Select
        binding.DataSource = New BindingList(Of DialogueEntry)(candidates.ToList())
        status.Text = $"{binding.Count:N0} / {messages.Count:N0} records shown. " & If(modified, "Unsaved changes.", "Original game remains unchanged.")
    End Sub

    Private Sub GridSelection(sender As Object, e As EventArgs)
        Dim r As DialogueEntry = Nothing
        If grid.CurrentRow IsNot Nothing Then r = TryCast(grid.CurrentRow.DataBoundItem, DialogueEntry)
        chosen = Nothing
        originalText.Text = If(r Is Nothing, "", If(r.Japanese.Length = 0, "(Unmapped Japanese glyphs. Load matching JP index for readable text.)", r.Japanese))
        englishText.Text = If(r Is Nothing, "", r.English)
        originalHex.Text = If(r Is Nothing, "", r.EncodedHex)
        replacementHex.Text = If(r Is Nothing, "", r.ReplacementHex)
        info.Text = If(r Is Nothing, "Select a record on the left.", $"{r.FileName}  •  PTR {r.PointerOffset:X8}  •  TEXT {r.TextOffset:X8}")
        chosen = r
    End Sub

    Private Sub EditEnglish(sender As Object, e As EventArgs)
        If chosen Is Nothing OrElse loading Then Return
        chosen.English = englishText.Text : modified = True
        chosen.TranslationFit = ""
        chosen.TranslationNote = "Manually modified. Recheck encoded byte capacity."
        binding.ResetCurrentItem()
        RefreshMetrics()
    End Sub
    Private Sub EditHex(sender As Object, e As EventArgs)
        If chosen Is Nothing OrElse loading Then Return
        chosen.ReplacementHex = replacementHex.Text : modified = True
        binding.ResetCurrentItem()
        RefreshMetrics()
    End Sub

    Private Function ConfirmDiscard() As Boolean
        Return Not modified OrElse MessageBox.Show(Me, "Your current translations are unsaved. Continue and replace the current scan?", "Unsaved work", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes
    End Function

    Private Sub PickImage(sender As Object, e As EventArgs)
        If Not ConfirmDiscard() Then Return
        Using dlg As New OpenFileDialog With {.Title = "Open Saturn image; scanning starts automatically", .Filter = "Saturn images|*.cue;*.bin;*.iso|All files|*.*"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            StartScan(dlg.FileName, False)
        End Using
    End Sub
    Private Sub PickFolder(sender As Object, e As EventArgs)
        If Not ConfirmDiscard() Then Return
        Using dlg As New FolderBrowserDialog With {.Description = "Choose extracted Saturn MURA files"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            StartScan(dlg.SelectedPath, True)
        End Using
    End Sub

    Private Async Sub StartScan(source As String, folder As Boolean)
        SetBusy(True, "Opening image and discovering resource files...")
        Dim progress As New Progress(Of String)(Sub(t) status.Text = t)
        Try
            Dim outcome = Await Task.Run(Function() ScanResources(source, folder, progress))
            messages = outcome.Entries
            currentSource = source
            currentFolder = folder
            fileCount = outcome.FileCount
            modified = False
            SetBusy(False, outcome.Summary)
            Dim lookupIndex = If(lastIndex.Length > 0, lastIndex, FindNearbyIndex(source, folder))
            If lookupIndex.Length > 0 Then
                Try
                    Dim imported = Await Task.Run(Function() LegacyIndex.ApplyFromFile(lookupIndex, messages))
                    lastIndex = lookupIndex
                    status.Text = outcome.Summary & $"  |  {imported:N0} JP index matches."
                Catch ex As Exception
                    MessageBox.Show(Me, "Disc scan succeeded but optional JP index failed: " & ex.Message, "Optional index", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End Try
            End If
            RefreshGrid()
            RefreshMetrics()
            If messages.Count = 0 Then MessageBox.Show(Me, "Game files were found, but no verified dialogue pointer groups were detected. Try the matching English-beta disc revision or inspect the MURA folder. No image bytes were changed.", "Scanner result", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            SetBusy(False, "Scan failed: " & ex.Message)
            MessageBox.Show(Me, ex.ToString(), "Cannot scan disc", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Class ScanOutcome
        Public Property Entries As New List(Of DialogueEntry)
        Public Property FileCount As Integer
        Public Property Summary As String = ""
    End Class

    Private Shared Function ScanResources(source As String, folder As Boolean, progress As IProgress(Of String)) As ScanOutcome
        Dim result As New ScanOutcome()
        Dim officialNames As String() = {
            "MURA_A1.BIN", "MURA_B1.BIN", "MURA_C1.BIN", "MURA_D1.BIN",
            "MURA_E1.BIN", "MURA_F1.BIN", "MURA_G1.BIN", "MURA_H1.BIN",
            "MURA_I1.BIN", "MURA_N1.BIN", "MURA_S1.BIN", "MURA_Z1.BIN"}
        Dim handled As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim verifiedBeta As Boolean = True

        If folder Then
            Dim filePaths = Directory.GetFiles(source, "MURA_*.BIN", SearchOption.TopDirectoryOnly).OrderBy(Function(x) x).ToArray()
            result.FileCount = filePaths.Length
            For Each officialName In officialNames
                Dim resourcePath = Path.Combine(source, officialName)
                If Not File.Exists(resourcePath) Then
                    verifiedBeta = False
                    Continue For
                End If
                progress.Report("Validating and scanning native text: " & officialName)
                Dim bytes = File.ReadAllBytes(resourcePath)
                If KnownSaturnDialogueLayout.ForFile(officialName, bytes).Count = 0 Then verifiedBeta = False
                result.Entries.AddRange(NativeDialogueScanner.ScanBytes(officialName, bytes))
                handled.Add(officialName)
            Next
            If Not verifiedBeta Then
                'Fallback for modified or other Saturn revisions:
                'inspect remaining resources without claiming the beta layout.
                For Each resourcePath In filePaths
                    Dim resourceName = Path.GetFileName(resourcePath)
                    If handled.Contains(resourceName) Then Continue For
                    progress.Report("Scanning unrecognized resource: " & resourceName)
                    result.Entries.AddRange(NativeDialogueScanner.ScanBytes(resourceName, File.ReadAllBytes(resourcePath)))
                Next
            End If
            result.Summary = If(verifiedBeta,
                $"Verified Saturn English Beta text: {result.Entries.Count:N0} messages in 12 known MURA resources ({result.FileCount} files discovered).",
                $"Revision-specific scan: {result.FileCount} MURA resources, {result.Entries.Count:N0} candidate messages. Native glyphs decoded where known.")
        Else
            Using disc = SaturnDisc.Open(source)
                Dim mura = disc.Files.Where(Function(x) Not x.IsDirectory AndAlso
                    x.Name.StartsWith("MURA_", StringComparison.OrdinalIgnoreCase) AndAlso
                    x.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)).ToArray()
                result.FileCount = mura.Length
                If mura.Length = 0 Then
                    Throw New InvalidDataException($"ISO9660 volume parsed ({disc.Files.Count} entries), but no MURA_*.BIN files exist on this disc.")
                End If
                For Each officialName In officialNames
                    Dim resource = disc.FindFile(officialName)
                    If resource Is Nothing Then
                        verifiedBeta = False
                        Continue For
                    End If
                    progress.Report($"Reading real Saturn dialogues: {resource.Name} ({resource.Length:N0} bytes)...")
                    Dim bytes = disc.ReadFileBytes(resource)
                    If KnownSaturnDialogueLayout.ForFile(resource.Name, bytes).Count = 0 Then verifiedBeta = False
                    result.Entries.AddRange(NativeDialogueScanner.ScanBytes(resource.Name, bytes))
                    handled.Add(resource.Name)
                Next
                If Not verifiedBeta Then
                    For Each resource In mura
                        If handled.Contains(resource.Name) Then Continue For
                        progress.Report($"Scanning unrecognized resource: {resource.Name} ({resource.Length:N0} bytes)...")
                        result.Entries.AddRange(NativeDialogueScanner.ScanBytes(resource.Name, disc.ReadFileBytes(resource)))
                    Next
                End If
                result.Summary = If(verifiedBeta,
                    $"Verified Saturn English Beta: {result.Entries.Count:N0} decoded messages from 12 text resources, {result.FileCount} MURA files discovered.",
                    $"{disc.SectorSize}-byte disc scan: {result.FileCount} MURA files, {result.Entries.Count:N0} candidate messages (unknown revision).")
            End Using
        End If
        If verifiedBeta AndAlso result.Entries.Count <> 10882 Then
            result.Summary &= $" WARNING: expected 10,882 indexed entries, found {result.Entries.Count:N0}; check original ISO revision."
        End If
        Return result
    End Function

    Private Shared Function FindNearbyIndex(source As String, folder As Boolean) As String
        Dim dirs = {If(folder, source, Path.GetDirectoryName(source)), AppContext.BaseDirectory}
        Dim names = {"Suikoden_Saturn_Translation_Batch001_Imported.json", "Suikoden_Saturn_English_Translation_Imported.json"}
        For Each dirPath In dirs
            If String.IsNullOrWhiteSpace(dirPath) Then Continue For
            For Each filename In names
                Dim candidate = Path.Combine(dirPath, filename)
                If File.Exists(candidate) Then Return candidate
            Next
        Next
        Return ""
    End Function

    Private Async Sub PickIndex(sender As Object, e As EventArgs)
        If messages.Count = 0 Then
            MessageBox.Show(Me, "Open a Saturn ISO/BIN/CUE or dialogue CSV first, then load its matching earlier JP/EN index.", "No dialogue list")
            Return
        End If
        Using dlg As New OpenFileDialog With {.Title = "Select verified Saturn dialogue index", .Filter = "Legacy dialogue catalogue|*.json"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            SetBusy(True, "Matching Japanese records to verified offsets and bytes...")
            Try
                Dim imported = Await Task.Run(Function() LegacyIndex.ApplyFromFile(dlg.FileName, messages))
                lastIndex = dlg.FileName
                modified = True
                SetBusy(False, $"{imported:N0} existing catalogue matches loaded. Export CSV to save imported drafts.")
                RefreshGrid() : RefreshMetrics()
                MessageBox.Show(Me, $"Verified and loaded {imported:N0} Japanese dialogues from the supplied index. Unmatched records remain intact.", "Index imported")
            Catch ex As Exception
                SetBusy(False, "Index load failed.")
                MessageBox.Show(Me, ex.Message, "Index error")
            End Try
        End Using
    End Sub

    Private Sub ImportCsv(sender As Object, e As EventArgs)
        If messages.Count = 0 Then
            MessageBox.Show(Me, "Scan a game first.", "Import CSV") : Return
        End If
        Using dlg As New OpenFileDialog With {.Filter = "Translation CSV|*.csv"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim count = CsvStore.ImportInto(dlg.FileName, messages)
                modified = True
                RefreshGrid() : RefreshMetrics()
                MessageBox.Show(Me, $"{count:N0} records imported where file, text offset and original bytes matched.", "Import complete")
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Import error")
            End Try
        End Using
    End Sub
    Private Sub ExportCsv(sender As Object, e As EventArgs)
        If messages.Count = 0 Then
            MessageBox.Show(Me, "Scan a game first.", "Export CSV") : Return
        End If
        Using dlg As New SaveFileDialog With {.Filter = "Translation CSV|*.csv", .FileName = "Suikoden_Saturn_All_Dialogues_Manual.csv", .OverwritePrompt = True}
            If lastProjectCsv.Length > 0 Then dlg.FileName = lastProjectCsv
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                CsvStore.Save(dlg.FileName, messages)
                modified = False
                lastProjectCsv = dlg.FileName
                RefreshGrid() : RefreshMetrics()
                status.Text = "CSV saved. Previous CSV version backed up automatically when overwritten."
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Export error")
            End Try
        End Using
    End Sub

    Private Sub PickGlyphs(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = "Verified Saturn glyph mapping CSV|*.csv"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                glyphMap = DialogueEncoding.LoadGlyphMap(dlg.FileName)
                status.Text = $"{glyphMap.Count} glyphs loaded. Only verified mappings should be used for ROM patching."
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Glyph table error")
            End Try
        End Using
    End Sub

    Private Sub EncodeSelected(sender As Object, e As EventArgs)
        If chosen Is Nothing Then
            MessageBox.Show(Me, "Select a dialogue first.", "Encoding") : Return
        End If
        Try
            Dim startsWithQuote = DialogueEncoding.HexToBytes(chosen.EncodedHex)(0) = &HDF
            Dim newBytes = DialogueEncoding.EncodeEnglish(chosen.English, glyphMap, startsWithQuote)
            DialogueEncoding.ValidatePatch(DialogueEncoding.HexToBytes(chosen.EncodedHex), newBytes, chosen.Key)
            chosen.ReplacementHex = DialogueEncoding.ToHex(newBytes)
            replacementHex.Text = chosen.ReplacementHex
            modified = True : RefreshMetrics()
            status.Text = $"Encoded {newBytes.Length} bytes for {chosen.Key}. Original allocation retained."
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "English encoding blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub OpenCsvStandalone(sender As Object, e As EventArgs)
        If Not ConfirmDiscard() Then Return
        Using dlg As New OpenFileDialog With {
            .Filter = "Dialogue CSV|*.csv", .Title = "Open Saturn dialogue CSV without a disc image"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim inputRows = CsvStore.LoadFile(dlg.FileName)
                If inputRows.Count = 0 Then Throw New InvalidDataException("No valid rows with Saturn native text bytes.")
                messages = inputRows
                fileCount = messages.Select(Function(x) x.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                currentSource = ""
                currentFolder = False
                lastProjectCsv = dlg.FileName
                modified = False
                RefreshGrid()
                RefreshMetrics()
                status.Text = $"Loaded {messages.Count:N0} rows from CSV. Ready to translate; disc rebuilding requires reopening and verifying the original ISO."
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Could not open dialogue CSV", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub StopTranslation(sender As Object, e As EventArgs)
        If translating IsNot Nothing Then
            translating.Cancel()
            status.Text = "Translation stop requested. Saving current draft checkpoint..."
        End If
    End Sub

    Private Async Sub TranslateAll(sender As Object, e As EventArgs)
        If translating IsNot Nothing Then Return
        If messages.Count = 0 Then
            MessageBox.Show(Me, "Open your dialogue CSV or scan the Saturn game before translating.", "No dialogues")
            Return
        End If
        Using setup As New AutoTranslateDialog()
            If setup.ShowDialog(Me) <> DialogResult.OK Then Return
            Using outputDlg As New SaveFileDialog With {
                .Filter = "Translation CSV|*.csv", .FileName = "Suikoden_Saturn_All_Dialogues_EN_Drafts.csv",
                .Title = "Choose a separate translated CSV; original is preserved", .OverwritePrompt = True}
                If outputDlg.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim destination = outputDlg.FileName
                If lastProjectCsv.Length > 0 AndAlso
                   Path.GetFullPath(destination).Equals(Path.GetFullPath(lastProjectCsv), StringComparison.OrdinalIgnoreCase) Then
                    MessageBox.Show(Me, "Choose a NEW CSV output name so the source dialogue file stays unchanged.", "Source backup required")
                    Return
                End If

                Dim settings = setup.Configuration
                Dim ready As New Dictionary(Of String, String)(StringComparer.Ordinal)
                For Each record In messages
                    If record.Japanese.Length > 0 AndAlso record.English.Trim().Length > 0 AndAlso
                       Not ready.ContainsKey(record.Japanese) Then
                        ready.Add(record.Japanese, record.English)
                    End If
                Next
                Dim reused As Integer = 0
                For Each record In messages
                    Dim cached As String = Nothing
                    If record.Japanese.Length > 0 AndAlso record.English.Trim().Length = 0 AndAlso
                       ready.TryGetValue(record.Japanese, cached) Then
                        record.English = cached
                        Dim checked = TranslationRules.CheckDraft(record.Japanese, record.EncodedHex, cached)
                        record.TranslationFit = checked.Status
                        record.TranslationNote = checked.Note
                        record.TranslationSource = "Reused previous English draft"
                        reused += 1
                    End If
                Next

                Dim groups = messages.Where(Function(x) x.Japanese.Length > 0 AndAlso
                     x.English.Trim().Length = 0).GroupBy(Function(x) x.Japanese, StringComparer.Ordinal).
                     Select(Function(g) g.ToList()).ToList()
                If File.Exists(destination) Then
                    Dim backup = destination & ".backup_" & DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                    File.Copy(destination, backup, False)
                End If
                translating = New CancellationTokenSource()
                Dim completed = 0
                Dim filled = reused
                Dim saved = False
                Dim resultNote = ""
                SetBusy(True, "Preparing output translation checkpoint...")
                englishText.ReadOnly = True
                Try
                    CsvStore.SaveCheckpoint(destination, messages)
                    saved = True
                    Using translator As New SaturnAutoTranslator(settings)
                        For Each group In groups
                            translating.Token.ThrowIfCancellationRequested()
                            Dim smallest = group.OrderBy(Function(x) TranslationRules.PayloadBudget(x.EncodedHex)).First()
                            Dim result = Await translator.TranslateToCapacityAsync(
                                smallest.Japanese, smallest.EncodedHex, translating.Token)
                            For Each record In group
                                Dim check = TranslationRules.CheckDraft(record.Japanese, record.EncodedHex, result.Text)
                                record.English = check.Text
                                record.TranslationFit = check.Status
                                record.TranslationNote = check.Note
                                record.TranslationSource = settings.Backend & " (machine draft)"
                                filled += 1
                            Next
                            completed += 1
                            If completed Mod 25 = 0 Then
                                CsvStore.SaveCheckpoint(destination, messages)
                                status.Text = $"Translated {completed:N0}/{groups.Count:N0} unique remaining dialogues. {filled:N0} rows filled, output auto-saved. Ctrl/Stop available."
                                binding.ResetBindings(False)
                                RefreshMetrics()
                                Await Task.Yield()
                            End If
                        Next
                    End Using
                    resultNote = $"Completed draft generation for {completed:N0} distinct sources, {filled:N0} filled records. Review the OVER LIMIT and REVIEW groups."
                Catch ex As OperationCanceledException
                    resultNote = $"Stopped safely after {completed:N0} sources. {filled:N0} records filled. Reopen the CSV and use TRANSLATE ALL to resume."
                Catch ex As Exception
                    resultNote = $"Stopped on translation provider error after {completed:N0} sources: {ex.Message}. Existing work is retained."
                Finally
                    Try
                        CsvStore.SaveCheckpoint(destination, messages)
                        saved = True
                    Catch saveError As Exception
                        saved = False
                        resultNote &= " WARNING: checkpoint save failed: " & saveError.Message
                    End Try
                    If translating IsNot Nothing Then translating.Dispose()
                    translating = Nothing
                    englishText.ReadOnly = False
                    SetBusy(False, resultNote)
                    If saved Then
                        lastProjectCsv = destination
                        modified = False
                    Else
                        modified = True
                    End If
                    RefreshGrid()
                    RefreshMetrics()
                    MessageBox.Show(Me, resultNote & Environment.NewLine & Environment.NewLine &
                        "CSV output: " & destination & Environment.NewLine &
                        "Generated English is a draft, NOT verified Saturn-encoded text. Do not rebuild until controls and real font bytes are verified.",
                        "Translation run", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End Try
            End Using
        End Using
    End Sub

    Private Async Sub BuildCopy(sender As Object, e As EventArgs)
        If currentSource.Length = 0 OrElse messages.Count = 0 Then
            MessageBox.Show(Me, "Scan a Saturn image first.", "Rebuild") : Return
        End If
        Dim patches = messages.Where(Function(x) Not String.IsNullOrWhiteSpace(x.ReplacementHex)).ToList()
        If patches.Count = 0 Then
            MessageBox.Show(Me, "No encoded HEX patches ready. English text drafts alone do not patch game bytes. Use ENCODE ENGLISH with a verified font table, or enter replacement hex manually.", "No binary patches")
            Return
        End If
        Using dlg As New FolderBrowserDialog With {.Description = "Select a NEW output folder (not the original game directory)"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim targetDir = dlg.SelectedPath
            SetBusy(True, "Validating translated bytes and creating separate output copy...")
            Dim progress As New Progress(Of String)(Sub(s) status.Text = s)
            Try
                Dim output As String = ""
                If currentFolder Then
                    output = Await Task.Run(Function() ResourceFolderPatcher.MakeCopies(currentSource, targetDir, patches, progress))
                Else
                    output = Await Task.Run(Function()
                                                Using disc = SaturnDisc.Open(currentSource)
                                                    Return disc.RebuildFixedSize(targetDir, patches, progress)
                                                End Using
                                            End Function)
                End If
                SetBusy(False, "Rebuilt: " & output)
                MessageBox.Show(Me, $"Created game data copy:{Environment.NewLine}{output}{Environment.NewLine}{Environment.NewLine}Your original remains untouched.", "Rebuild complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As Exception
                SetBusy(False, "Rebuild blocked. Original unchanged.")
                MessageBox.Show(Me, ex.Message, "Build validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub CloseRequested(sender As Object, e As FormClosingEventArgs)
        If translating IsNot Nothing Then
            translating.Cancel()
            e.Cancel = True
            MessageBox.Show(Me, "Translation stop requested. The CSV checkpoint will be saved before closing.", "Saving work")
            Return
        End If
        If modified AndAlso Not ConfirmDiscard() Then e.Cancel = True
    End Sub
End Class
