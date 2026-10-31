Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Globalization

Public Class DialogueEntry
    Public Property Key As String = ""
    Public Property FileName As String = ""
    Public Property BankOffset As Integer
    Public Property GroupIndex As Integer
    Public Property MessageIndex As Integer
    Public Property PointerOffset As Long
    Public Property TextOffset As Long
    Public Property Japanese As String = ""
    Public Property English As String = ""
    Public Property EncodedHex As String = ""
    Public Property ReplacementHex As String = ""
    Public Property OriginalSha As String = ""
    Public Property Provenance As String = "Native pointer scan"
    Public Property TranslationFit As String = ""
    Public Property TranslationNote As String = ""
    Public Property TranslationSource As String = ""
    Public ReadOnly Property PointerText As String
        Get
            Return PointerOffset.ToString("X8", CultureInfo.InvariantCulture)
        End Get
    End Property
    Public ReadOnly Property OffsetText As String
        Get
            Return TextOffset.ToString("X8", CultureInfo.InvariantCulture)
        End Get
    End Property
    Public ReadOnly Property StatusText As String
        Get
            If Not String.IsNullOrWhiteSpace(ReplacementHex) Then Return "PATCH READY"
            If Not String.IsNullOrWhiteSpace(TranslationFit) Then Return TranslationFit
            Return If(Not String.IsNullOrWhiteSpace(English), "DRAFT", "UNTRANSLATED")
        End Get
    End Property
End Class

