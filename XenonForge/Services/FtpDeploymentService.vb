Imports System.Collections.Generic
Imports System.Linq
Imports System.IO
Imports System.Net
Imports System.Threading
Imports System.Threading.Tasks
Imports XenonForge.Engine

Namespace Services
    Public NotInheritable Class FtpDeploymentOptions
        Public Property Host As String = String.Empty
        Public Property Port As Integer = 21
        Public Property Username As String = "xbox"
        Public Property Password As String = String.Empty
        Public Property RemoteContentPath As String = "/Hdd1/Content/0000000000000000"
        Public Property UsePassive As Boolean = True
    End Class

    Public NotInheritable Class FtpDeploymentProgress
        Public Property Percent As Integer
        Public Property CurrentFile As String = String.Empty
        Public Property UploadedBytes As Long
        Public Property TotalBytes As Long
    End Class

    Public NotInheritable Class FtpDeploymentService
        Private Const BufferSize As Integer = 1024 * 1024
        Private Const TimeoutMs As Integer = 30000

        Public Async Function TestConnectionAsync(options As FtpDeploymentOptions,
                                                  cancellationToken As CancellationToken) As Task
            ValidateOptions(options)
            Dim request = CreateRequest(options, "/", WebRequestMethods.Ftp.ListDirectory)
            Using cancellationToken.Register(Sub() request.Abort())
                Using response = DirectCast(Await request.GetResponseAsync(), FtpWebResponse)
                    If response.StatusCode <> FtpStatusCode.OpeningData AndAlso
                       response.StatusCode <> FtpStatusCode.DataAlreadyOpen AndAlso
                       response.StatusCode <> FtpStatusCode.ClosingData Then
                        ' A successful FTP response is enough; different Xbox FTP servers use different codes here.
                    End If
                End Using
            End Using
        End Function

        Public Async Function DeployAsync(result As GodConversionResult,
                                          options As FtpDeploymentOptions,
                                          progress As IProgress(Of FtpDeploymentProgress),
                                          cancellationToken As CancellationToken) As Task(Of String)
            If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
            ValidateOptions(options)
            If Not Directory.Exists(result.OutputTitleFolder) Then
                Throw New DirectoryNotFoundException(result.OutputTitleFolder)
            End If

            Dim remoteContent = NormalizeRemotePath(options.RemoteContentPath)
            Dim destination = CombineRemote(remoteContent, result.Info.TitleIdHex)
            Dim files = Directory.EnumerateFiles(result.OutputTitleFolder, "*", SearchOption.AllDirectories).ToList()
            Dim total = files.Sum(Function(path) New FileInfo(path).Length)
            Dim uploaded As Long = 0

            Await EnsureDirectoryAsync(options, destination, cancellationToken)

            Dim created As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {destination}
            For Each file In files
                cancellationToken.ThrowIfCancellationRequested()

                Dim relative = Path.GetRelativePath(result.OutputTitleFolder, file).Replace("\", "/")
                Dim remoteFile = CombineRemote(destination, relative)
                Dim remoteParent = GetRemoteParent(remoteFile)

                If Not created.Contains(remoteParent) Then
                    Await EnsureDirectoryAsync(options, remoteParent, cancellationToken)
                    created.Add(remoteParent)
                End If

                uploaded += Await UploadFileAsync(
                    file,
                    remoteFile,
                    options,
                    total,
                    uploaded,
                    progress,
                    cancellationToken)
            Next

            Return destination
        End Function

        Private Shared Async Function UploadFileAsync(localPath As String,
                                                      remotePath As String,
                                                      options As FtpDeploymentOptions,
                                                      totalBytes As Long,
                                                      uploadedBefore As Long,
                                                      progress As IProgress(Of FtpDeploymentProgress),
                                                      cancellationToken As CancellationToken) As Task(Of Long)
            Dim request = CreateRequest(options, remotePath, WebRequestMethods.Ftp.UploadFile)
            request.ContentLength = New FileInfo(localPath).Length
            Dim uploadedThisFile As Long = 0

            Using cancellationToken.Register(Sub() request.Abort())
                Using input As New FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous Or FileOptions.SequentialScan)
                    Using output = Await request.GetRequestStreamAsync()
                        Dim buffer(BufferSize - 1) As Byte
                        While True
                            cancellationToken.ThrowIfCancellationRequested()
                            Dim read = Await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            If read <= 0 Then Exit While

                            Await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            uploadedThisFile += read
                            Dim currentTotal = uploadedBefore + uploadedThisFile

                            If progress IsNot Nothing Then
                                progress.Report(New FtpDeploymentProgress With {
                                    .Percent = CInt(Math.Min(100L, currentTotal * 100L \ Math.Max(1L, totalBytes))),
                                    .CurrentFile = Path.GetFileName(localPath),
                                    .UploadedBytes = currentTotal,
                                    .TotalBytes = totalBytes
                                })
                            End If
                        End While
                        Await output.FlushAsync(cancellationToken)
                    End Using
                End Using

                Using response = DirectCast(Await request.GetResponseAsync(), FtpWebResponse)
                End Using
            End Using

            Return uploadedThisFile
        End Function

        Private Shared Async Function EnsureDirectoryAsync(options As FtpDeploymentOptions,
                                                           remotePath As String,
                                                           cancellationToken As CancellationToken) As Task
            Dim normalized = NormalizeRemotePath(remotePath)
            Dim segments = normalized.Trim("/"c).Split("/"c, StringSplitOptions.RemoveEmptyEntries)
            Dim current = String.Empty

            For Each segment In segments
                current = CombineRemote(current, segment)
                Dim request = CreateRequest(options, current, WebRequestMethods.Ftp.MakeDirectory)

                Try
                    Using cancellationToken.Register(Sub() request.Abort())
                        Using response = DirectCast(Await request.GetResponseAsync(), FtpWebResponse)
                        End Using
                    End Using
                Catch ex As WebException
                    Dim ftpResponse = TryCast(ex.Response, FtpWebResponse)
                    If ftpResponse Is Nothing OrElse ftpResponse.StatusCode <> FtpStatusCode.ActionNotTakenFileUnavailable Then
                        Throw
                    End If
                    ftpResponse.Dispose()
                End Try
            Next
        End Function

        Private Shared Function CreateRequest(options As FtpDeploymentOptions,
                                              remotePath As String,
                                              method As String) As FtpWebRequest
