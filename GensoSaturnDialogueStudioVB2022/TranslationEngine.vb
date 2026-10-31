Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Linq
Imports System.Collections.Generic
Imports System.Globalization

Public Class AutoTranslateSettings
    Public Property Backend As String = "Local Ollama"
    Public Property Model As String = "qwen2.5:7b"
    Public Property ContactEmail As String = ""
    Public Property Endpoint As String = "http://127.0.0.1:11434"
End Class

Public Class TranslationDraft
    Public Property Text As String = ""
    Public Property Status As String = "REVIEW"
    Public Property Note As String = ""
    Public Property EstimatedBytes As Integer
    Public Property NativeCapacity As Integer
End Class

Public NotInheritable Class TranslationRules
    Private Sub New()
    End Sub
    Private Shared ReadOnly tags As New Regex("\[[0-9A-Fa-f]{2}(?:[\s\-][0-9A-Fa-f]{2})*\]", RegexOptions.Compiled)
    Private Shared ReadOnly unknownKanji As New Regex("\[0[12]\s+[0-9A-Fa-f]{2}\]", RegexOptions.Compiled)

    Public Shared Function PrepareJapanese(japanese As String) As String
        Dim source = If(japanese, "").Replace(vbCr, "")
        Return unknownKanji.Replace(source, Function(m)
                              Dim code = m.Value.Trim("["c, "]"c).Replace(" ", "").ToUpperInvariant()
                              Dim value = SaturnJapaneseDecoder.GetMappedGlyph(code)
                              Return If(value IsNot Nothing, value, "□")
                          End Function)
    End Function

    Public Shared Function ControlSequence(source As String) As List(Of String)
        Dim list As New List(Of String)
        For Each match As Match In tags.Matches(If(source, ""))
            Dim code = Regex.Replace(match.Value.ToUpperInvariant(), "[\s\-]", "")
            If code.StartsWith("01") OrElse code.StartsWith("02") Then Continue For
            list.Add(code)
        Next
        Return list
    End Function
    Public Shared Function SameControls(original As String, translation As String) As Boolean
        Return ControlSequence(original).SequenceEqual(ControlSequence(translation))
    End Function

    Public Shared Function PayloadBudget(hex As String) As Integer
        Dim raw = DialogueEncoding.HexToBytes(hex)
        If raw.Length = 0 Then Return 0
        Dim reserve = 1
        If raw(0) = &HDF Then reserve += 1
        If raw.Length >= 2 AndAlso raw(raw.Length - 2) = &HE0 Then reserve += 1
        Return Math.Max(0, raw.Length - reserve)
    End Function

    Public Shared Function EstimatedEncodedBytes(english As String) As Integer
        Dim plain = If(english, "").Replace(vbCr, "")
        Dim bytes = 0
        For Each match As Match In tags.Matches(plain)
            bytes += Regex.Replace(match.Value, "[^0-9A-Fa-f]", "").Length \ 2
        Next
        bytes += tags.Replace(plain, "").Length
        Return bytes
    End Function

    Public Shared Function NormalizeEnglish(value As String) As String
        Dim output = If(value, "").Trim()
        If output.Contains("</think>", StringComparison.OrdinalIgnoreCase) Then
            output = output.Substring(output.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase) + 8).Trim()
        End If
        output = Regex.Replace(output, "^(English(?: translation)?|Translation)\s*:\s*", "", RegexOptions.IgnoreCase)
        output = output.Replace(ChrW(&H2019), "'"c).Replace(ChrW(&H2018), "'"c)
        output = output.Replace(ChrW(&H201C), """"c).Replace(ChrW(&H201D), """"c)
        output = output.Replace(ChrW(&H2014), "-"c).Replace(ChrW(&H2013), "-"c)
        output = output.Replace("…", "...")
        Return output.Trim()
    End Function

    Public Shared Function CheckDraft(japanese As String, hex As String, english As String) As TranslationDraft
        Dim draft As New TranslationDraft With {.Text = NormalizeEnglish(english)}
        Dim source = PrepareJapanese(japanese)
        draft.NativeCapacity = PayloadBudget(hex)
        draft.EstimatedBytes = EstimatedEncodedBytes(draft.Text)
        Dim reasons As New List(Of String)
        If draft.Text.Length = 0 Then reasons.Add("Empty translation")
        If Not SameControls(source, draft.Text) Then reasons.Add("Control tags changed or are not in original order")
        If draft.EstimatedBytes > draft.NativeCapacity Then reasons.Add($"Estimated bytes {draft.EstimatedBytes} exceed original payload {draft.NativeCapacity}")
        If draft.Text.Any(Function(c) AscW(c) > 127) Then reasons.Add("Non-ASCII requires native glyph verification")
        If source.Contains("□") Then reasons.Add("Source has undecoded Japanese glyph(s)")
        If reasons.Count = 0 Then
            draft.Status = "EST FIT"
            draft.Note = $"Estimated {draft.EstimatedBytes}/{draft.NativeCapacity} bytes. NOT native-encoded or confirmed in game."
        Else
            draft.Status = If(draft.EstimatedBytes > draft.NativeCapacity, "OVER LIMIT", "REVIEW")
            draft.Note = String.Join("; ", reasons)
        End If
        Return draft
    End Function
End Class

Public Class SaturnAutoTranslator
    Implements IDisposable

    Private ReadOnly client As New HttpClient()
    Private ReadOnly settings As AutoTranslateSettings
    Private Shared ReadOnly tags As New Regex("(\[[0-9A-Fa-f]{2}(?:[\s\-][0-9A-Fa-f]{2})*\])", RegexOptions.Compiled)

    Public Sub New(opts As AutoTranslateSettings)
        settings = opts
        client.Timeout = TimeSpan.FromMinutes(7)
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SuikodenSaturnDialogueStudio/0.2.2")
    End Sub

    Public Async Function TranslateToCapacityAsync(japanese As String, hex As String,
                                                    ct As CancellationToken) As Task(Of TranslationDraft)
        Dim clean = TranslationRules.PrepareJapanese(japanese)
        Dim capacity = TranslationRules.PayloadBudget(hex)
        Dim english = TranslationRules.NormalizeEnglish(Await TranslateTextAsync(clean, capacity, ct).ConfigureAwait(False))
        If Not TranslationRules.SameControls(clean, english) Then
            english = Await TranslateByPartsAsync(clean, capacity, ct).ConfigureAwait(False)
        End If
        Dim draft = TranslationRules.CheckDraft(japanese, hex, english)
        If draft.Status = "OVER LIMIT" AndAlso settings.Backend = "Local Ollama" Then
            For attempt = 1 To 2
                Dim prompt = "Shorten this English Suikoden game dialogue to fit " &
                    capacity.ToString(CultureInfo.InvariantCulture) & " text bytes or fewer. " &
                    "Use ASCII punctuation, retain plot meaning and speaker names. All [HH] and [HH HH] control tags must remain in EXACT same order, " &
                    "unchanged. Do NOT remove control codes. Reply ONLY with shorter English. " &
                    vbLf & "ORIGINAL JAPANESE: " & clean & vbLf & "CURRENT ENGLISH: " & draft.Text
                Dim shorter = TranslationRules.NormalizeEnglish(Await OllamaRequestAsync(prompt, ct).ConfigureAwait(False))
                If TranslationRules.SameControls(clean, shorter) Then
                    Dim candidate = TranslationRules.CheckDraft(japanese, hex, shorter)
                    If candidate.EstimatedBytes < draft.EstimatedBytes Then draft = candidate
                    If draft.Status <> "OVER LIMIT" Then Exit For
                End If
            Next
        End If
        Return draft
    End Function

    Private Async Function TranslateTextAsync(source As String, capacity As Integer,
                                              ct As CancellationToken) As Task(Of String)
        If source.Trim().Length = 0 Then Return source
        If settings.Backend = "Local Ollama" Then
            Dim prompt = "Translate this Japanese text from Genso Suikoden into natural, compact English. " &
                "Use canonical names: Gremio, Teo, Viktor, Flik, Odessa, Mathiu, Young Master. " &
                "Keep ALL [HH] and [HH HH] bracket control tags exactly unchanged and in the same order. " &
                "Do not add or remove bracket tags. □ marks unresolved source glyph(s); infer cautiously. " &
                "The game has approximately " & capacity.ToString(CultureInfo.InvariantCulture) &
                " bytes available for English characters and preserved tags. Use concise ASCII, no Markdown, no commentary. Output ONLY English." &
                vbLf & "JAPANESE: " & source
            Return Await OllamaRequestAsync(prompt, ct).ConfigureAwait(False)
        End If
        If settings.Backend = "MyMemory (free online)" Then
            Return Await MyMemoryParagraphAsync(source, ct).ConfigureAwait(False)
        End If
        Throw New InvalidOperationException("Unknown translation provider.")
    End Function

    Private Async Function OllamaRequestAsync(prompt As String, ct As CancellationToken) As Task(Of String)
        Dim baseUrl = settings.Endpoint.Trim().TrimEnd("/"c)
        If Not baseUrl.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) AndAlso
           Not baseUrl.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("For privacy, local Ollama must use localhost or 127.0.0.1.")
        End If
        Dim json = JsonSerializer.Serialize(New With {.model = settings.Model, .prompt = prompt,
                                                       .stream = False, .options = New With {.temperature = 0.15, .num_predict = 700}})
        Using request As New HttpRequestMessage(HttpMethod.Post, baseUrl & "/api/generate")
            request.Content = New StringContent(json, Encoding.UTF8, "application/json")
            Using response = Await client.SendAsync(request, ct).ConfigureAwait(False)
                Dim received = Await response.Content.ReadAsStringAsync(ct).ConfigureAwait(False)
                If Not response.IsSuccessStatusCode Then Throw New InvalidOperationException(
                    $"Ollama HTTP {CInt(response.StatusCode)}: {received.Substring(0, Math.Min(330, received.Length))}")
                Using doc = JsonDocument.Parse(received)
                    Return If(doc.RootElement.GetProperty("response").GetString(), "")
                End Using
            End Using
        End Using
    End Function

    Private Async Function TranslateByPartsAsync(source As String, capacity As Integer,
                                                ct As CancellationToken) As Task(Of String)
        Dim parts = tags.Split(source)
        Dim result As New StringBuilder
        Dim count = Math.Max(1, parts.Where(Function(x) x.Length > 0 AndAlso Not x.StartsWith("[")).Count())
        For Each part In parts
            ct.ThrowIfCancellationRequested()
            If part = "" Then Continue For
            If tags.IsMatch(part) AndAlso part.StartsWith("[") AndAlso part.EndsWith("]") Then
                result.Append(part)
            Else
                result.Append(TranslationRules.NormalizeEnglish(
                    Await TranslateTextAsync(part, Math.Max(10, capacity \ count), ct).ConfigureAwait(False)))
            End If
        Next
        Return result.ToString()
    End Function

    Private Async Function MyMemoryParagraphAsync(source As String, ct As CancellationToken) As Task(Of String)
        Dim result As New StringBuilder()
        For Each part In tags.Split(source)
            ct.ThrowIfCancellationRequested()
            If part = "" Then Continue For
            If tags.IsMatch(part) AndAlso part.StartsWith("[") AndAlso part.EndsWith("]") Then
                result.Append(part)
            Else
                Dim chunk As New StringBuilder()
                For Each c In part
                    If Encoding.UTF8.GetByteCount(chunk.ToString() & c) > 400 AndAlso chunk.Length > 0 Then
                        result.Append(Await MyMemoryRequestAsync(chunk.ToString(), ct).ConfigureAwait(False))
                        chunk.Clear()
                    End If
                    chunk.Append(c)
                Next
                If chunk.Length > 0 Then result.Append(Await MyMemoryRequestAsync(chunk.ToString(), ct).ConfigureAwait(False))
            End If
        Next
        Return result.ToString()
    End Function

    Private Async Function MyMemoryRequestAsync(source As String, ct As CancellationToken) As Task(Of String)
        If String.IsNullOrWhiteSpace(source) Then Return source
        Dim url = "https://api.mymemory.translated.net/get?q=" & Uri.EscapeDataString(source) & "&langpair=ja%7Cen"
        If settings.ContactEmail.Length > 0 Then url &= "&de=" & Uri.EscapeDataString(settings.ContactEmail)
        Using response = Await client.GetAsync(url, ct).ConfigureAwait(False)
            If Not response.IsSuccessStatusCode Then Throw New InvalidOperationException(
                $"MyMemory HTTP {CInt(response.StatusCode)}; the daily limit may have been reached.")
            Dim reply = Await response.Content.ReadAsStringAsync(ct).ConfigureAwait(False)
            Using doc = JsonDocument.Parse(reply)
                Dim code As JsonElement
                If doc.RootElement.TryGetProperty("responseStatus", code) AndAlso
                   code.ValueKind = JsonValueKind.Number AndAlso code.GetInt32() <> 200 Then
                    Throw New InvalidOperationException("MyMemory limit reached. Save work and resume another time.")
                End If
                Return WebUtility.HtmlDecode(If(doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString(), ""))
            End Using
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        client.Dispose()
    End Sub
End Class
