Imports System.IO

Namespace Engine
    Public Module XboxExecutableParser
        Private Const XexExecutionInfoKey As UInteger = &H40006UI

        Public Function ReadTitleInfo(isoPath As String) As XboxTitleInfo
            Using stream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan)
                Dim fs As New XdvdfsReader(stream)
                Dim info As XboxTitleInfo

                Dim xex = fs.FindRootEntry("default.xex")
                If xex IsNot Nothing Then
                    info = ParseXex(fs.ReadEntryPrefix(xex, 256 * 1024))
                    info.ContentType = XboxContentType.GamesOnDemand
                Else
                    Dim xbe = fs.FindRootEntry("default.xbe")
                    If xbe Is Nothing Then Throw New InvalidDataException("No default.xex or default.xbe was found in the ISO root.")
                    info = ParseXbe(fs.ReadEntryPrefix(xbe, 256 * 1024))
                    info.ContentType = XboxContentType.XboxOriginal
                End If

                info.IsoPath = isoPath
                info.IsoSize = stream.Length
                info.DiscKind = fs.DiscKind
                info.PartitionOffset = fs.PartitionOffset
                info.RootDirectorySector = fs.RootSector
                info.RootDirectorySize = fs.RootSize
                info.UsedDataSize = fs.GetMaxUsedPrefixSize()
                info.DisplayName = GameCatalog.ResolveTitle(info.TitleId, Path.GetFileNameWithoutExtension(isoPath))
                Return info
            End Using
        End Function

        Private Function ParseXex(data As Byte()) As XboxTitleInfo
            If data.Length < 24 OrElse data(0) <> AscW("X"c) OrElse data(1) <> AscW("E"c) OrElse data(2) <> AscW("X"c) OrElse data(3) <> AscW("2"c) Then
                Throw New InvalidDataException("default.xex is missing the XEX2 signature.")
            End If

            Dim fieldCount = BinaryHelpers.ReadUInt32BE(data, 20)
            If fieldCount = 0UI Then Throw New InvalidDataException("XEX contains no optional headers.")
            Dim pos = 24
            For i As Integer = 0 To CInt(fieldCount) - 1
                If pos + 8 > data.Length Then Exit For
                Dim key = BinaryHelpers.ReadUInt32BE(data, pos)
                Dim value = BinaryHelpers.ReadUInt32BE(data, pos + 4)
                pos += 8
                If key <> XexExecutionInfoKey Then Continue For

                Dim off = CInt(value)
                If off < 0 OrElse off + 20 > data.Length Then Throw New InvalidDataException("XEX execution info points outside the executable header.")
                Return New XboxTitleInfo With {
                    .MediaId = BinaryHelpers.ReadUInt32BE(data, off),
                    .Version = BinaryHelpers.ReadUInt32BE(data, off + 4),
                    .BaseVersion = BinaryHelpers.ReadUInt32BE(data, off + 8),
                    .TitleId = BinaryHelpers.ReadUInt32BE(data, off + 12),
                    .Platform = data(off + 16),
                    .ExecutableType = data(off + 17),
                    .DiscNumber = data(off + 18),
                    .DiscCount = data(off + 19)
                }
            Next
            Throw New InvalidDataException("XEX execution info (0x00040006) was not found.")
        End Function

        Private Function ParseXbe(data As Byte()) As XboxTitleInfo
            If data.Length < &H11C + 4 OrElse data(0) <> AscW("X"c) OrElse data(1) <> AscW("B"c) OrElse data(2) <> AscW("E"c) OrElse data(3) <> AscW("H"c) Then
                Throw New InvalidDataException("default.xbe is missing the XBEH signature.")
            End If

            Dim baseAddress = BinaryHelpers.ReadUInt32LE(data, &H104)
            Dim certificateAddress = BinaryHelpers.ReadUInt32LE(data, &H118)
            Dim certOffset64 = CLng(certificateAddress) - CLng(baseAddress)
            If certOffset64 < 0 OrElse certOffset64 + 12 > data.Length Then Throw New InvalidDataException("XBE certificate offset is outside the executable header.")
            Dim certOffset = CInt(certOffset64)
            Dim version As UInteger = 0
            If certOffset + 180 <= data.Length Then version = BinaryHelpers.ReadUInt32LE(data, certOffset + 176)

            Return New XboxTitleInfo With {
                .TitleId = BinaryHelpers.ReadUInt32LE(data, certOffset + 8),
                .MediaId = 0UI,
                .Version = version,
                .DiscNumber = 1,
                .DiscCount = 1
            }
        End Function
    End Module
End Namespace
