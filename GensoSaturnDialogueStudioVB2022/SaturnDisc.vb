Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Linq
Imports System.Collections.Generic
Imports System.Buffers.Binary

Public Class SaturnFileEntry
    Public Property Name As String = ""
    Public Property FullName As String = ""
    Public Property Extent As UInteger
    Public Property Length As UInteger
    Public Property IsDirectory As Boolean
End Class

Public Class SaturnDisc
    Implements IDisposable
    Public ReadOnly Property ImagePath As String
    Public ReadOnly Property CuePath As String
    Public ReadOnly Property SectorSize As Integer
    Public ReadOnly Property UserOffset As Integer
    Public ReadOnly Property SectorBias As Long
    Public ReadOnly Property Files As New List(Of SaturnFileEntry)
    Public ReadOnly Property CueFiles As New List(Of String)
    Private ReadOnly inputStream As FileStream
    Private ReadOnly roots As New HashSet(Of UInteger)

    Private Sub New(path As String, cue As String)
        ImagePath = path
        CuePath = cue
        inputStream = New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        Try
            Dim pvd As Byte() = Nothing
            Dim sectorCandidates = New Integer() {2048, 2352, 2448}
            Dim found As Boolean = False
            For Each s In sectorCandidates
                If found Then Exit For
                Dim offsets As Integer() = If(s = 2048, {0}, {16, 24})
                For Each offs In offsets
                    If found Then Exit For
                    'Most discs place the descriptor at track-relative LBA 16. Some
                    'images include a 150-sector pregap, so probe a bounded prefix.
                    For phys As Long = 16 To Math.Min(420L, inputStream.Length \ s - 1L)
                        Dim buffer(2047) As Byte
                        inputStream.Position = phys * s + offs
                        inputStream.ReadExactly(buffer)
                        If buffer(0) <> 1 OrElse buffer(6) <> 1 Then Continue For
                        If Encoding.ASCII.GetString(buffer, 1, 5) <> "CD001" Then Continue For
                        If buffer(156) < 34 Then Continue For
                        Dim rootLba = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(158, 4))
                        Dim rootLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(166, 4))
                        If rootLba < 16UI OrElse rootLength = 0UI OrElse rootLength > 32UI * 1024UI * 1024UI Then Continue For
                        If (phys - 16L + CLng(rootLba)) * s + offs + 2048L > inputStream.Length Then Continue For
                        SectorSize = s
                        UserOffset = offs
                        SectorBias = phys - 16L
                        pvd = buffer
                        found = True
                        Exit For
                    Next
                Next
            Next
            If Not found Then Throw New InvalidDataException("ISO9660 primary volume descriptor (CD001) not found. Open a Saturn Mode1/2352 BIN/CUE or 2048-byte ISO.")
            Dim extent = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(158, 4))
            Dim length = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(166, 4))
            WalkDirectory(extent, length, "", 0)
        Catch
            inputStream.Dispose()
            Throw
        End Try
    End Sub

    Public Shared Function Open(selectedPath As String) As SaturnDisc
        Dim absPath = Path.GetFullPath(selectedPath)
        If Not File.Exists(absPath) Then Throw New FileNotFoundException("Image not found.", absPath)
        Dim cue As String = ""
        Dim image = absPath
        If Path.GetExtension(absPath).Equals(".cue", StringComparison.OrdinalIgnoreCase) Then
            cue = absPath
            Dim images = ReadCueSources(cue)
            If images.Count = 0 Then Throw New InvalidDataException("No FILE entries found in CUE.")
            image = images(0)
        Else
            Dim sidecar = Path.ChangeExtension(absPath, ".cue")
            If File.Exists(sidecar) Then cue = sidecar
        End If
        If Not File.Exists(image) Then Throw New FileNotFoundException("CUE image file is missing.", image)
        Dim disc = New SaturnDisc(image, cue)
        If cue.Length > 0 Then disc.CueFiles.AddRange(ReadCueSources(cue))
        Return disc
    End Function

    Private Shared Function ReadCueSources(cue As String) As List(Of String)
        Dim list As New List(Of String)
        For Each line In File.ReadAllLines(cue)
            Dim m = Regex.Match(line, "^\s*FILE\s+""([^""]+)""\s+\S+", RegexOptions.IgnoreCase)
            If Not m.Success Then m = Regex.Match(line, "^\s*FILE\s+(\S+)\s+\S+", RegexOptions.IgnoreCase)
            If Not m.Success Then Continue For
            Dim src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cue), m.Groups(1).Value.Replace("/"c, Path.DirectorySeparatorChar)))
            If Not list.Contains(src, StringComparer.OrdinalIgnoreCase) Then list.Add(src)
        Next
        Return list
    End Function

    Private Function ReadDataSector(lba As Long) As Byte()
        Dim physical = lba + SectorBias
        If physical < 0 OrElse (physical + 1) * CLng(SectorSize) > inputStream.Length Then Throw New EndOfStreamException("Sector outside disc image: " & lba.ToString())
        Dim bytes(2047) As Byte
        inputStream.Position = physical * SectorSize + UserOffset
        inputStream.ReadExactly(bytes)
        Return bytes
    End Function

    Private Sub WalkDirectory(extent As UInteger, dataLength As UInteger, prefix As String, depth As Integer)
        If depth > 12 OrElse dataLength > 32UI * 1024UI * 1024UI OrElse Not roots.Add(extent) Then Return
        Dim sectors = CInt((CLng(dataLength) + 2047L) \ 2048L)
        If sectors > 16000 Then Return
        For block As Integer = 0 To sectors - 1
            Dim bytes = ReadDataSector(CLng(extent) + block)
            Dim pos = 0
            While pos < 2048
                Dim n = CInt(bytes(pos))
                If n = 0 Then Exit While
                If n < 34 OrElse pos + n > 2048 Then Exit While
                Dim nameLen = CInt(bytes(pos + 32))
                If nameLen > 0 AndAlso pos + 33 + nameLen <= 2048 Then
                    Dim idBytes = bytes.AsSpan(pos + 33, nameLen).ToArray()
                    If Not (nameLen = 1 AndAlso (idBytes(0) = 0 OrElse idBytes(0) = 1)) Then
                        Dim id = Encoding.ASCII.GetString(idBytes)
                        Dim sep = id.IndexOf(";"c)
                        If sep >= 0 Then id = id.Substring(0, sep)
                        id = id.TrimEnd("."c)
                        Dim lba = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 2, 4))
                        Dim length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 10, 4))
                        Dim folder = (bytes(pos + 25) And 2) <> 0
                        Dim ent As New SaturnFileEntry With {.Name = id, .FullName = prefix & id, .Extent = lba, .Length = length, .IsDirectory = folder}
                        Files.Add(ent)
                        If Files.Count > 30000 Then Throw New InvalidDataException("Too many ISO entries.")
                        If folder AndAlso lba > 0UI Then WalkDirectory(lba, length, prefix & id & "/", depth + 1)
                    End If
                End If
                pos += n
            End While
        Next
    End Sub

    Public Function FindFile(fileName As String) As SaturnFileEntry
        Return Files.FirstOrDefault(Function(f) Not f.IsDirectory AndAlso f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
    End Function

    Public Function ReadFileBytes(entry As SaturnFileEntry) As Byte()
        If entry Is Nothing OrElse entry.IsDirectory Then Throw New ArgumentException("Select a data file.")
        If entry.Length > 70UI * 1024UI * 1024UI Then Throw New InvalidDataException("Resource exceeds 70 MiB loading safety limit.")
        Dim output(CInt(entry.Length) - 1) As Byte
        Dim offset = 0
        While offset < output.Length
            Dim sectorBytes = ReadDataSector(CLng(entry.Extent) + offset \ 2048)
            Dim count = Math.Min(2048, output.Length - offset)
            Buffer.BlockCopy(sectorBytes, 0, output, offset, count)
            offset += count
        End While
        Return output
    End Function

    Public Function ReadSlice(entry As SaturnFileEntry, offset As Long, size As Integer) As Byte()
        If offset < 0 OrElse size < 0 OrElse offset + size > CLng(entry.Length) Then Throw New InvalidDataException("File slice out of bounds.")
        Dim result As Byte() = If(size = 0, Array.Empty(Of Byte)(), New Byte(size - 1) {})
        Dim copied = 0
        While copied < size
            Dim pos = offset + copied
            Dim sectorBytes = ReadDataSector(CLng(entry.Extent) + pos \ 2048L)
            Dim within = CInt(pos Mod 2048L)
            Dim n = Math.Min(2048 - within, size - copied)
            Buffer.BlockCopy(sectorBytes, within, result, copied, n)
            copied += n
        End While
        Return result
    End Function

    Public Function RebuildFixedSize(outDirectory As String, patches As IReadOnlyList(Of DialogueEntry), progress As IProgress(Of String)) As String
        If UserOffset <> 0 AndAlso UserOffset <> 16 Then Throw New NotSupportedException("MODE2 data or nonstandard user offset is scan-only; rebuild requires MODE1 or 2048 ISO.")
        If patches.Count = 0 Then Throw New InvalidOperationException("No encoded translations selected for rebuilding.")
        Dim plan = PreparePlan(patches)
        Directory.CreateDirectory(outDirectory)
        If Path.GetFullPath(outDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(Path.GetDirectoryName(ImagePath)).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
            Throw New IOException("Choose a different output directory from the source image directory.")
        End If
        Dim newName = Path.GetFileNameWithoutExtension(ImagePath) & "_Translated" & Path.GetExtension(ImagePath)
        Dim target = Path.Combine(outDirectory, newName)
        If File.Exists(target) Then Throw New IOException("Output already exists: " & target)
        Dim cueOut As String = ""
        If CuePath.Length > 0 Then
            cueOut = Path.Combine(outDirectory, Path.GetFileNameWithoutExtension(CuePath) & "_Translated.cue")
            If File.Exists(cueOut) Then Throw New IOException("Output CUE already exists: " & cueOut)
            For Each f In CueFiles
                If f.Equals(ImagePath, StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not File.Exists(f) Then Throw New FileNotFoundException("Audio track file missing.", f)
                If File.Exists(Path.Combine(outDirectory, Path.GetFileName(f))) Then Throw New IOException("Audio destination already exists: " & Path.GetFileName(f))
            Next
        End If
        progress?.Report("Copying original disc image; original remains untouched...")
        Try
            File.Copy(ImagePath, target, False)
            Using w As New FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                Dim dirty As New SortedDictionary(Of Long, Byte())
                For Each p In plan
                    Dim entry = FindFile(p.FileName)
                    Dim replacementBytes = DialogueEncoding.HexToBytes(p.ReplacementHex)
                    Dim old = DialogueEncoding.HexToBytes(p.EncodedHex)
                    Dim padded = CType(old.Clone(), Byte())
                    Array.Clear(padded, 0, padded.Length)
                    Array.Copy(replacementBytes, padded, replacementBytes.Length)
                    Dim i As Integer = 0
                    While i < padded.Length
                        Dim virtualOff = p.TextOffset + i
                        Dim physical = CLng(entry.Extent) + virtualOff \ 2048L + SectorBias
                        Dim into = CInt(virtualOff Mod 2048L)
                        Dim blockCount = Math.Min(padded.Length - i, 2048 - into)
                        Dim sector As Byte() = Nothing
                        If Not dirty.TryGetValue(physical, sector) Then
                            sector = New Byte(SectorSize - 1) {}
                            w.Position = physical * SectorSize
                            w.ReadExactly(sector)
                            dirty.Add(physical, sector)
                        End If
                        Buffer.BlockCopy(padded, i, sector, UserOffset + into, blockCount)
                        i += blockCount
                    End While
                Next
                progress?.Report($"Recomputing integrity on {dirty.Count:N0} modified sectors...")
                For Each item In dirty
                    If SectorSize <> 2048 Then
                        If UserOffset <> 16 Then Throw New NotSupportedException("Cannot rebuild sector mode.")
                        CdRomEccEdc.RebuildMode1(item.Value)
                    End If
                    w.Position = item.Key * SectorSize
                    w.Write(item.Value)
                Next
                w.Flush(True)
            End Using
            If CuePath.Length > 0 Then
                progress?.Report("Copying audio tracks and rewriting CUE...")
                Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {{ImagePath, newName}}
                For Each original In CueFiles
                    If original.Equals(ImagePath, StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim title = Path.GetFileName(original)
                    File.Copy(original, Path.Combine(outDirectory, title), False)
                    map(original) = title
                Next
                Dim cueLines = File.ReadAllLines(CuePath)
                For idx = 0 To cueLines.Length - 1
                    Dim line = cueLines(idx)
                    Dim matched = Regex.Match(line, "^(\s*FILE\s+)(?:""([^""]+)""|(\S+))(\s+.*)$", RegexOptions.IgnoreCase)
                    If Not matched.Success Then Continue For
                    Dim existing = If(matched.Groups(2).Success, matched.Groups(2).Value, matched.Groups(3).Value)
                    Dim src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(CuePath), existing.Replace("/"c, Path.DirectorySeparatorChar)))
                    Dim newCueFile As String = Nothing
                    If Not map.TryGetValue(src, newCueFile) Then Throw New IOException("Unmapped CUE file: " & src)
                    cueLines(idx) = matched.Groups(1).Value & """" & newCueFile & """" & matched.Groups(4).Value
                Next
                File.WriteAllLines(cueOut, cueLines, Encoding.ASCII)
            End If
            File.WriteAllText(Path.Combine(outDirectory, "BUILD_REPORT.txt"),
                $"Genso Suikoden Saturn Dialogue Studio v0.2.0{Environment.NewLine}" &
                $"Source image: {ImagePath}{Environment.NewLine}" &
                $"Output image: {target}{Environment.NewLine}" &
                $"Sector layout: {SectorSize} bytes, user data offset {UserOffset}, LBA bias {SectorBias}{Environment.NewLine}" &
                $"Patched messages: {plan.Count:N0}{Environment.NewLine}" &
                "Fixed-length only; no pointer, LBA, or file size changes." & Environment.NewLine)
            progress?.Report("Output rebuilt successfully.")
            Return If(cueOut.Length > 0, cueOut, target)
        Catch
            'The output was not validly completed; never delete or change originals.
            If File.Exists(target) Then File.Delete(target)
            If cueOut.Length > 0 AndAlso File.Exists(cueOut) Then File.Delete(cueOut)
            Throw
        End Try
    End Function

    Private Function PreparePlan(patches As IReadOnlyList(Of DialogueEntry)) As List(Of DialogueEntry)
        Dim distinct As New Dictionary(Of String, DialogueEntry)(StringComparer.OrdinalIgnoreCase)
        For Each p In patches
            Dim entry = FindFile(p.FileName)
            If entry Is Nothing Then Throw New FileNotFoundException("No disc resource: " & p.FileName)
            Dim old = DialogueEncoding.HexToBytes(p.EncodedHex)
            Dim replacement = DialogueEncoding.HexToBytes(p.ReplacementHex)
            DialogueEncoding.ValidatePatch(old, replacement, p.Key)
            If p.TextOffset < 0 OrElse p.TextOffset + old.Length > CLng(entry.Length) Then Throw New InvalidDataException("Patch outside file: " & p.Key)
            Dim existing = ReadSlice(entry, p.TextOffset, old.Length)
            If Not existing.SequenceEqual(old) Then Throw New InvalidDataException("Original text mismatch at: " & p.Key)
            Dim key = p.FileName & "|" & p.TextOffset.ToString()
            If distinct.ContainsKey(key) Then
                If Not distinct(key).ReplacementHex.Equals(p.ReplacementHex, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("Conflicting translations for shared message: " & p.Key)
            Else
                distinct.Add(key, p)
            End If
        Next
        For Each g In distinct.Values.GroupBy(Function(x) x.FileName)
            Dim prevEnd As Long = -1
            For Each p In g.OrderBy(Function(x) x.TextOffset)
                If p.TextOffset < prevEnd Then Throw New InvalidDataException("Overlapping dialogue patches: " & p.Key)
                prevEnd = p.TextOffset + DialogueEncoding.HexToBytes(p.EncodedHex).Length
            Next
        Next
        Return distinct.Values.ToList()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        inputStream.Dispose()
    End Sub
End Class