Public NotInheritable Class DialogueEncoding
    Private Sub New()
    End Sub
    Public Shared Function NormalizeHex(value As String) As String
        Return New String(If(value, "").Where(Function(c) Not Char.IsWhiteSpace(c) AndAlso c <> "-"c).ToArray()).ToUpperInvariant()
    End Function
    Public Shared Function HexToBytes(value As String) As Byte()
        Dim h = NormalizeHex(value)
        If h.Length Mod 2 <> 0 Then Throw New InvalidDataException("Hex characters must form complete bytes.")
        If h.Length = 0 Then Return Array.Empty(Of Byte)()
        Return Convert.FromHexString(h)
    End Function
    Public Shared Function ToHex(bytes As Byte()) As String
        Return Convert.ToHexString(bytes)
    End Function
    Public Shared Sub ValidatePatch(original As Byte(), encoded As Byte(), sourceKey As String)
        If original.Length < 2 OrElse original(original.Length - 1) <> &HF Then
            Throw New InvalidDataException("Source message lacks a valid 0F terminator: " & sourceKey)
        End If
        If encoded.Length < 2 OrElse encoded.Length > original.Length Then
            Throw New InvalidDataException($"Message does not fit original allocation ({encoded.Length} > {original.Length}): {sourceKey}")
        End If
        If encoded(encoded.Length - 1) <> &HF Then
            Throw New InvalidDataException("Translation must finish with native 0F terminator: " & sourceKey)
        End If
    End Sub
    Public Shared Function LoadGlyphMap(path As String) As Dictionary(Of Char, Byte())
        Dim map As New Dictionary(Of Char, Byte())
        Dim lines = CsvStore.Parse(File.ReadAllText(path, Encoding.UTF8))
        For Each row In lines.Skip(1)
            If row.Length < 2 OrElse row(0).Length <> 1 Then Continue For
            Dim bytes = HexToBytes(row(1))
            If bytes.Length > 0 Then map(row(0)(0)) = bytes
        Next
        Return map
    End Function
    Public Shared Function EncodeEnglish(s As String, glyphs As Dictionary(Of Char, Byte()), Optional startsWithQuote As Boolean = True) As Byte()
        If glyphs Is Nothing OrElse glyphs.Count = 0 Then Throw New InvalidOperationException("Load a verified Saturn English glyph mapping before encoding.")
        Dim output As New List(Of Byte)
        If startsWithQuote Then output.Add(&HDF)
        Dim i = 0
        While i < s.Length
            Dim ch = s(i)
            If ch = "["c Then
                Dim endAt = s.IndexOf("]"c, i + 1)
                If endAt <= i Then Throw New InvalidDataException("Missing ] in raw control token.")
                Dim text = s.Substring(i + 1, endAt - i - 1)
                Dim tokens = text.Split({" "c, ControlChars.Tab, "-"c}, StringSplitOptions.RemoveEmptyEntries)
                If tokens.Length = 0 Then Throw New InvalidDataException("Empty control token.")
                For Each t In tokens
                    If t.Length <> 2 Then Throw New InvalidDataException("Use [HH] or [HH HH] for original Saturn controls.")
                    output.Add(Byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
                Next
                i = endAt + 1
                Continue While
            End If
            If ch = ControlChars.Cr Then
                i += 1
                Continue While
            End If
            If ch = ControlChars.Lf Then
                If output.Count > 0 AndAlso output(output.Count - 1) <> &HE AndAlso output(output.Count - 1) <> &HD Then output.Add(&HE)
                i += 1
                Continue While
            End If
            Dim encoded As Byte() = Nothing
            If Not glyphs.TryGetValue(ch, encoded) Then Throw New InvalidDataException("Unmapped character: " & ch & " (U+" & AscW(ch).ToString("X4") & ").")
            output.AddRange(encoded)
            i += 1
        End While
        output.Add(&HF)
        Return output.ToArray()
    End Function
End Class

Public NotInheritable Class NativeDialogueScanner
    Private Sub New()
    End Sub
    Private Shared Function RelPtr(data As Byte(), at As Integer) As Integer
        If at < 0 OrElse at + 3 >= data.Length OrElse data(at) <> 0 OrElse data(at + 1) < &H20 OrElse data(at + 1) > &H2F Then Return -1
        Return (CInt(data(at + 1) - &H20) << 16) Or (CInt(data(at + 2)) << 8) Or CInt(data(at + 3))
    End Function
    Private Shared Function MessageEnd(data As Byte(), target As Integer) As Integer
        If target < 0 OrElse target >= data.Length Then Return -1
        Dim limit = Math.Min(data.Length - 1, target + 2047)
        Dim i = target + 1
        While i <= limit
            If data(i) = &HF Then Return i
            'Glyphs and controls 01..0C have a second byte, which
            'may itself equal 0F. Do not mistake such a byte for
            'the end of the actual message.
            If data(i) >= 1 AndAlso data(i) <= 12 Then i += 1
            i += 1
        End While
        Return -1
    End Function
    Private Shared Function Hit(data As Byte(), pointerAt As Integer, bankBase As Integer) As Integer
        Dim relative = RelPtr(data, pointerAt)
        If relative < 0 Then Return -1
        Dim target = CLng(bankBase) + CLng(relative)
        If target < 0 OrElse target >= data.Length Then Return -1
        'Verified Saturn message pointer tables follow their text banks.
        'Reject forward references: these caused repeated false rows
        '(e.g. 0x81828 at small pointer table offsets).
        If target >= pointerAt Then Return -1
        'The heuristic is only a fallback for unrecognized disc revisions.
        'Do not use it to guess a pointer table from arbitrary strings.
        If data(CInt(target)) <> &HDF Then Return -1
        If MessageEnd(data, CInt(target)) < 0 Then Return -1
        Return CInt(target)
    End Function
    Private Shared Function LooksLikeMessageTable(data As Byte(), at As Integer, bank As Integer,
                                                   count As Integer) As Boolean
        If count < 3 Then Return False
        Dim unique As New HashSet(Of Integer)
        Dim earlier As Integer = -1
        Dim decreases As Integer = 0
        For i As Integer = 0 To count - 1
            Dim target = Hit(data, at + i * 4, bank)
            If target < 0 Then Return False
            unique.Add(target)
            If earlier > target Then decreases += 1
            earlier = target
        Next
        'An actual message catalogue contains many distinct message
        'starts. Repeated aliases do not form a dialogue table.
        If unique.Count < 3 Then Return False
        If unique.Count * 2 < count Then Return False
        'Native text tables are predominantly in text offset order.
        If decreases > Math.Max(2, count \ 8) Then Return False
        Return True
    End Function

    Private Shared Function RunLength(data As Byte(), at As Integer, bank As Integer) As Integer
        Dim count = 0
        While at + count * 4L + 4L <= data.Length AndAlso count < 4096
            If Hit(data, at + count * 4, bank) < 0 Then Exit While
            count += 1
        End While
        Return count
    End Function

    'For the exact Saturn English Beta v1.24 resource hashes, scan all
    'known native pointer groups, including tables with only 1-2 entries
    'and messages whose first byte is not DF. 373 structural slots are
    'excluded using the already verified index diagnostics.
    Public Shared Function ScanKnownTables(name As String, data As Byte(),
                                           tables As IReadOnlyList(Of SaturnTableSpec),
                                           Optional excludeStructural As Boolean = True) As List(Of DialogueEntry)
        Dim result As New List(Of DialogueEntry)
        If data Is Nothing OrElse tables Is Nothing Then Return result
        For Each spec In tables
            Dim tableStart As Long = CLng(spec.Bank) + CLng(spec.Table)
            For message As Integer = 0 To spec.Slots - 1
                If excludeStructural AndAlso KnownSaturnDialogueLayout.IsRejected(
                    name, spec.Bank, spec.Group, message) Then Continue For
                Dim ptrPos As Long = tableStart + CLng(message) * 4L
                If ptrPos < 0 OrElse ptrPos + 4L > data.Length Then Continue For
                Dim relative = RelPtr(data, CInt(ptrPos))
                If relative < 0 Then Continue For
                Dim textStart As Long = CLng(spec.Bank) + CLng(relative)
                If textStart < spec.Bank OrElse textStart >= ptrPos OrElse
                    textStart >= spec.BankEnd Then Continue For
                Dim endAt = MessageEnd(data, CInt(textStart))
                If endAt < 0 OrElse endAt >= spec.BankEnd OrElse
                    endAt >= tableStart Then Continue For
                Dim messageBytes(endAt - CInt(textStart)) As Byte
                Buffer.BlockCopy(data, CInt(textStart), messageBytes, 0, messageBytes.Length)
                Dim nativeHex = Convert.ToHexString(messageBytes)
                result.Add(New DialogueEntry With {
                    .FileName = name, .BankOffset = spec.Bank,
                    .GroupIndex = spec.Group, .MessageIndex = message,
                    .PointerOffset = ptrPos, .TextOffset = textStart,
                    .Key = $"{name}:{spec.Bank:X8}:{spec.Group:X2}:{message:X2}",
                    .EncodedHex = nativeHex,
                    .Japanese = SaturnJapaneseDecoder.Decode(nativeHex),
                    .Provenance = "Verified Beta v1.24 native bank and pointer table"})
            Next
        Next
        Return result
    End Function

    Public Shared Function ScanBytes(name As String, data As Byte()) As List(Of DialogueEntry)
        Dim result As New List(Of DialogueEntry)
        If data Is Nothing OrElse data.Length < 256 Then Return result
        Dim exactTables = KnownSaturnDialogueLayout.ForFile(name, data)
        If exactTables.Count > 0 Then Return ScanKnownTables(name, data, exactTables)
        Dim byModulo(2047) As List(Of Integer)
        For j = 0 To 2047
            byModulo(j) = New List(Of Integer)()
        Next
        For i = 0 To data.Length - 3
            If data(i) = &HDF AndAlso MessageEnd(data, i) >= 0 Then byModulo(i And 2047).Add(i)
        Next
        Dim groupsByBank As New Dictionary(Of Integer, Integer)
        Dim pos = 0
        While pos + 16 <= data.Length
            If pos Mod 4 <> 0 Then
                pos += 1
                Continue While
            End If
            Dim rel = RelPtr(data, pos)
            If rel < 0 Then
                pos += 4
                Continue While
            End If
            Dim bestBank = -1
            Dim bestLen = 0
            For Each target In byModulo(rel And 2047)
                Dim bank = target - rel
                If bank < 0 OrElse bank > pos OrElse (bank Mod 2048) <> 0 Then Continue For
                Dim count = RunLength(data, pos, bank)
                If count > bestLen Then
                    bestLen = count
                    bestBank = bank
                End If
                If bestLen >= 200 Then Exit For
            Next
            If bestLen < 3 OrElse Not LooksLikeMessageTable(data, pos, bestBank, bestLen) Then
                pos += 4
                Continue While
            End If
            Dim groupIndex = If(groupsByBank.ContainsKey(bestBank), groupsByBank(bestBank), 0)
            groupsByBank(bestBank) = groupIndex + 1
            For msg As Integer = 0 To bestLen - 1
                Dim ptr = pos + msg * 4
                Dim textStart = Hit(data, ptr, bestBank)
                Dim last = MessageEnd(data, textStart)
                Dim message(last - textStart) As Byte
                Buffer.BlockCopy(data, textStart, message, 0, message.Length)
                result.Add(New DialogueEntry With {
                    .FileName = name, .BankOffset = bestBank,
                    .GroupIndex = groupIndex, .MessageIndex = msg,
                    .PointerOffset = ptr, .TextOffset = textStart,
                    .Key = $"{name}:{bestBank:X8}:{groupIndex:X2}:{msg:X2}",
                    .EncodedHex = Convert.ToHexString(message),
                    .Japanese = SaturnJapaneseDecoder.Decode(Convert.ToHexString(message)),
                    .Provenance = "Native pointer table; decoded Japanese glyphs"})
            Next
            pos += bestLen * 4
        End While
        Return result
    End Function
End Class

Public NotInheritable Class LegacyIndex
    Private Sub New()
    End Sub
    Public Shared Function ApplyFromFile(indexPath As String, entries As IList(Of DialogueEntry)) As Integer
        Dim indexed As New Dictionary(Of String, DialogueEntry)(StringComparer.OrdinalIgnoreCase)
        For Each ent In entries
            indexed(ent.FileName & "|" & ent.PointerOffset.ToString() & "|" & ent.TextOffset.ToString()) = ent
        Next
        Dim attached = 0
        Using doc = JsonDocument.Parse(File.ReadAllText(indexPath, Encoding.UTF8))
            Dim records = doc.RootElement.GetProperty("records")
            For Each item In records.EnumerateArray()
                Dim fn = GetString(item, "file")
                Dim txt = GetLong(item, "text_offset")
                Dim ptr = GetLong(item, "pointer_offset")
                Dim label = fn & "|" & ptr.ToString() & "|" & txt.ToString()
                Dim target As DialogueEntry = Nothing
                If Not indexed.TryGetValue(label, target) Then Continue For
                Dim sourceHex = DialogueEncoding.NormalizeHex(GetString(item, "encoded_hex"))
                If Not sourceHex.Equals(DialogueEncoding.NormalizeHex(target.EncodedHex), StringComparison.Ordinal) Then Continue For
                target.Japanese = GetString(item, "source_text")
                target.English = GetString(item, "english_text")
                target.OriginalSha = GetString(item, "source_sha256")
                target.Provenance = "Verified native pointer + imported catalogue"
                attached += 1
            Next
        End Using
        Return attached
    End Function
    Private Shared Function GetString(o As JsonElement, key As String) As String
        Dim p As JsonElement
        If o.TryGetProperty(key, p) AndAlso p.ValueKind = JsonValueKind.String Then Return If(p.GetString(), "")
        Return ""
    End Function
    Private Shared Function GetLong(o As JsonElement, key As String) As Long
        Dim p As JsonElement
        If o.TryGetProperty(key, p) AndAlso p.ValueKind = JsonValueKind.Number Then Return p.GetInt64()
        Return -1
    End Function
End Class

Public NotInheritable Class CsvStore
    Private Sub New()
    End Sub
    Public Shared Function Quote(s As String) As String
        Return """" & If(s, "").Replace("""", """""") & """"
    End Function
    Public Shared Sub Save(path As String, entries As IEnumerable(Of DialogueEntry))
        If File.Exists(path) Then
            Dim backup = path & ".backup_" & DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
            File.Copy(path, backup, False)
        End If
        WriteRows(path, entries)
    End Sub

    Private Shared Sub WriteRows(path As String, entries As IEnumerable(Of DialogueEntry))
        Using w As New StreamWriter(path, False, New UTF8Encoding(True))
            w.WriteLine("Key,File,PointerOffset,TextOffset,EncodedHex,Japanese,English,ReplacementHex,OriginalSha,FitStatus,ReviewNote,TranslationSource")
            For Each e In entries
                Dim fields = New String() {e.Key, e.FileName, e.PointerOffset.ToString(CultureInfo.InvariantCulture), e.TextOffset.ToString(CultureInfo.InvariantCulture), e.EncodedHex, e.Japanese, e.English, e.ReplacementHex, e.OriginalSha, e.TranslationFit, e.TranslationNote, e.TranslationSource}
                w.WriteLine(String.Join(",", fields.Select(Function(s) Quote(s))))
            Next
        End Using
    End Sub

    Public Shared Sub SaveCheckpoint(path As String, entries As IEnumerable(Of DialogueEntry))
        Dim temp = path & ".writing"
        Try
            WriteRows(temp, entries)
            File.Move(temp, path, True)
        Finally
            If File.Exists(temp) Then File.Delete(temp)
        End Try
    End Sub

    Public Shared Function LoadFile(path As String) As List(Of DialogueEntry)
        Dim data = Parse(File.ReadAllText(path, Encoding.UTF8))
        Dim result As New List(Of DialogueEntry)()
        If data.Count = 0 Then Return result
        Dim headers = data(0).Select(Function(x) x.TrimStart(ChrW(&HFEFF))).ToArray()
        Dim getv As Func(Of String(), String, String) = Function(row, col)
            Dim n = Array.FindIndex(headers, Function(h) h.Equals(col, StringComparison.OrdinalIgnoreCase))
            Return If(n < 0 OrElse n >= row.Length, "", row(n))
        End Function
        For Each row In data.Skip(1)
            Dim offset As Long
            Dim pointer As Long
            If Not Long.TryParse(getv(row, "TextOffset"), NumberStyles.Integer, CultureInfo.InvariantCulture, offset) Then Continue For
            If Not Long.TryParse(getv(row, "PointerOffset"), NumberStyles.Integer, CultureInfo.InvariantCulture, pointer) Then Continue For
            Dim encoded = getv(row, "EncodedHex")
            If String.IsNullOrWhiteSpace(encoded) Then Continue For
            Try
                Dim originalBytes = DialogueEncoding.HexToBytes(encoded)
                If originalBytes.Length < 2 OrElse originalBytes(originalBytes.Length - 1) <> &HF Then Continue For
            Catch
                Continue For
            End Try
            result.Add(New DialogueEntry With {
                .Key = getv(row, "Key"), .FileName = getv(row, "File"),
                .PointerOffset = pointer, .TextOffset = offset, .EncodedHex = encoded,
                .Japanese = getv(row, "Japanese"), .English = getv(row, "English"),
                .ReplacementHex = getv(row, "ReplacementHex"), .OriginalSha = getv(row, "OriginalSha"),
                .TranslationFit = getv(row, "FitStatus"), .TranslationNote = getv(row, "ReviewNote"),
                .TranslationSource = getv(row, "TranslationSource")})
        Next
        Return result
    End Function
    Public Shared Function ImportInto(path As String, entries As IList(Of DialogueEntry)) As Integer
        Dim byLocation As New Dictionary(Of String, List(Of DialogueEntry))(StringComparer.OrdinalIgnoreCase)
        For Each e In entries
            Dim key = e.FileName & "|" & e.TextOffset.ToString(CultureInfo.InvariantCulture)
            If Not byLocation.ContainsKey(key) Then byLocation(key) = New List(Of DialogueEntry)
            byLocation(key).Add(e)
        Next
        Dim table = Parse(File.ReadAllText(path, Encoding.UTF8))
        If table.Count = 0 Then Return 0
        Dim heads = table(0).Select(Function(s) s.TrimStart(ChrW(&HFEFF))).ToArray()
        Dim readField As Func(Of String(), String, String) = Function(row, col)
                                                               Dim n = Array.FindIndex(heads, Function(h) h.Equals(col, StringComparison.OrdinalIgnoreCase))
                                                               Return If(n < 0 OrElse n >= row.Length, "", row(n))
                                                           End Function
        Dim count = 0
        For Each row In table.Skip(1)
            Dim name = readField(row, "File")
            Dim off As Long
            If Not Long.TryParse(readField(row, "TextOffset"), NumberStyles.Integer, CultureInfo.InvariantCulture, off) Then Continue For
            Dim match As List(Of DialogueEntry) = Nothing
            If Not byLocation.TryGetValue(name & "|" & off.ToString(CultureInfo.InvariantCulture), match) Then Continue For
            Dim expectedHex = DialogueEncoding.NormalizeHex(readField(row, "EncodedHex"))
            If expectedHex.Length = 0 Then Continue For
            For Each e In match
                If Not DialogueEncoding.NormalizeHex(e.EncodedHex).Equals(expectedHex, StringComparison.Ordinal) Then Continue For
                Dim enText = readField(row, "English")
                If enText.Length > 0 OrElse e.English.Length = 0 Then e.English = enText
                Dim patch = readField(row, "ReplacementHex")
                If patch.Length > 0 Then e.ReplacementHex = patch
                Dim fit = readField(row, "FitStatus")
                If fit.Length > 0 Then e.TranslationFit = fit
                Dim note = readField(row, "ReviewNote")
                If note.Length > 0 Then e.TranslationNote = note
                Dim fromProvider = readField(row, "TranslationSource")
                If fromProvider.Length > 0 Then e.TranslationSource = fromProvider
                Dim jp = readField(row, "Japanese")
                If jp.Length > 0 Then e.Japanese = jp
                count += 1
            Next
        Next
        Return count
    End Function
    Public Shared Function Parse(s As String) As List(Of String())
        Dim result As New List(Of String())
        Dim cells As New List(Of String)
        Dim sb As New StringBuilder()
        Dim quoted As Boolean = False
        Dim i = 0
        While i < s.Length
            Dim c = s(i)
            If c = """"c Then
                If quoted AndAlso i + 1 < s.Length AndAlso s(i + 1) = """"c Then
                    sb.Append(""""c)
                    i += 1
                Else
                    quoted = Not quoted
                End If
            ElseIf c = ","c AndAlso Not quoted Then
                cells.Add(sb.ToString())
                sb.Clear()
            ElseIf (c = ControlChars.Cr OrElse c = ControlChars.Lf) AndAlso Not quoted Then
                If c = ControlChars.Cr AndAlso i + 1 < s.Length AndAlso s(i + 1) = ControlChars.Lf Then i += 1
                cells.Add(sb.ToString())
                sb.Clear()
                If cells.Any(Function(x) x.Length > 0) Then result.Add(cells.ToArray())
                cells.Clear()
            Else
                sb.Append(c)
            End If
            i += 1
        End While
        If quoted Then Throw New InvalidDataException("Unclosed CSV field.")
        cells.Add(sb.ToString())
        If cells.Any(Function(x) x.Length > 0) Then result.Add(cells.ToArray())
        Return result
    End Function
End Class
