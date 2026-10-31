Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Buffers.Binary
Imports System.Collections.Generic

Public NotInheritable Class AppSelfTests
    Private Sub New()
    End Sub

    Public Shared Function Run() As Integer
        Dim folder = Path.Combine(Path.GetTempPath(), "saturn_dialogue_vb2022_tests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            TestCsvAndEncoding(folder)
            TestDisc(folder, False)
            TestDisc(folder, True)
            TestFolderPatch(folder)
            Console.WriteLine("PASS: CSV, glyph encoder, ISO9660 scanning, MODE1/2352 scanning, ECC/EDC rebuild, BIN/CUE output, MURA backup patches.")
            Return 0
        Catch ex As Exception
            Console.Error.WriteLine("FAIL: " & ex.ToString())
            Return 1
        Finally
            Try
                Directory.Delete(folder, True)
            Catch
            End Try
        End Try
    End Function

    Private Shared Sub Check(ok As Boolean, description As String)
        If Not ok Then Throw New InvalidDataException("Test failure: " & description)
    End Sub

    Private Shared Function SampleMura() As Byte()
        Dim mura(4095) As Byte
        For n = 0 To 2
            Dim start = 64 + n * 32
            mura(start) = &HDF
            mura(start + 1) = CByte(&H41 + n)
            mura(start + 2) = &HF
            Dim pointerPos = &H180 + n * 4
            mura(pointerPos) = 0
            mura(pointerPos + 1) = &H20
            mura(pointerPos + 2) = 0
            mura(pointerPos + 3) = CByte(start)
        Next
        Return mura
    End Function

    Private Shared Function IsoRecord(id As Byte(), lba As UInteger, size As UInteger, directoryFlag As Boolean) As Byte()
        Dim length = 33 + id.Length + If(id.Length Mod 2 = 0, 1, 0)
        Dim r(length - 1) As Byte
        r(0) = CByte(length)
        r(1) = 0
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(2, 4), lba)
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(6, 4), lba)
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(10, 4), size)
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(14, 4), size)
        r(25) = If(directoryFlag, CByte(2), CByte(0))
        r(28) = 1 : r(31) = 1
        r(32) = CByte(id.Length)
        Buffer.BlockCopy(id, 0, r, 33, id.Length)
        Return r
    End Function

    Private Shared Function IsoFixture() As Byte()
        Dim iso(28 * 2048 - 1) As Byte
        Dim pvd(2047) As Byte
        pvd(0) = 1
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1)
        pvd(6) = 1
        BinaryPrimitives.WriteUInt32LittleEndian(pvd.AsSpan(80, 4), 28UI)
        BinaryPrimitives.WriteUInt32BigEndian(pvd.AsSpan(84, 4), 28UI)
        BinaryPrimitives.WriteUInt16LittleEndian(pvd.AsSpan(128, 2), 2048US)
        BinaryPrimitives.WriteUInt16BigEndian(pvd.AsSpan(130, 2), 2048US)
        Dim rootRec = IsoRecord({CByte(0)}, 20UI, 2048UI, True)
        Buffer.BlockCopy(rootRec, 0, pvd, 156, rootRec.Length)
        Buffer.BlockCopy(pvd, 0, iso, 16 * 2048, pvd.Length)
        Dim terminal(2047) As Byte
        terminal(0) = 255 : terminal(6) = 1
        Encoding.ASCII.GetBytes("CD001").CopyTo(terminal, 1)
        Buffer.BlockCopy(terminal, 0, iso, 17 * 2048, terminal.Length)
        Dim dot = IsoRecord({CByte(0)}, 20UI, 2048UI, True)
        Dim dotdot = IsoRecord({CByte(1)}, 20UI, 2048UI, True)
        Dim fileRec = IsoRecord(Encoding.ASCII.GetBytes("MURA_A1.BIN;1"), 24UI, 4096UI, False)
        Dim cursor = 20 * 2048
        Buffer.BlockCopy(dot, 0, iso, cursor, dot.Length) : cursor += dot.Length
        Buffer.BlockCopy(dotdot, 0, iso, cursor, dotdot.Length) : cursor += dotdot.Length
        Buffer.BlockCopy(fileRec, 0, iso, cursor, fileRec.Length)
        Buffer.BlockCopy(SampleMura(), 0, iso, 24 * 2048, 4096)
        Return iso
    End Function

    Private Shared Sub TestDisc(tempFolder As String, raw As Boolean)
        Dim fixturePath As String = Path.Combine(tempFolder, If(raw, "fixture.bin", "fixture.iso"))
        Dim payload = IsoFixture()
        If raw Then
            Using output As New FileStream(fixturePath, FileMode.CreateNew)
                For lba = 0 To 27
                    Dim sector(2351) As Byte
                    sector(0) = 0
                    For i = 1 To 10
                        sector(i) = &HFF
                    Next
                    sector(11) = 0
                    sector(12) = 0 : sector(13) = 2
                    sector(14) = CByte(lba)
                    sector(15) = 1
                    Buffer.BlockCopy(payload, lba * 2048, sector, 16, 2048)
                    CdRomEccEdc.RebuildMode1(sector)
                    output.Write(sector)
                Next
            End Using
            File.WriteAllText(Path.Combine(tempFolder, "fixture.cue"),
                "FILE ""fixture.bin"" BINARY" & vbCrLf &
                "  TRACK 01 MODE1/2352" & vbCrLf &
                "    INDEX 01 00:00:00" & vbCrLf)
        Else
            File.WriteAllBytes(fixturePath, payload)
        End If
        Dim toOpen = If(raw, Path.Combine(tempFolder, "fixture.cue"), fixturePath)
        Dim outDir = Path.Combine(tempFolder, If(raw, "rawOutput", "isoOutput"))
        Using disc = SaturnDisc.Open(toOpen)
            Check(disc.SectorSize = If(raw, 2352, 2048), "image sector detection")
            Dim mura = disc.FindFile("MURA_A1.BIN")
            Check(mura IsNot Nothing, "disc MURA discovery")
            Dim entries = NativeDialogueScanner.ScanBytes(mura.Name, disc.ReadFileBytes(mura))
            Check(entries.Count = 3, "pointer scanner should find three dialogues")
            Check(entries(0).EncodedHex = "DF410F", "native text correct")
            entries(0).ReplacementHex = "DF440F"
            Dim outPath = disc.RebuildFixedSize(outDir, {entries(0)}, Nothing)
            Check(File.Exists(outPath), "rebuilt output present")
        End Using
        Using rebuilt = SaturnDisc.Open(If(raw, Path.Combine(outDir, "fixture_Translated.cue"), Path.Combine(outDir, "fixture_Translated.iso")))
            Dim data = rebuilt.ReadFileBytes(rebuilt.FindFile("MURA_A1.BIN"))
            Check(data(64) = &HDF AndAlso data(65) = &H44 AndAlso data(66) = &HF, "patched dialogue roundtrip")
            Check(data(97) = &H42, "neighbor messages preserved")
        End Using
        Using original = SaturnDisc.Open(toOpen)
            Dim data = original.ReadFileBytes(original.FindFile("MURA_A1.BIN"))
            Check(data(65) = &H41, "original image unchanged")
        End Using
        Console.WriteLine("PASS: " & If(raw, "raw MODE1 BIN/CUE", "ISO 2048") & " discovery, scan and rebuild")
    End Sub

    Private Shared Sub TestFolderPatch(folder As String)
        Dim sourceDir = Path.Combine(folder, "muraInput")
        Dim outputDir = Path.Combine(folder, "muraOutput")
        Directory.CreateDirectory(sourceDir)
        Dim fileName = "MURA_B1.BIN"
        File.WriteAllBytes(Path.Combine(sourceDir, fileName), SampleMura())
        Dim r = NativeDialogueScanner.ScanBytes(fileName, SampleMura())(0)
        r.ReplacementHex = "DF4F0F"
        ResourceFolderPatcher.MakeCopies(sourceDir, outputDir, {r}, Nothing)
        Check(File.ReadAllBytes(Path.Combine(sourceDir, fileName))(65) = &H41, "MURA original unchanged")
        Check(File.ReadAllBytes(Path.Combine(outputDir, fileName))(65) = &H4F, "MURA patch copied")
    End Sub

    Private Shared Sub TestCsvAndEncoding(folder As String)
        Dim r = New DialogueEntry With {.Key = "Test", .FileName = "MURA_A1.BIN", .TextOffset = 10, .PointerOffset = 100,
            .EncodedHex = "DF410F", .Japanese = "おはよう", .English = "Hello," & vbLf & "world"}
        Dim csvPath As String = Path.Combine(folder, "project.csv")
        CsvStore.Save(csvPath, {r})
        Dim reload = New DialogueEntry With {.Key = r.Key, .FileName = r.FileName, .TextOffset = r.TextOffset, .PointerOffset = r.PointerOffset, .EncodedHex = r.EncodedHex}
        Dim count = CsvStore.ImportInto(csvPath, {reload})
        Check(count = 1 AndAlso reload.English = r.English, "CSV multiline quoting and import")
        Dim mapping As New Dictionary(Of Char, Byte()) From {{"A"c, {CByte(&H41)}}, {"!"c, {CByte(&H21)}}}
        Dim bytes = DialogueEncoding.EncodeEnglish("A[04 09][0E]!", mapping)
        Check(Convert.ToHexString(bytes) = "DF4104090E210F", "binary control token ordering")
    End Sub
End Class
