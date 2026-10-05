Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Threading

Namespace Services
    Public NotInheritable Class ZipImportResult
        Public Property ZipPath As String = String.Empty
        Public Property ExtractedIsoPaths As New List(Of String)()
        Public Property ZipDeleted As Boolean
    End Class

    Public NotInheritable Class ZipImportService
        Public Async Function ExtractIsosAndDeleteZipAsync(zipPath As String,
                                                          cancellationToken As CancellationToken) As Task(Of ZipImportResult)
            Return Await Task.Run(Function() ExtractInternal(zipPath, cancellationToken), cancellationToken)
        End Function

        Private Function ExtractInternal(zipPath As String,
                                         cancellationToken As CancellationToken) As ZipImportResult
            If String.IsNullOrWhiteSpace(zipPath) Then Throw New ArgumentException("ZIP path is empty.", NameOf(zipPath))
            If Not File.Exists(zipPath) Then Throw New FileNotFoundException("ZIP file was not found.", zipPath)
            If Not Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("The selected file is not a ZIP archive.")
            End If

            Dim fullZipPath = Path.GetFullPath(zipPath)
            Dim baseDirectory = Path.GetDirectoryName(fullZipPath)
            If String.IsNullOrWhiteSpace(baseDirectory) Then Throw New InvalidDataException("Could not determine ZIP folder.")

            Dim archiveName = Path.GetFileNameWithoutExtension(fullZipPath)
            Dim outputDirectory = Path.Combine(baseDirectory, archiveName & "_extracted")
            Directory.CreateDirectory(outputDirectory)

            Dim result As New ZipImportResult With {.ZipPath = fullZipPath}

            Using archive = ZipFile.OpenRead(fullZipPath)
                Dim isoEntries = archive.Entries.
                    Where(Function(e) Not String.IsNullOrWhiteSpace(e.Name) AndAlso
                                      Path.GetExtension(e.Name).Equals(".iso", StringComparison.OrdinalIgnoreCase) AndAlso
                                      e.Length > 0).
                    OrderByDescending(Function(e) e.Length).
                    ToList()

                If isoEntries.Count = 0 Then
                    Throw New InvalidDataException("No .iso file was found inside the ZIP archive.")
                End If

                For Each entry In isoEntries
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim safeName = Path.GetFileName(entry.Name)
                    If String.IsNullOrWhiteSpace(safeName) Then Continue For

                    Dim finalPath = GetUniquePath(outputDirectory, safeName)
                    Dim partialPath = finalPath & ".partial"

                    Try
                        Using input = entry.Open()
                            Using output As New FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan)
                                input.CopyTo(output)
                                output.Flush(True)
                            End Using
                        End Using

                        Dim extractedLength = New FileInfo(partialPath).Length
                        If extractedLength <> entry.Length OrElse extractedLength <= 0 Then
                            Throw New InvalidDataException($"Extracted ISO size mismatch for {entry.FullName}.")
                        End If

                        File.Move(partialPath, finalPath)
                        result.ExtractedIsoPaths.Add(finalPath)
                    Catch
                        Try
                            If File.Exists(partialPath) Then File.Delete(partialPath)
                        Catch
                        End Try
                        Throw
                    End Try
                Next
            End Using

            If result.ExtractedIsoPaths.Count = 0 Then
                Throw New InvalidDataException("No ISO files could be extracted from the ZIP archive.")
            End If

            For Each isoPath In result.ExtractedIsoPaths
                If Not File.Exists(isoPath) OrElse New FileInfo(isoPath).Length <= 0 Then
                    Throw New InvalidDataException("An extracted ISO could not be verified. The ZIP archive was kept.")
                End If
            Next

            cancellationToken.ThrowIfCancellationRequested()

            File.Delete(fullZipPath)
            result.ZipDeleted = Not File.Exists(fullZipPath)
            If Not result.ZipDeleted Then
                Throw New IOException("ISO extraction succeeded, but the ZIP archive could not be deleted.")
            End If

            Return result
        End Function

        Private Shared Function GetUniquePath(directory As String, fileName As String) As String
            Dim candidate = Path.Combine(directory, fileName)
            If Not File.Exists(candidate) AndAlso Not File.Exists(candidate & ".partial") Then Return candidate

            Dim stem = Path.GetFileNameWithoutExtension(fileName)
            Dim ext = Path.GetExtension(fileName)
            For i = 2 To 9999
                candidate = Path.Combine(directory, $"{stem} ({i}){ext}")
                If Not File.Exists(candidate) AndAlso Not File.Exists(candidate & ".partial") Then Return candidate
            Next

            Throw New IOException("Could not create a unique ISO filename.")
        End Function
    End Class
End Namespace
