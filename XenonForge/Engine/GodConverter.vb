Imports System.Linq
Imports System.Collections.Generic
Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography

Namespace Engine
    Public Enum GodTrimMode
        SmartTrim
        FullImage
    End Enum

    Public NotInheritable Class GodConverter
        Private Const BlockSize As Integer = &H1000
        Private Const BlocksPerPart As Long = &HA1C4L
        Private Const BlocksPerSubpart As Integer = &HCC
        Private Const SubpartsPerPart As Integer = &HCB
        Private Const SubpartSize As Integer = BlockSize * BlocksPerSubpart
        Private Const HashListSize As Integer = &H1000
        Private Const FullPartBlocks As Long = BlocksPerPart + SubpartsPerPart + 1L
        Private Const HeaderSize As Integer = &HB000

        Public Async Function ConvertAsync(info As XboxTitleInfo,
                                           outputRoot As String,
                                           trimMode As GodTrimMode,
                                           progress As IProgress(Of ConversionProgress),
                                           cancellationToken As Threading.CancellationToken) As Task(Of GodConversionResult)
            Return Await Task.Run(Function() ConvertInternal(info, outputRoot, trimMode, progress, cancellationToken), cancellationToken)
        End Function

        Private Function ConvertInternal(info As XboxTitleInfo,
                                         outputRoot As String,
                                         trimMode As GodTrimMode,
                                         progress As IProgress(Of ConversionProgress),
                                         cancellationToken As Threading.CancellationToken) As GodConversionResult
            If info Is Nothing Then Throw New ArgumentNullException(NameOf(info))
            If Not File.Exists(info.IsoPath) Then Throw New FileNotFoundException("ISO file was not found.", info.IsoPath)
            Directory.CreateDirectory(outputRoot)

            Dim dataSize As Long
            If trimMode = GodTrimMode.SmartTrim Then
                dataSize = info.UsedDataSize
            Else
                dataSize = info.IsoSize - info.PartitionOffset
            End If
            If dataSize <= 0 Then Throw New InvalidDataException("The ISO has no usable game partition data.")

            Dim blockCount = CeilingDiv(dataSize, BlockSize)
            Dim partCount = CeilingDiv(blockCount, BlocksPerPart)
            If partCount <= 0 Then Throw New InvalidDataException("Could not calculate GOD partition count.")

            Dim titleFolder = Path.Combine(outputRoot, info.TitleIdHex)
            Dim contentFolder = Path.Combine(titleFolder, info.ContentTypeHex)
            Dim dataFolder = Path.Combine(contentFolder, info.MediaIdHex & ".data")
            Dim headerFile = Path.Combine(contentFolder, info.MediaIdHex)

            If Directory.Exists(dataFolder) Then Directory.Delete(dataFolder, True)
            Directory.CreateDirectory(dataFolder)
            If File.Exists(headerFile) Then File.Delete(headerFile)

            Report(progress, 1, "Preparing GOD layout", 0, CInt(partCount), 0, dataSize)
            Dim partSizes As New List(Of Long)()

            Using iso As New FileStream(info.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.RandomAccess)
                For partIndex As Long = 0 To partCount - 1
                    cancellationToken.ThrowIfCancellationRequested()
                    Dim partPath = Path.Combine(dataFolder, $"Data{partIndex:0000}")
                    Dim partSize = WritePart(iso, info.PartitionOffset, dataSize, partIndex, partPath, progress, CInt(partCount), cancellationToken)
                    partSizes.Add(partSize)
                    Dim pct = CInt(Math.Min(90L, 5L + ((partIndex + 1L) * 82L \ Math.Max(1L, partCount))))
                    Report(progress, pct, $"Wrote Data{partIndex:0000}", CInt(partIndex + 1), CInt(partCount), Math.Min(dataSize, (partIndex + 1L) * BlocksPerPart * BlockSize), dataSize)
                Next
            End Using

            cancellationToken.ThrowIfCancellationRequested()
            Report(progress, 92, "Building MHT hash chain", CInt(partCount), CInt(partCount), dataSize, dataSize)
            Dim rootHash = BuildMhtChain(dataFolder, partCount)

            cancellationToken.ThrowIfCancellationRequested()
            Report(progress, 96, "Writing LIVE/STFS header", CInt(partCount), CInt(partCount), dataSize, dataSize)
            WriteConHeader(headerFile, info, blockCount, partCount, partSizes, rootHash)

            Dim totalBytes = partSizes.Sum() + New FileInfo(headerFile).Length
            Report(progress, 100, "Complete", CInt(partCount), CInt(partCount), dataSize, dataSize)

            Return New GodConversionResult With {
                .Info = info,
                .OutputTitleFolder = titleFolder,
                .HeaderFile = headerFile,
                .DataFolder = dataFolder,
                .TotalBytes = totalBytes
            }
        End Function

        Private Function WritePart(iso As FileStream,
                                   partitionOffset As Long,
                                   dataSize As Long,
                                   partIndex As Long,
                                   outputPath As String,
                                   progress As IProgress(Of ConversionProgress),
                                   totalParts As Integer,
                                   cancellationToken As Threading.CancellationToken) As Long
            Dim partRelativeStart = partIndex * BlocksPerPart * BlockSize
            If partRelativeStart >= dataSize Then Return 0

            Using output As New FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan)
                Dim master As New GodHashList()
                output.Write(New Byte(HashListSize - 1) {}, 0, HashListSize)

                Dim relativePos = partRelativeStart
                Dim buffer(SubpartSize - 1) As Byte

                For subpart = 0 To SubpartsPerPart - 1
                    cancellationToken.ThrowIfCancellationRequested()
                    If relativePos >= dataSize Then Exit For

                    Dim want = CInt(Math.Min(CLng(SubpartSize), dataSize - relativePos))
                    iso.Position = partitionOffset + relativePos
                    Dim read = ReadUpTo(iso, buffer, want)
                    If read <= 0 Then Exit For

                    Dim subHash As New GodHashList()
                    Dim blockOffset = 0
                    While blockOffset < read
                        Dim count = Math.Min(BlockSize, read - blockOffset)
                        subHash.AddBlockHash(buffer, blockOffset, count)
                        blockOffset += count
                    End While

                    Dim subBytes = subHash.Bytes
                    output.Write(subBytes, 0, subBytes.Length)
                    master.AddBlockHash(subBytes, 0, subBytes.Length)
                    output.Write(buffer, 0, read)
                    relativePos += read

                    Dim processed = Math.Min(dataSize, relativePos)
                    Dim basePct = 5 + CInt((processed * 82L) \ Math.Max(1L, dataSize))
                    Report(progress, Math.Min(87, basePct), $"Converting part {partIndex + 1}/{totalParts}", CInt(partIndex + 1), totalParts, processed, dataSize)

                    If read < SubpartSize Then Exit For
                Next

                output.Position = 0
                Dim masterBytes = master.Bytes
                output.Write(masterBytes, 0, masterBytes.Length)
                output.Flush(True)
                Return output.Length
            End Using
        End Function

        Private Shared Function BuildMhtChain(dataFolder As String, partCount As Long) As Byte()
            Dim current = ReadMht(Path.Combine(dataFolder, $"Data{partCount - 1:0000}"))
            For index = partCount - 2 To 0 Step -1
                Dim path = Path.Combine(dataFolder, $"Data{index:0000}")
                Dim previous = ReadMht(path)
                previous.AddHash(current.Digest())
                WriteMht(path, previous)
                current = previous
            Next
            Return current.Digest()
        End Function

        Private Shared Function ReadMht(path As String) As GodHashList
            Dim bytes(HashListSize - 1) As Byte
            Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim total = 0
                While total < bytes.Length
                    Dim n = stream.Read(bytes, total, bytes.Length - total)
                    If n <= 0 Then Exit While
                    total += n
                End While
                If total <> HashListSize Then Throw New InvalidDataException($"Invalid GOD part MHT: {path}")
            End Using
            Return GodHashList.FromBytes(bytes)
        End Function

        Private Shared Sub WriteMht(path As String, mht As GodHashList)
            Using stream As New FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)
                Dim bytes = mht.Bytes
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush(True)
            End Using
        End Sub

        Private Shared Sub WriteConHeader(path As String,
                                          info As XboxTitleInfo,
                                          blockCount As Long,
                                          partCount As Long,
                                          partSizes As List(Of Long),
                                          rootMhtHash As Byte())
            Dim header = LoadLiveTemplate()
            If header.Length <> HeaderSize Then Throw New InvalidDataException($"LIVE header template must be {HeaderSize} bytes.")

            BinaryHelpers.WriteUInt32BE(header, &H344, CUInt(info.ContentType))
            BinaryHelpers.WriteUInt32BE(header, &H354, info.MediaId)
            BinaryHelpers.WriteUInt32BE(header, &H360, info.TitleId)
            header(&H364) = info.Platform
            header(&H365) = info.ExecutableType
            header(&H366) = info.DiscNumber
            header(&H367) = info.DiscCount
            Array.Copy(rootMhtHash, 0, header, &H37D, 20)
            BinaryHelpers.WriteUInt24BE(header, &H392, CUInt(blockCount))
            BinaryHelpers.WriteUInt16BE(header, &H395, 0US)
            BinaryHelpers.WriteUInt32LE(header, &H3A0, CUInt(partCount))

            Dim lastPartSize = partSizes(partSizes.Count - 1)
            Dim totalPartBytes = lastPartSize + Math.Max(0L, partCount - 1L) * FullPartBlocks * BlockSize
            BinaryHelpers.WriteUInt32BE(header, &H3A4, CUInt(totalPartBytes \ &H100L))

            If Not String.IsNullOrWhiteSpace(info.DisplayName) Then
                BinaryHelpers.WriteUtf16BeNullTerminated(header, &H411, &H1691 - &H411, info.DisplayName)
                BinaryHelpers.WriteUtf16BeNullTerminated(header, &H1691, &H1712 - &H1691, info.DisplayName)
            End If

            header(&H35B) = 0
            header(&H35F) = 0
            header(&H391) = 0
            Using sha = SHA1.Create()
                Dim digest = sha.ComputeHash(header, &H344, &HACBC)
                Array.Copy(digest, 0, header, &H32C, digest.Length)
            End Using

            Directory.CreateDirectory(Path.GetDirectoryName(path))
            File.WriteAllBytes(path, header)
        End Sub

        Private Shared Function LoadLiveTemplate() As Byte()
            Dim asm = Assembly.GetExecutingAssembly()
            Using resource = asm.GetManifestResourceStream("XenonForge.empty_live.bin")
                If resource Is Nothing Then
                    Throw New InvalidOperationException("The embedded LIVE header template is missing. Run build.bat / prepare-assets.ps1 before building XenonForge.")
                End If
                Using ms As New MemoryStream()
                    resource.CopyTo(ms)
                    Return ms.ToArray()
                End Using
            End Using
        End Function

        Private Shared Function ReadUpTo(stream As Stream, buffer As Byte(), count As Integer) As Integer
            Dim total = 0
            While total < count
                Dim n = stream.Read(buffer, total, count - total)
                If n <= 0 Then Exit While
                total += n
            End While
            Return total
        End Function

        Private Shared Function CeilingDiv(value As Long, divisor As Long) As Long
            Return (value + divisor - 1L) \ divisor
        End Function

        Private Shared Sub Report(progress As IProgress(Of ConversionProgress),
                                  percent As Integer,
                                  stage As String,
                                  currentPart As Integer,
                                  totalParts As Integer,
                                  bytesProcessed As Long,
                                  totalBytes As Long)
            If progress Is Nothing Then Return
            progress.Report(New ConversionProgress With {
                .Percent = Math.Max(0, Math.Min(100, percent)),
                .Stage = stage,
                .CurrentPart = currentPart,
                .TotalParts = totalParts,
                .BytesProcessed = bytesProcessed,
                .TotalBytes = totalBytes
            })
        End Sub

        Private NotInheritable Class GodHashList
            Private ReadOnly _buffer(HashListSize - 1) As Byte
            Private _count As Integer

            Public ReadOnly Property Bytes As Byte()
                Get
                    Return DirectCast(_buffer.Clone(), Byte())
                End Get
            End Property

            Public Shared Function FromBytes(bytes As Byte()) As GodHashList
                Dim result As New GodHashList()
                Array.Copy(bytes, result._buffer, Math.Min(bytes.Length, result._buffer.Length))
                result._count = 0
                For i = 0 To 203
                    Dim allZero = True
                    For j = 0 To 19
                        If result._buffer(i * 20 + j) <> 0 Then
                            allZero = False
                            Exit For
                        End If
                    Next
                    If allZero Then
                        result._count = i
                        Return result
                    End If
                Next
                result._count = 204
                Return result
            End Function

            Public Sub AddHash(hash As Byte())
                If hash Is Nothing OrElse hash.Length <> 20 Then Throw New ArgumentException("SHA-1 hash must be 20 bytes.")
                If _count >= 204 Then Throw New InvalidDataException("GOD hash list overflow.")
                Array.Copy(hash, 0, _buffer, _count * 20, 20)
                _count += 1
            End Sub

            Public Sub AddBlockHash(data As Byte(), offset As Integer, count As Integer)
                Using sha = SHA1.Create()
                    AddHash(sha.ComputeHash(data, offset, count))
                End Using
            End Sub

            Public Function Digest() As Byte()
                Using sha = SHA1.Create()
                    Return sha.ComputeHash(_buffer)
                End Using
            End Function
        End Class
    End Class
End Namespace
