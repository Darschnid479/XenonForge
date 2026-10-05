Imports System.Linq
Imports System.Collections.Generic
Imports System.IO
Imports XenonForge.Engine

Namespace Services
    Public NotInheritable Class UsbTarget
        Public Property RootPath As String = String.Empty
        Public Property Label As String = String.Empty
        Public Property DriveType As DriveType
        Public Property FreeBytes As Long
        Public Property TotalBytes As Long

        Public Overrides Function ToString() As String
            Dim name = If(String.IsNullOrWhiteSpace(Label), "USB / Drive", Label)
            Return $"{name}  •  {RootPath}  •  {FormatBytes(FreeBytes)} free"
        End Function

        Private Shared Function FormatBytes(value As Long) As String
            Dim gb = value / 1024.0 / 1024.0 / 1024.0
            Return $"{gb:0.0} GB"
        End Function
    End Class

    Public NotInheritable Class UsbDeploymentProgress
        Public Property Percent As Integer
        Public Property CurrentFile As String = String.Empty
        Public Property CopiedBytes As Long
        Public Property TotalBytes As Long
    End Class

    Public NotInheritable Class UsbDeploymentService
        Public Function GetTargets() As List(Of UsbTarget)
            Dim targets As New List(Of UsbTarget)()
            For Each drive In DriveInfo.GetDrives()
                Try
                    If Not drive.IsReady Then Continue For
                    If drive.DriveType <> DriveType.Removable AndAlso drive.DriveType <> DriveType.Fixed Then Continue For
                    If drive.RootDirectory.FullName.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase) Then Continue For
                    targets.Add(New UsbTarget With {
                        .RootPath = drive.RootDirectory.FullName,
                        .Label = drive.VolumeLabel,
                        .DriveType = drive.DriveType,
                        .FreeBytes = drive.AvailableFreeSpace,
                        .TotalBytes = drive.TotalSize
                    })
                Catch
                    ' Ignore drives that become unavailable while enumerating.
                End Try
            Next
            Return targets.OrderBy(Function(t) If(t.DriveType = DriveType.Removable, 0, 1)).ThenBy(Function(t) t.RootPath).ToList()
        End Function

        Public Async Function DeployAsync(result As GodConversionResult,
                                          target As UsbTarget,
                                          progress As IProgress(Of UsbDeploymentProgress),
                                          cancellationToken As Threading.CancellationToken) As Task(Of String)
            If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
            If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
            Return Await Task.Run(Function() DeployInternal(result, target, progress, cancellationToken), cancellationToken)
        End Function

        Private Function DeployInternal(result As GodConversionResult,
                                        target As UsbTarget,
                                        progress As IProgress(Of UsbDeploymentProgress),
                                        cancellationToken As Threading.CancellationToken) As String
            Dim sourceTitleFolder = result.OutputTitleFolder
            If Not Directory.Exists(sourceTitleFolder) Then Throw New DirectoryNotFoundException(sourceTitleFolder)

            Dim contentRoot = Path.Combine(target.RootPath, "Content", "0000000000000000")
            Dim destination = Path.Combine(contentRoot, result.Info.TitleIdHex)
            Dim total = GetDirectorySize(sourceTitleFolder)

            Dim refreshed = New DriveInfo(Path.GetPathRoot(target.RootPath))
            If refreshed.AvailableFreeSpace < total Then
                Throw New IOException($"Not enough free space. Need {FormatBytes(total)}, available {FormatBytes(refreshed.AvailableFreeSpace)}.")
            End If

            Directory.CreateDirectory(contentRoot)
            Dim copied As Long = 0
            CopyDirectory(sourceTitleFolder, destination, total, copied, progress, cancellationToken)
            Return destination
        End Function

        Private Shared Sub CopyDirectory(source As String,
                                         destination As String,
                                         total As Long,
                                         ByRef copied As Long,
                                         progress As IProgress(Of UsbDeploymentProgress),
                                         cancellationToken As Threading.CancellationToken)
            Directory.CreateDirectory(destination)
            For Each file In Directory.GetFiles(source)
                cancellationToken.ThrowIfCancellationRequested()
                Dim destFile = Path.Combine(destination, Path.GetFileName(file))
                CopyFile(file, destFile, total, copied, progress, cancellationToken)
            Next
            For Each dir In Directory.GetDirectories(source)
                cancellationToken.ThrowIfCancellationRequested()
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), total, copied, progress, cancellationToken)
            Next
        End Sub

        Private Shared Sub CopyFile(source As String,
                                    destination As String,
                                    total As Long,
                                    ByRef copied As Long,
                                    progress As IProgress(Of UsbDeploymentProgress),
                                    cancellationToken As Threading.CancellationToken)
            Const BufferSize As Integer = 4 * 1024 * 1024
            Dim buffer(BufferSize - 1) As Byte
            Using input As New FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan)
                Using output As New FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan)
                    While True
                        cancellationToken.ThrowIfCancellationRequested()
                        Dim n = input.Read(buffer, 0, buffer.Length)
                        If n <= 0 Then Exit While
                        output.Write(buffer, 0, n)
                        copied += n
                        If progress IsNot Nothing Then
                            progress.Report(New UsbDeploymentProgress With {
                                .Percent = CInt(Math.Min(100L, copied * 100L \ Math.Max(1L, total))),
                                .CurrentFile = Path.GetFileName(source),
                                .CopiedBytes = copied,
                                .TotalBytes = total
                            })
                        End If
                    End While
                    output.Flush(True)
                End Using
            End Using
        End Sub

        Private Shared Function GetDirectorySize(path As String) As Long
            Dim total As Long = 0
            For Each file In Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                Try
                    total += New FileInfo(file).Length
                Catch
                End Try
            Next
            Return total
        End Function

        Private Shared Function FormatBytes(value As Long) As String
            Return $"{value / 1024.0 / 1024.0 / 1024.0:0.00} GB"
        End Function
    End Class
End Namespace
