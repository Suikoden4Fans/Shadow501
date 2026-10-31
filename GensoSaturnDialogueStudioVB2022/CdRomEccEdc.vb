Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Buffers.Binary

'CD-ROM MODE1 EDC/ECC. Regenerate both P and Q for every edited 2352-byte
'sector. Subchannel bytes (2448 layout) remain unchanged.
Public NotInheritable Class CdRomEccEdc
    Private Sub New()
    End Sub
    Private Shared ReadOnly forward As Byte() = BuildForward()
    Private Shared ReadOnly backward As Byte() = BuildBackward(forward)
    Private Shared ReadOnly edcTable As UInteger() = BuildEdcTable()

    Private Shared Function BuildForward() As Byte()
        Dim t(255) As Byte
        For i = 0 To 255
            Dim j = i << 1
            If (i And &H80) <> 0 Then j = j Xor &H11D
            t(i) = CByte(j And &HFF)
        Next
        Return t
    End Function

    Private Shared Function BuildBackward(fwd As Byte()) As Byte()
        Dim t(255) As Byte
        For i = 0 To 255
            t(i Xor CInt(fwd(i))) = CByte(i)
        Next
        Return t
    End Function

    Private Shared Function BuildEdcTable() As UInteger()
        Dim t(255) As UInteger
        For i = 0 To 255
            Dim value As UInteger = CUInt(i)
            For bit = 0 To 7
                If (value And 1UI) <> 0UI Then
                    value = (value >> 1) Xor &HD8018001UI
                Else
                    value = value >> 1
                End If
            Next
            t(i) = value
        Next
        Return t
    End Function

    Public Shared Sub RebuildMode1(sector As Byte())
        If sector Is Nothing OrElse sector.Length < 2352 Then Throw New InvalidDataException("Not a full raw sector.")
        If sector(15) <> 1 Then Throw New InvalidDataException("Sector is not MODE1/2352. Unsafe to rebuild without mode-specific ECC.")
        Dim value As UInteger = 0UI
        For i = 0 To 2063
            Dim index = CInt((value Xor CUInt(sector(i))) And &HFFUI)
            value = (value >> 8) Xor edcTable(index)
        Next
        BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(2064, 4), value)
        Array.Clear(sector, 2068, 8)
        EccCompute(sector, 12, 86, 24, 2, 86, 2076)
        EccCompute(sector, 12, 52, 43, 86, 88, 2248)
    End Sub

    Private Shared Sub EccCompute(src As Byte(), start As Integer, majorCount As Integer,
                                  minorCount As Integer, majorMult As Integer,
                                  minorInc As Integer, outputOffset As Integer)
        Dim size = majorCount * minorCount
        For major = 0 To majorCount - 1
            Dim index = ((major >> 1) * majorMult) + (major And 1)
            Dim a = 0
            Dim b = 0
            For minor = 0 To minorCount - 1
                Dim v = CInt(src(start + index))
                index += minorInc
                If index >= size Then index -= size
                a = a Xor v
                b = b Xor v
                a = CInt(forward(a))
            Next
            a = CInt(backward(CInt(forward(a)) Xor b))
            src(outputOffset + major) = CByte(a)
            src(outputOffset + major + majorCount) = CByte(a Xor b)
        Next
    End Sub
End Class
