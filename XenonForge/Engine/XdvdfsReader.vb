Imports System.Collections.Generic
Imports System.Linq
Imports System.IO
Imports System.Text

Namespace Engine
    Public NotInheritable Class XdvdfsReader
        Public Const SectorSize As Long = &H800L
        Private Shared ReadOnly Magic As Byte() = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA")

        Private ReadOnly _stream As FileStream
        Private ReadOnly _partitionOffset As Long
        Private ReadOnly _rootSector As UInteger
        Private ReadOnly _rootSize As UInteger

        Public ReadOnly Property DiscKind As XboxDiscKind
        Public ReadOnly Property PartitionOffset As Long
            Get
                Return _partitionOffset
            End Get
        End Property
        Public ReadOnly Property RootSector As UInteger
            Get
                Return _rootSector
            End Get
        End Property
        Public ReadOnly Property RootSize As UInteger
            Get
                Return _rootSize
            End Get
        End Property

        Public Sub New(stream As FileStream)
            _stream = stream
            Dim detected = DetectPartition(stream)
            DiscKind = detected.Kind
            _partitionOffset = detected.Offset

            Dim descOffset = _partitionOffset + &H20L * SectorSize
            Dim descriptor = BinaryHelpers.ReadExactlyAt(_stream, descOffset, 32)
            If Not descriptor.AsSpan(0, Magic.Length).SequenceEqual(Magic) Then
                Throw New InvalidDataException("XDVDFS volume descriptor is invalid.")
            End If
            _rootSector = BinaryHelpers.ReadUInt32LE(descriptor, 20)
            _rootSize = BinaryHelpers.ReadUInt32LE(descriptor, 24)
        End Sub

        Private Shared Function DetectPartition(stream As FileStream) As (Kind As XboxDiscKind, Offset As Long)
            Dim candidates = {
                (XboxDiscKind.XSF, 0L),
                (XboxDiscKind.XGD2, &HFD90000L),
                (XboxDiscKind.XGD1, &H18300000L),
                (XboxDiscKind.XGD3, &H2080000L)
            }

            For Each candidate In candidates
                Dim absolute = candidate.Item2 + &H20L * SectorSize
                If absolute < 0 OrElse absolute + Magic.Length > stream.Length Then Continue For
                Dim buffer = BinaryHelpers.ReadExactlyAt(stream, absolute, Magic.Length)
                If buffer.AsSpan().SequenceEqual(Magic) Then
                    Return (candidate.Item1, candidate.Item2)
                End If
            Next
            Throw New InvalidDataException("XDVDFS signature not found. This does not look like an Xbox/Xbox 360 game ISO.")
        End Function

        Public Function ReadRootEntries(Optional recursive As Boolean = False) As List(Of XdvdfsEntry)
            Return ReadDirectory(_rootSector, _rootSize, recursive, 0)
        End Function

        Public Function FindRootEntry(name As String) As XdvdfsEntry
            Return ReadRootEntries(False).FirstOrDefault(Function(e) e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        End Function

        Public Function ReadEntryPrefix(entry As XdvdfsEntry, maxBytes As Integer) As Byte()
            Dim count = CInt(Math.Min(CLng(entry.Size), CLng(maxBytes)))
            If count <= 0 Then Return Array.Empty(Of Byte)()
            Return BinaryHelpers.ReadExactlyAt(_stream, _partitionOffset + CLng(entry.Sector) * SectorSize, count)
        End Function


        Public Sub CopyEntryToFile(entry As XdvdfsEntry,
                                   destinationPath As String,
                                   cancellationToken As Threading.CancellationToken)
            If entry Is Nothing Then Throw New ArgumentNullException(NameOf(entry))
            If entry.IsDirectory Then Throw New InvalidOperationException("Cannot copy a directory entry as a file.")

            Dim parent = Path.GetDirectoryName(destinationPath)
            If Not String.IsNullOrWhiteSpace(parent) Then Directory.CreateDirectory(parent)

            Const BufferSize As Integer = 1024 * 1024
            Dim buffer(BufferSize - 1) As Byte
            Dim remaining As Long = entry.Size
            _stream.Position = _partitionOffset + CLng(entry.Sector) * SectorSize

            Using output As New FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan)
                While remaining > 0
                    cancellationToken.ThrowIfCancellationRequested()
                    Dim want = CInt(Math.Min(CLng(buffer.Length), remaining))
                    Dim read = _stream.Read(buffer, 0, want)
                    If read <= 0 Then Throw New EndOfStreamException($"Unexpected end of ISO while extracting {entry.Name}.")
                    output.Write(buffer, 0, read)
                    remaining -= read
                End While
                output.Flush(True)
            End Using
        End Sub

        Public Function GetMaxUsedPrefixSize() As Long
            Dim maxEnd As Long = &H21L * SectorSize
            maxEnd = Math.Max(maxEnd, CLng(_rootSector) * SectorSize + _rootSize)
            For Each entry In ReadRootEntries(True)
                maxEnd = Math.Max(maxEnd, GetEntryMaxEnd(entry))
            Next
            Return maxEnd
        End Function

        Private Function GetEntryMaxEnd(entry As XdvdfsEntry) As Long
            Dim result = CLng(entry.Sector) * SectorSize + entry.Size
            If entry.Children IsNot Nothing Then
                For Each child In entry.Children
                    result = Math.Max(result, GetEntryMaxEnd(child))
                Next
            End If
            Return result
        End Function

        Private Function ReadDirectory(sector As UInteger, size As UInteger, recursive As Boolean, depth As Integer) As List(Of XdvdfsEntry)
            If depth > 64 Then Throw New InvalidDataException("XDVDFS directory nesting is too deep.")
            Dim result As New List(Of XdvdfsEntry)()
            If size = 0 Then Return result

            Dim sectorCount = CInt((CULng(size) + CULng(SectorSize) - 1UL) \ CULng(SectorSize))
            For i = 0 To sectorCount - 1
                Dim absolute = _partitionOffset + (CLng(sector) + i) * SectorSize
                If absolute >= _stream.Length Then Exit For
                Dim available = CInt(Math.Min(SectorSize, _stream.Length - absolute))
                Dim data = BinaryHelpers.ReadExactlyAt(_stream, absolute, available)
                ParseDirectorySector(data, result)
            Next

            If recursive Then
                For Each entry In result.Where(Function(x) x.IsDirectory AndAlso x.Size > 0)
                    entry.Children = ReadDirectory(entry.Sector, entry.Size, True, depth + 1)
                Next
            End If
            Return result
        End Function

        Private Shared Sub ParseDirectorySector(data As Byte(), output As List(Of XdvdfsEntry))
            Dim pos = 0
            While pos + 14 <= data.Length
                Dim left = BinaryHelpers.ReadUInt16LE(data, pos)
                Dim right = BinaryHelpers.ReadUInt16LE(data, pos + 2)
                If left = &HFFFFUS OrElse right = &HFFFFUS Then Exit While

                Dim sector = BinaryHelpers.ReadUInt32LE(data, pos + 4)
                Dim size = BinaryHelpers.ReadUInt32LE(data, pos + 8)
                Dim attrs = data(pos + 12)
                Dim nameLen = CInt(data(pos + 13))
                If nameLen = 0 OrElse pos + 14 + nameLen > data.Length Then Exit While

                Dim name = Encoding.ASCII.GetString(data, pos + 14, nameLen)
                output.Add(New XdvdfsEntry With {
                    .Sector = sector,
                    .Size = size,
                    .Attributes = attrs,
                    .Name = name
                })

                Dim afterEntry = pos + 14 + nameLen
                pos = (afterEntry + 3) And Not 3
            End While
        End Sub
    End Class
End Namespace