#Disable Warning SYSLIB0014
            Dim request = DirectCast(WebRequest.Create(BuildUri(options, remotePath)), FtpWebRequest)
#Enable Warning SYSLIB0014
            request.Method = method
            request.Credentials = New NetworkCredential(options.Username, options.Password)
            request.UseBinary = True
            request.UsePassive = options.UsePassive
            request.KeepAlive = False
            request.EnableSsl = False
            request.Timeout = TimeoutMs
            request.ReadWriteTimeout = TimeoutMs
            request.Proxy = Nothing
            Return request
        End Function

        Private Shared Function BuildUri(options As FtpDeploymentOptions, remotePath As String) As Uri
            Dim host = options.Host.Trim()
            If host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) Then
                host = New Uri(host).Host
            End If

            Dim escapedPath = String.Join("/",
                NormalizeRemotePath(remotePath).
                    Trim("/"c).
                    Split("/"c, StringSplitOptions.RemoveEmptyEntries).
                    Select(Function(part) Uri.EscapeDataString(part)))

            Dim builder As New UriBuilder("ftp", host, options.Port, "/" & escapedPath)
            Return builder.Uri
        End Function

        Private Shared Function NormalizeRemotePath(value As String) As String
            Dim path = If(value, String.Empty).Trim().Replace("\", "/").Replace(":", String.Empty)
            While path.Contains("//", StringComparison.Ordinal)
                path = path.Replace("//", "/")
            End While
            If Not path.StartsWith("/", StringComparison.Ordinal) Then path = "/" & path
            Return path.TrimEnd("/"c)
        End Function

        Private Shared Function CombineRemote(left As String, right As String) As String
            Dim a = NormalizeRemotePath(If(left, String.Empty))
            Dim b = If(right, String.Empty).Replace("\", "/").Trim("/"c)
            If String.IsNullOrEmpty(b) Then Return a
            If a = "/" Then Return "/" & b
            Return a & "/" & b
        End Function

        Private Shared Function GetRemoteParent(path As String) As String
            Dim normalized = NormalizeRemotePath(path)
            Dim index = normalized.LastIndexOf("/"c)
            If index <= 0 Then Return "/"
            Return normalized.Substring(0, index)
        End Function

        Private Shared Sub ValidateOptions(options As FtpDeploymentOptions)
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            If String.IsNullOrWhiteSpace(options.Host) Then Throw New ArgumentException("Enter the Xbox 360 IP address or host name.")
            If options.Port < 1 OrElse options.Port > 65535 Then Throw New ArgumentOutOfRangeException(NameOf(options.Port))
            If String.IsNullOrWhiteSpace(options.Username) Then Throw New ArgumentException("Enter the FTP username.")
            If String.IsNullOrWhiteSpace(options.RemoteContentPath) Then Throw New ArgumentException("Enter the Xbox Content folder path.")
        End Sub
    End Class
End Namespace
