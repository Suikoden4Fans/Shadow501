Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Text
Imports System.Collections.Generic
Imports System.Linq
Imports System.Globalization

Public NotInheritable Class ResourceFolderPatcher
    Private Sub New()
    End Sub
    Public Shared Function MakeCopies(sourceFolder As String, targetFolder As String,
                                      edits As IReadOnlyList(Of DialogueEntry),
                                      progress As IProgress(Of String)) As String
        If Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar).Equals(
            Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
            Throw New IOException("Output folder must differ from original.")
        End If
        Directory.CreateDirectory(targetFolder)
        Dim outputs As New List(Of String)
        Dim changed = 0
        Try
            For Each grp In edits.GroupBy(Function(e) e.FileName, StringComparer.OrdinalIgnoreCase)
                Dim source = Path.Combine(sourceFolder, grp.Key)
                Dim dest = Path.Combine(targetFolder, grp.Key)
                If Not File.Exists(source) Then Throw New FileNotFoundException("Source MURA resource missing.", source)
                If File.Exists(dest) Then Throw New IOException("Output file already exists: " & dest)
                progress?.Report("Validating original bytes: " & grp.Key)
                Dim original = File.ReadAllBytes(source)
                Dim patched = CType(original.Clone(), Byte())
                Dim updates As New Dictionary(Of Long, DialogueEntry)
                For Each e In grp
                    Dim old = DialogueEncoding.HexToBytes(e.EncodedHex)
                    Dim repl = DialogueEncoding.HexToBytes(e.ReplacementHex)
                    DialogueEncoding.ValidatePatch(old, repl, e.Key)
                    If e.TextOffset < 0 OrElse e.TextOffset + old.Length > original.Length Then Throw New InvalidDataException("Message outside source: " & e.Key)
                    If Not original.AsSpan(CInt(e.TextOffset), old.Length).SequenceEqual(old) Then Throw New InvalidDataException("Source byte mismatch at " & e.Key)
                    Dim first As DialogueEntry = Nothing
                    If updates.TryGetValue(e.TextOffset, first) Then
                        If Not String.Equals(DialogueEncoding.NormalizeHex(first.ReplacementHex), DialogueEncoding.NormalizeHex(e.ReplacementHex), StringComparison.Ordinal) Then
                            Throw New InvalidDataException("Conflicting shared-message patches at " & e.Key)
                        End If
                    Else
                        updates.Add(e.TextOffset, e)
                    End If
                Next
                Dim previousEnd As Long = -1
                For Each e In updates.Values.OrderBy(Function(x) x.TextOffset)
                    If e.TextOffset < previousEnd Then Throw New InvalidDataException("Overlapping patches: " & e.Key)
                    Dim old = DialogueEncoding.HexToBytes(e.EncodedHex)
                    Dim repl = DialogueEncoding.HexToBytes(e.ReplacementHex)
                    Array.Clear(patched, CInt(e.TextOffset), old.Length)
                    Array.Copy(repl, 0, patched, CInt(e.TextOffset), repl.Length)
                    previousEnd = e.TextOffset + old.Length
                    changed += 1
                Next
                File.WriteAllBytes(dest, patched)
                outputs.Add(dest)
            Next
            Dim report = Path.Combine(targetFolder, "BUILD_REPORT.txt")
            File.WriteAllText(report,
                "Genso Suikoden Saturn Dialogue Studio v0.2.0" & Environment.NewLine &
                "Mode: patched extracted MURA file copies" & Environment.NewLine &
                "Source: " & sourceFolder & Environment.NewLine &
                "Output: " & targetFolder & Environment.NewLine &
                "Patched messages: " & changed.ToString("N0", CultureInfo.InvariantCulture) & Environment.NewLine &
                "Source resources are unchanged." & Environment.NewLine, Encoding.UTF8)
            Return targetFolder
        Catch
            For Each created In outputs
                If File.Exists(created) Then File.Delete(created)
            Next
            Throw
        End Try
    End Function
End Class
