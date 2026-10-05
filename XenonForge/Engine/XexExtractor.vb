Imports System.IO
Imports System.Linq
Imports System.Threading

Namespace Engine
    Public NotInheritable Class XexExtractionProgress
        Public Property Percent As Integer
        Public Property CurrentPath As String = String.Empty
        Public Property BytesProcessed As Long
        Public Property TotalBytes As Long
    End Class

    Public NotInheritable Class XexExtractionResult
        Public Property Info As XboxTitleInfo = New XboxTitleInfo()
        Public Property OutputFolder As String = String.Empty
        Public Property ExecutablePath As String = String.Empty
        Public Property TotalBytes As Long
        Public Property FileCount As Integer
    End Class

    Public NotInheritable Class XexExtractor
        Public Async Function ExtractAsync(info As XboxTitleInfo,
                                           outputRoot As String,
                                           progress As IProgress(Of XexExtractionProgress),
                                           cancellationToken As CancellationToken) As Task(Of XexExtractionResult)
            If info Is Nothing Then Throw New ArgumentNullException(NameOf(info))
            If Not File.Exists(info.IsoPath) Then Throw New FileNotFoundException("ISO file was not found.", info.IsoPath)

            Return Await Task.Run(
                Function() ExtractInternal(info, outputRoot, progress, cancellationToken),
                cancellationToken)
        End Function

        Private Function ExtractInternal(info As XboxTitleInfo,
                                         outputRoot As String,
                                         progress As IProgress(Of XexExtractionProgress),
                                         cancellationToken As CancellationToken) As XexExtractionResult
            Dim safeName = MakeSafeName(info.DisplayName)
            Dim folderName = $"{info.TitleIdHex} - {safeName}"
            Dim xexRoot = Path.Combine(outputRoot, "XEX")
            Dim destinationRoot = Path.Combine(xexRoot, folderName)

            If Directory.Exists(destinationRoot) Then
                destinationRoot = GetUniqueDirectory(destinationRoot)
            End If
            Directory.CreateDirectory(destinationRoot)

            Using iso As New FileStream(info.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.RandomAccess)
                Dim fs As New XdvdfsReader(iso)
                Dim entries = fs.ReadRootEntries(True)

                Dim flatFiles = Flatten(entries, String.Empty).Where(Function(x) Not x.Entry.IsDirectory).ToList()
                Dim totalBytes = flatFiles.Sum(Function(x) CLng(x.Entry.Size))
                Dim copied As Long = 0
                Dim fileCount = 0

                For Each item In flatFiles
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim relative = SanitizeRelativePath(item.RelativePath)
                    Dim targetPath = GetSafeTargetPath(destinationRoot, relative)
                    Dim parent = Path.GetDirectoryName(targetPath)
                    If Not String.IsNullOrWhiteSpace(parent) Then Directory.CreateDirectory(parent)

                    fs.CopyEntryToFile(item.Entry, targetPath, cancellationToken)
                    copied += item.Entry.Size
                    fileCount += 1

                    progress?.Report(New XexExtractionProgress With {
                        .Percent = CInt(Math.Min(100L, copied * 100L  Math.Max(1L, totalBytes))),
                        .CurrentPath = relative,
                        .BytesProcessed = copied,
                        .TotalBytes = totalBytes
                    })
                Next

                Dim executable = Path.Combine(destinationRoot, If(info.ContentType = XboxContentType.XboxOriginal, "default.xbe", "default.xex"))
                If Not File.Exists(executable) Then
                    Throw New InvalidDataException($"Extraction completed, but {Path.GetFileName(executable)} was not found in the extracted root.")
                End If

                Return New XexExtractionResult With {
                    .Info = info,
                    .OutputFolder = destinationRoot,
                    .ExecutablePath = executable,
                    .TotalBytes = copied,
                    .FileCount = fileCount
                }
            End Using
        End Function

        Private Shared Function Flatten(entries As IEnumerable(Of XdvdfsEntry), prefix As String) As List(Of (Entry As XdvdfsEntry, RelativePath As String))
            Dim result As New List(Of (Entry As XdvdfsEntry, RelativePath As String))()
            For Each entry In entries
                Dim relative = If(String.IsNullOrEmpty(prefix), entry.Name, Path.Combine(prefix, entry.Name))
                result.Add((entry, relative))
                If entry.IsDirectory AndAlso entry.Children IsNot Nothing Then
                    result.AddRange(Flatten(entry.Children, relative))
                End If
            Next
            Return result
        End Function

        Private Shared Function MakeSafeName(value As String) As String
            Dim name = If(String.IsNullOrWhiteSpace(value), "Xbox Game", value.Trim())
            For Each ch In Path.GetInvalidFileNameChars()
                name = name.Replace(ch, "_"c)
            Next
            If name.Length > 80 Then name = name.Substring(0, 80).Trim()
            Return name
        End Function

        Private Shared Function SanitizeRelativePath(relative As String) As String
            Dim parts = relative.Split({Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}, StringSplitOptions.RemoveEmptyEntries)
            Dim clean As New List(Of String)()
            For Each raw In parts
                Dim part = raw
                If part = "." OrElse part = ".." Then Continue For
                For Each ch In Path.GetInvalidFileNameChars()
                    part = part.Replace(ch, "_"c)
                Next
                If String.IsNullOrWhiteSpace(part) Then part = "_"
                clean.Add(part)
            Next
            If clean.Count = 0 Then Throw New InvalidDataException("Encountered an invalid empty XDVDFS path.")
            Return Path.Combine(clean.ToArray())
        End Function

        Private Shared Function GetSafeTargetPath(root As String, relative As String) As String
            Dim rootFull = Path.GetFullPath(root)
            If Not rootFull.EndsWith(Path.DirectorySeparatorChar) Then rootFull &= Path.DirectorySeparatorChar
            Dim target = Path.GetFullPath(Path.Combine(rootFull, relative))
            If Not target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("Unsafe path detected while extracting XDVDFS.")
            End If
            Return target
        End Function

        Private Shared Function GetUniqueDirectory(path As String) As String
            For i = 2 To 9999
                Dim candidate = $"{path} ({i})"
                If Not Directory.Exists(candidate) Then Return candidate
            Next
            Throw New IOException("Could not create a unique XEX output folder.")
        End Function
    End Class
End Namespace
