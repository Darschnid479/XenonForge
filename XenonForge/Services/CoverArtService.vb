Imports System.IO
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading

Namespace Services
    Public NotInheritable Class CoverArtService
        Private Shared ReadOnly Client As New HttpClient(New HttpClientHandler With {
            .AllowAutoRedirect = True,
            .AutomaticDecompression = Net.DecompressionMethods.All
        }) With {
            .Timeout = TimeSpan.FromSeconds(8)
        }

        Private ReadOnly _cacheRoot As String =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XenonForge", "covers")

        Public ReadOnly Property CacheRoot As String
            Get
                Return _cacheRoot
            End Get
        End Property

        Public Async Function GetCoverBytesAsync(titleIdHex As String,
                                                 cancellationToken As CancellationToken) As Task(Of Byte())
            If String.IsNullOrWhiteSpace(titleIdHex) OrElse titleIdHex.Length <> 8 Then Return Nothing
            titleIdHex = titleIdHex.ToUpperInvariant()

            Directory.CreateDirectory(_cacheRoot)
            Dim cacheFile = Path.Combine(_cacheRoot, titleIdHex & ".cover")
            If File.Exists(cacheFile) Then
                Try
                    Dim cached = Await File.ReadAllBytesAsync(cacheFile, cancellationToken)
                    If cached.Length > 128 Then Return cached
                Catch
                    ' If the cache entry is damaged/unreadable, fetch a fresh copy.
                End Try
            End If

            Try
                Dim metadataUrl = $"http://xboxunity.net/api/Covers/{titleIdHex}"
                Using response = Await Client.GetAsync(metadataUrl, cancellationToken)
                    If Not response.IsSuccessStatusCode Then Return Nothing
                    Dim json = Await response.Content.ReadAsStringAsync(cancellationToken)
                    Dim imageUrl = SelectBestImageUrl(json)
                    If String.IsNullOrWhiteSpace(imageUrl) Then Return Nothing

                    Dim uri As Uri = Nothing
                    If Not Uri.TryCreate(imageUrl, UriKind.Absolute, uri) Then Return Nothing
                    If uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps Then Return Nothing
                    If Not uri.Host.EndsWith("xboxunity.net", StringComparison.OrdinalIgnoreCase) Then Return Nothing

                    Using imageResponse = Await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        If Not imageResponse.IsSuccessStatusCode Then Return Nothing
                        Dim length = imageResponse.Content.Headers.ContentLength
                        If length.HasValue AndAlso length.Value > 12L * 1024L * 1024L Then Return Nothing
                        Dim bytes = Await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken)
                        If bytes.Length < 128 OrElse bytes.Length > 12 * 1024 * 1024 Then Return Nothing

                        Try
                            Await File.WriteAllBytesAsync(cacheFile, bytes, cancellationToken)
                        Catch
                            ' Cover display should still work when the cache cannot be written.
                        End Try
                        Return bytes
                    End Using
                End Using
            Catch ex As OperationCanceledException
                Throw
            Catch
                Return Nothing
            End Try
        End Function

        Public Sub ClearCache()
            Try
                If Directory.Exists(_cacheRoot) Then Directory.Delete(_cacheRoot, True)
            Catch
            End Try
        End Sub

        Private Shared Function SelectBestImageUrl(json As String) As String
            Try
                Using document = JsonDocument.Parse(json)
                    If document.RootElement.ValueKind <> JsonValueKind.Array Then Return Nothing

                    Dim fallback As String = Nothing
                    For Each item In document.RootElement.EnumerateArray()
                        Dim candidate = ReadUrl(item, "front")
                        If String.IsNullOrWhiteSpace(candidate) Then candidate = ReadUrl(item, "thumbnail")
                        If String.IsNullOrWhiteSpace(candidate) Then candidate = ReadUrl(item, "url")
                        If String.IsNullOrWhiteSpace(candidate) Then Continue For

                        If String.IsNullOrWhiteSpace(fallback) Then fallback = candidate

                        Dim official As Boolean = False
                        Dim officialProp As JsonElement
                        If item.TryGetProperty("official", officialProp) Then
                            If officialProp.ValueKind = JsonValueKind.True Then official = True
                            If officialProp.ValueKind = JsonValueKind.Number Then
                                Dim n As Integer
                                If officialProp.TryGetInt32(n) Then official = n <> 0
                            End If
                        End If
                        If official Then Return candidate
                    Next
                    Return fallback
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        Private Shared Function ReadUrl(item As JsonElement, propertyName As String) As String
            Dim prop As JsonElement
            If item.TryGetProperty(propertyName, prop) AndAlso prop.ValueKind = JsonValueKind.String Then
                Return prop.GetString()
            End If
            Return Nothing
        End Function
    End Class
End Namespace
