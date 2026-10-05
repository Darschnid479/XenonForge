Imports System.IO
Imports System.Buffers.Binary
Imports System.Text

Namespace Engine
    Friend Module BinaryHelpers
        Public Function ReadUInt16LE(data As Byte(), offset As Integer) As UShort
            Return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))
        End Function

        Public Function ReadUInt32LE(data As Byte(), offset As Integer) As UInteger
            Return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4))
        End Function

        Public Function ReadUInt32BE(data As Byte(), offset As Integer) As UInteger
            Return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4))
        End Function

        Public Sub WriteUInt16BE(data As Byte(), offset As Integer, value As UShort)
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value)
        End Sub

        Public Sub WriteUInt32BE(data As Byte(), offset As Integer, value As UInteger)
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value)
        End Sub

        Public Sub WriteUInt32LE(data As Byte(), offset As Integer, value As UInteger)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value)
        End Sub

        Public Sub WriteUInt24BE(data As Byte(), offset As Integer, value As UInteger)
            data(offset) = CByte((value >> 16) And &HFFUI)
            data(offset + 1) = CByte((value >> 8) And &HFFUI)
            data(offset + 2) = CByte(value And &HFFUI)
        End Sub

        Public Sub WriteUtf16BeNullTerminated(data As Byte(), offset As Integer, maxBytes As Integer, value As String)
            If maxBytes < 2 OrElse offset < 0 OrElse offset + maxBytes > data.Length Then Return
            Array.Clear(data, offset, maxBytes)
            If String.IsNullOrEmpty(value) Then Return

            Dim units = value.ToCharArray()
            Dim maxChars = Math.Max(0, (maxBytes - 2) \ 2)
            Dim count = Math.Min(units.Length, maxChars)
            For i = 0 To count - 1
                WriteUInt16BE(data, offset + i * 2, CUShort(AscW(units(i)) And &HFFFF))
            Next
        End Sub

        Public Function ReadExactlyAt(stream As FileStream, offset As Long, count As Integer) As Byte()
            Dim buffer(count - 1) As Byte
            stream.Position = offset
            Dim total = 0
            While total < count
                Dim n = stream.Read(buffer, total, count - total)
                If n <= 0 Then Throw New EndOfStreamException($"Unexpected end of file at 0x{offset + total:X}.")
                total += n
            End While
            Return buffer
        End Function
    End Module
End Namespace
