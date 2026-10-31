Imports System
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Security.Cryptography
Imports System.Globalization
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Linq

Public Class MainForm
    Inherits Form
    Private ReadOnly rows As New List(Of DialogueRow)
    Private ReadOnly list As New DataGridView()
    Private ReadOnly jp As New TextBox()
    Private ReadOnly en As New TextBox()
    Private ReadOnly raw As New TextBox()
    Private ReadOnly replacement As New TextBox()
    Private ReadOnly filter As New TextBox()
    Private ReadOnly status As New ToolStripStatusLabel("Open an index to begin.")
    Private ReadOnly current As New BindingSource()
    Private indexPath As String = ""
    Private gameDir As String = ""
    Private imagePath As String = ""
    Private cuePath As String = ""
    Private glyphMap As New Dictionary(Of Char, Byte())()
    Private active As DialogueRow
    Private dirty As Boolean

    Public Sub New()
        Text = "Genso Suikoden Saturn Dialogue Studio — VB2022 v0.1.1"
        Width = 1420 : Height = 860 : MinimumSize = New Size(980, 650)
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 76, .AutoScroll = True, .WrapContents = True}
        AddButton(bar, "Open Dialogue Index", AddressOf OpenIndex)
        AddButton(bar, "Open MURA Folder", AddressOf OpenFolder)
        AddButton(bar, "Open ISO/BIN", AddressOf OpenImage)
        AddButton(bar, "Load Glyph Table", AddressOf LoadGlyphs)
        AddButton(bar, "Export CSV", AddressOf ExportCsv)
        AddButton(bar, "Import CSV", AddressOf ImportCsv)
        AddButton(bar, "Save Project CSV", AddressOf SaveProject)
        AddButton(bar, "Build MURA Copy", AddressOf BuildFolder)
        AddButton(bar, "Build ISO 2048 Copy", AddressOf BuildIso)
        AddButton(bar, "Hex Patch Selected", AddressOf ApplyHexSelected)
        Dim searchBar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 35}
        searchBar.Controls.Add(New Label With {.Text = "Search file / Japanese / English:", .AutoSize = True, .Padding = New Padding(0, 8, 0, 0)})
        filter.Width = 510 : searchBar.Controls.Add(filter)
        AddButton(searchBar, "Filter", Sub() RefreshRows())
        AddButton(searchBar, "All", Sub()
                                         filter.Clear()
                                         RefreshRows()
                                     End Sub)
        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 760}
        list.Dock = DockStyle.Fill : list.ReadOnly = True : list.AllowUserToAddRows = False
        list.AllowUserToDeleteRows = False : list.MultiSelect = False
        list.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        list.AutoGenerateColumns = False : list.RowHeadersVisible = False
        list.Columns.Add(New DataGridViewTextBoxColumn With {.HeaderText = "File", .DataPropertyName = "FileName", .Width = 130})
        list.Columns.Add(New DataGridViewTextBoxColumn With {.HeaderText = "Offset", .DataPropertyName = "OffsetText", .Width = 95})
        list.Columns.Add(New DataGridViewTextBoxColumn With {.HeaderText = "JP dialogue", .DataPropertyName = "Japanese", .Width = 280})
        list.Columns.Add(New DataGridViewTextBoxColumn With {.HeaderText = "English draft", .DataPropertyName = "English", .Width = 360})
        list.DataSource = current
        AddHandler list.SelectionChanged, AddressOf SelectRow
        split.Panel1.Controls.Add(list)
        Dim pane As New TableLayoutPanel With {.Dock = DockStyle.Fill, .RowCount = 8, .ColumnCount = 1, .Padding = New Padding(10)}
        pane.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
        pane.RowStyles.Add(New RowStyle(SizeType.Percent, 28))
        pane.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
        pane.RowStyles.Add(New RowStyle(SizeType.Percent, 34))
        pane.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
        pane.RowStyles.Add(New RowStyle(SizeType.Percent, 20))
        pane.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
        pane.RowStyles.Add(New RowStyle(SizeType.Percent, 18))
        pane.Controls.Add(New Label With {.Text = "Original Japanese (read-only)", .Dock = DockStyle.Fill}, 0, 0)
        ConfigureText(jp, True) : pane.Controls.Add(jp, 0, 1)
        pane.Controls.Add(New Label With {.Text = "Manual English translation (editable)", .Dock = DockStyle.Fill}, 0, 2)
        ConfigureText(en, False) : pane.Controls.Add(en, 0, 3)
        pane.Controls.Add(New Label With {.Text = "Original encoded hex (read-only)", .Dock = DockStyle.Fill}, 0, 4)
        ConfigureText(raw, True) : pane.Controls.Add(raw, 0, 5)
        pane.Controls.Add(New Label With {.Text = "Manual replacement hex (equal-or-shorter only; original terminator preserved)", .Dock = DockStyle.Fill}, 0, 6)
        ConfigureText(replacement, False) : pane.Controls.Add(replacement, 0, 7)
        AddHandler en.TextChanged, Sub()
                                       If active IsNot Nothing Then
                                           active.English = en.Text : dirty = True
                                       End If
                                   End Sub
        AddHandler replacement.TextChanged, Sub()
                                                If active IsNot Nothing Then
                                                    active.ReplacementHex = replacement.Text : dirty = True
                                                End If
                                            End Sub
        split.Panel2.Controls.Add(pane)
        Dim sb As New StatusStrip()
        sb.Items.Add(status)
        Controls.Add(split) : Controls.Add(sb) : Controls.Add(searchBar) : Controls.Add(bar)
        AddHandler Me.FormClosing, Sub(sender, args)
                           If dirty AndAlso MessageBox.Show("Unsaved English changes. Close anyway?", "Confirm", MessageBoxButtons.YesNo) <> DialogResult.Yes Then args.Cancel = True
                       End Sub
    End Sub
    Private Sub ConfigureText(t As TextBox, ro As Boolean)
        t.Multiline = True : t.ReadOnly = ro : t.ScrollBars = ScrollBars.Both : t.Dock = DockStyle.Fill
        t.Font = New Font("Consolas", 10.0F)
    End Sub
    Private Sub AddButton(p As Control, title As String, action As Action)
        Dim b As New Button With {.Text = title, .AutoSize = True, .Height = 30}
        AddHandler b.Click, Sub() 
                                Try
                                    action()
                                Catch ex As Exception
                                    MessageBox.Show(ex.Message, "Operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                End Try
                            End Sub
        p.Controls.Add(b)
    End Sub
    Private Sub OpenIndex()
        Using d As New OpenFileDialog With {.Filter = "Dialogue index|*.json;*.csv|JSON index|*.json|CSV index|*.csv"}
            If d.ShowDialog() <> DialogResult.OK Then Return
            Dim incoming As New List(Of DialogueRow)
            If Path.GetExtension(d.FileName).ToLowerInvariant() = ".json" Then
                Using doc = JsonDocument.Parse(File.ReadAllText(d.FileName, Encoding.UTF8))
                    Dim arr = doc.RootElement.GetProperty("records")
                    For Each o In arr.EnumerateArray()
                        incoming.Add(New DialogueRow With {
                            .Key = Prop(o, "key"), .FileName = Prop(o, "file"),
                            .Japanese = Prop(o, "source_text"), .English = Prop(o, "english_text"),
                            .EncodedHex = Prop(o, "encoded_hex"),
                            .OriginalSha = Prop(o, "source_sha256"),
                            .TextOffset = LongProp(o, "text_offset")})
                    Next
                End Using
            Else
                incoming.AddRange(ReadCsv(d.FileName))
            End If
            If incoming.Count = 0 Then Throw New InvalidDataException("Index contains no records.")
            rows.Clear() : rows.AddRange(incoming) : indexPath = d.FileName : dirty = False
            RefreshRows() : status.Text = $"{rows.Count:N0} indexed dialogues loaded."
        End Using
    End Sub
    Private Shared Function Prop(o As JsonElement, key As String) As String
        Dim p As JsonElement
        Return If(o.TryGetProperty(key, p) AndAlso p.ValueKind = JsonValueKind.String, p.GetString(), "")
    End Function
    Private Shared Function LongProp(o As JsonElement, key As String) As Long
        Dim p As JsonElement
        Return If(o.TryGetProperty(key, p) AndAlso p.ValueKind = JsonValueKind.Number, p.GetInt64(), 0L)
    End Function
    Private Sub OpenFolder()
        Using d As New FolderBrowserDialog With {.Description = "Select extracted game's MURA files folder"}
            If d.ShowDialog() = DialogResult.OK Then gameDir = d.SelectedPath : status.Text = gameDir
        End Using
    End Sub
    Private Sub OpenImage()
        Using d As New OpenFileDialog With {.Filter = "ISO or BIN image|*.iso;*.bin|All|*.*"}
            If d.ShowDialog() <> DialogResult.OK Then Return
            imagePath = d.FileName
            Dim sidecar = Path.ChangeExtension(imagePath, ".cue")
            cuePath = If(File.Exists(sidecar), sidecar, "")
            status.Text = "Image selected. 2352-byte CD tracks cannot be directly rebuilt in this release."
        End Using
    End Sub
    Private Sub RefreshRows()
        Dim q = filter.Text.Trim()
        current.DataSource = rows.Where(Function(x) q.Length = 0 OrElse x.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) OrElse x.Japanese.Contains(q, StringComparison.OrdinalIgnoreCase) OrElse x.English.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList()
        status.Text = $"{CType(current.DataSource, List(Of DialogueRow)).Count:N0} / {rows.Count:N0} records"
    End Sub
    Private Sub SelectRow(sender As Object, e As EventArgs)
        If list.CurrentRow Is Nothing Then Return
        Dim r = TryCast(list.CurrentRow.DataBoundItem, DialogueRow)
        If r Is Nothing Then Return
        active = Nothing
        jp.Text = r.Japanese : en.Text = r.English : raw.Text = r.EncodedHex : replacement.Text = r.ReplacementHex
        active = r
    End Sub
    Private Sub SaveProject()
        If rows.Count = 0 Then Throw New InvalidOperationException("Load index first.")
        Using d As New SaveFileDialog With {.Filter = "CSV|*.csv", .FileName = "Suikoden_Saturn_Dialogues_Manual.csv"}
            If d.ShowDialog() = DialogResult.OK Then
                WriteCsv(d.FileName) : dirty = False : status.Text = "Saved " & d.FileName
            End If
        End Using
    End Sub
    Private Sub ExportCsv()
        SaveProject()
    End Sub
    Private Sub WriteCsv(path As String)
        Using w As New StreamWriter(path, False, New UTF8Encoding(True))
            w.WriteLine("Key,File,TextOffset,EncodedHex,Japanese,English,ReplacementHex,OriginalSha")
            For Each r In rows
                w.WriteLine(String.Join(",", {r.Key, r.FileName, r.TextOffset.ToString(CultureInfo.InvariantCulture), r.EncodedHex, r.Japanese, r.English, r.ReplacementHex, r.OriginalSha}.Select(AddressOf QuoteCsv)))
            Next
        End Using
    End Sub
    Private Shared Function QuoteCsv(s As String) As String
        Return """" & If(s, "").Replace("""", """""") & """"
    End Function
    Private Shared Function ParseCsv(s As String) As List(Of String())
        Dim result As New List(Of String())(), cur As New List(Of String)(), cell As New StringBuilder()
        Dim quoted As Boolean
        Dim i = 0
        While i < s.Length
            Dim c = s(i)
            If c = """"c Then
                If quoted AndAlso i + 1 < s.Length AndAlso s(i + 1) = """"c Then
                    cell.Append(""""c) : i += 1
                Else
                    quoted = Not quoted
                End If
            ElseIf c = ","c AndAlso Not quoted Then
                cur.Add(cell.ToString()) : cell.Clear()
            ElseIf (c = ChrW(10) OrElse c = ChrW(13)) AndAlso Not quoted Then
                If c = ChrW(13) AndAlso i + 1 < s.Length AndAlso s(i + 1) = ChrW(10) Then i += 1
                cur.Add(cell.ToString()) : cell.Clear()
                If cur.Count > 1 OrElse cur(0) <> "" Then result.Add(cur.ToArray())
                cur = New List(Of String)()
            Else
                cell.Append(c)
            End If
            i += 1
        End While
        If quoted Then Throw New InvalidDataException("CSV has an unclosed quote.")
        cur.Add(cell.ToString())
        If cur.Count > 1 OrElse cur(0) <> "" Then result.Add(cur.ToArray())
        Return result
    End Function
    Private Shared Function ReadCsv(filePath As String) As List(Of DialogueRow)
        Dim data = ParseCsv(System.IO.File.ReadAllText(filePath, Encoding.UTF8))
        If data.Count < 2 Then Return New List(Of DialogueRow)()
        Dim h = data(0).Select(Function(x) x.TrimStart(ChrW(&HFEFF))).ToArray()
        Dim getv As Func(Of String(), String, String) = Function(a, k)
                                                            Dim n = Array.FindIndex(h, Function(x) x.Equals(k, StringComparison.OrdinalIgnoreCase))
                                                            Return If(n >= 0 AndAlso n < a.Length, a(n), "")
                                                        End Function
        Dim result As New List(Of DialogueRow)
        For Each a In data.Skip(1)
            Dim offset As Long
            Long.TryParse(getv(a, "TextOffset"), offset)
            result.Add(New DialogueRow With {.Key = getv(a, "Key"), .FileName = getv(a, "File"),
                .TextOffset = offset, .EncodedHex = getv(a, "EncodedHex"),
                .Japanese = getv(a, "Japanese"), .English = getv(a, "English"),
                .ReplacementHex = getv(a, "ReplacementHex"), .OriginalSha = getv(a, "OriginalSha")})
        Next
        Return result
    End Function
    Private Sub ImportCsv()
        If rows.Count = 0 Then Throw New InvalidOperationException("Open the complete dialogue index first.")
        Using d As New OpenFileDialog With {.Filter = "CSV|*.csv"}
            If d.ShowDialog() <> DialogResult.OK Then Return
            Dim byKey = rows.ToDictionary(Function(x) x.Key, StringComparer.Ordinal)
            Dim count = 0
            For Each r In ReadCsv(d.FileName)
                Dim original As DialogueRow = Nothing
                If byKey.TryGetValue(r.Key, original) AndAlso original.FileName = r.FileName AndAlso original.TextOffset = r.TextOffset AndAlso NormalizeHex(original.EncodedHex) = NormalizeHex(r.EncodedHex) Then
                    original.English = r.English : original.ReplacementHex = r.ReplacementHex : count += 1
                End If
            Next
            dirty = True : RefreshRows()
            MessageBox.Show($"Imported {count} matching entries. No source offsets were modified.")
        End Using
    End Sub
    Private Sub LoadGlyphs()
        Using d As New OpenFileDialog With {.Filter = "Glyph mapping CSV|*.csv"}
            If d.ShowDialog() <> DialogResult.OK Then Return
            Dim lines = ParseCsv(File.ReadAllText(d.FileName, Encoding.UTF8))
            glyphMap.Clear()
            For Each a In lines.Skip(1)
                If a.Length < 2 OrElse a(0).Length <> 1 Then Continue For
                glyphMap(a(0)(0)) = Convert.FromHexString(NormalizeHex(a(1)))
            Next
            MessageBox.Show($"{glyphMap.Count} glyphs loaded. Control tokens must be explicit [HH] / [HH HH].")
        End Using
    End Sub
    Private Shared Function NormalizeHex(s As String) As String
        Return New String(If(s, "").Where(Function(c) Not Char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant()
    End Function
    Private Shared Function HexBytes(s As String) As Byte()
        Dim h = NormalizeHex(s)
        If h.Length Mod 2 <> 0 Then Throw New InvalidDataException("Hex must have complete bytes.")
        Return Convert.FromHexString(h)
    End Function
    Private Sub ApplyHexSelected()
        If active Is Nothing Then Throw New InvalidOperationException("Select a dialogue.")
        Dim bytes = HexBytes(active.ReplacementHex)
        ValidatePatch(active, bytes)
        MessageBox.Show("Replacement hex validated. Use Build MURA Copy or Build ISO 2048 Copy to apply it.")
    End Sub
    Private Shared Sub ValidatePatch(r As DialogueRow, bytes As Byte())
        Dim oldBytes = HexBytes(r.EncodedHex)
        If oldBytes.Length = 0 Then Throw New InvalidDataException("Missing original encoded bytes.")
        If bytes.Length = 0 OrElse bytes.Length > oldBytes.Length Then Throw New InvalidDataException("Replacement must be nonempty and fit within original byte allocation. Expansion/repointing is not implemented.")
        If oldBytes(oldBytes.Length - 1) <> &HF OrElse bytes(bytes.Length - 1) <> &HF Then Throw New InvalidDataException("Saturn message must retain 0F terminator.")
    End Sub
    Private Function Patches() As List(Of DialogueRow)
        Return rows.Where(Function(r) Not String.IsNullOrWhiteSpace(r.ReplacementHex)).ToList()
    End Function
    Private Shared Function Hash(bytes As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    End Function
    Private Shared Sub PatchStream(s As Stream, r As DialogueRow, baseOffset As Long)
        Dim original = HexBytes(r.EncodedHex)
        Dim replacementBytes = HexBytes(r.ReplacementHex)
        ValidatePatch(r, replacementBytes)
        If r.TextOffset < 0 OrElse baseOffset + r.TextOffset + original.Length > s.Length Then Throw New InvalidDataException("Offset outside file: " & r.Key)
        s.Position = baseOffset + r.TextOffset
        Dim present(original.Length - 1) As Byte
        s.ReadExactly(present)
        If Not present.SequenceEqual(original) Then Throw New InvalidDataException("Original byte mismatch; build stopped: " & r.Key)
        Dim patched = CType(original.Clone(), Byte())
        Array.Copy(replacementBytes, patched, replacementBytes.Length)
        If replacementBytes.Length < patched.Length Then Array.Fill(patched, CByte(0), replacementBytes.Length, patched.Length - replacementBytes.Length)
        s.Position = baseOffset + r.TextOffset : s.Write(patched)
    End Sub
    Private Sub BuildFolder()
        If String.IsNullOrEmpty(gameDir) Then Throw New InvalidOperationException("Choose MURA Folder.")
        Dim changes = Patches()
        If changes.Count = 0 Then Throw New InvalidOperationException("No replacement HEX entered. English drafts are not binary encoded automatically.")
        Using d As New FolderBrowserDialog With {.Description = "Select an EMPTY output folder, separate from the originals"}
            If d.ShowDialog() <> DialogResult.OK Then Return
            Dim dst = d.SelectedPath
            If Path.GetFullPath(dst).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then Throw New InvalidOperationException("Output must not be original folder.")
            For Each grp In changes.GroupBy(Function(x) x.FileName, StringComparer.OrdinalIgnoreCase)
                Dim fromPath = Path.Combine(gameDir, grp.Key)
                If Not File.Exists(fromPath) Then Throw New FileNotFoundException("Missing original MURA file", fromPath)
                Dim bytes = File.ReadAllBytes(fromPath)
                For Each r In grp
                    If r.OriginalSha.Length > 0 AndAlso Not String.Equals(Hash(bytes), r.OriginalSha, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("Source SHA mismatch: " & grp.Key)
                Next
                Dim destPath = Path.Combine(dst, Path.GetFileName(grp.Key))
                If File.Exists(destPath) Then Throw New IOException("Output exists. Refusing overwrite: " & destPath)
                Using out As New MemoryStream(CType(bytes.Clone(), Byte()), True)
                    For Each r In grp.OrderBy(Function(x) x.TextOffset)
                        PatchStream(out, r, 0)
                    Next
                    File.WriteAllBytes(destPath, out.ToArray())
                End Using
            Next
            MessageBox.Show($"Generated {changes.Select(Function(x) x.FileName).Distinct().Count()} modified MURA copy/copies. Original files unchanged.")
        End Using
    End Sub
    Private Sub BuildIso()
        Throw New NotSupportedException("ISO directory extents, file relocation, Saturn ECC/EDC and CUE reconstruction have not been verified. No unsafe image will be produced. Use Build MURA Copy for safe fixed-length patches.")
    End Sub
End Class

Public Class DialogueRow
    Public Property Key As String = ""
    Public Property FileName As String = ""
    Public Property TextOffset As Long
    Public Property Japanese As String = ""
    Public Property English As String = ""
    Public Property EncodedHex As String = ""
    Public Property ReplacementHex As String = ""
    Public Property OriginalSha As String = ""
    Public ReadOnly Property OffsetText As String
        Get
            Return TextOffset.ToString("X8")
        End Get
    End Property
End Class
