Imports System.IO
Imports System.Collections.Generic
Imports System.Reflection
Imports System.Text.Json

Namespace Engine
    Friend Module GameCatalog
        Private ReadOnly SyncRoot As New Object()
        Private _loaded As Boolean
        Private ReadOnly Titles As New Dictionary(Of UInteger, String)()

        Public Function ResolveTitle(titleId As UInteger, fallback As String) As String
            EnsureLoaded()
            Dim title As String = Nothing
            If Titles.TryGetValue(titleId, title) AndAlso Not String.IsNullOrWhiteSpace(title) Then Return title.Trim()
            If Not String.IsNullOrWhiteSpace(fallback) Then Return CleanFallback(fallback)
            Return $"Unknown title ({titleId:X8})"
        End Function

        Private Sub EnsureLoaded()
            If _loaded Then Return
            SyncLock SyncRoot
                If _loaded Then Return
                Try
                    Dim asm = Assembly.GetExecutingAssembly()
                    Using stream = asm.GetManifestResourceStream("XenonForge.titles.jsonl")
                        If stream Is Nothing Then
                            _loaded = True
                            Return
                        End If
                        Using reader As New StreamReader(stream)
                            While Not reader.EndOfStream
                                Dim line = reader.ReadLine()
                                If String.IsNullOrWhiteSpace(line) Then Continue While
                                Try
                                    Using doc = JsonDocument.Parse(line)
                                        Dim root = doc.RootElement
                                        Dim idText = root.GetProperty("TitleID").GetString()
                                        Dim name = root.GetProperty("Name").GetString()
                                        Dim id As UInteger
                                        If UInteger.TryParse(idText, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, id) AndAlso Not String.IsNullOrWhiteSpace(name) Then
                                            If Not Titles.ContainsKey(id) Then Titles.Add(id, name.Trim())
                                        End If
                                    End Using
                                Catch
                                    ' Ignore malformed catalog lines.
                                End Try
                            End While
                        End Using
                    End Using
                Catch
                    ' Catalog is a convenience feature; ISO metadata still works without it.
                End Try
                _loaded = True
            End SyncLock
        End Sub

        Private Function CleanFallback(value As String) As String
            Dim result = value.Replace("_", " ").Replace(".", " ")
            Do While result.Contains("  ")
                result = result.Replace("  ", " ")
            Loop
            Return result.Trim()
        End Function
    End Module
End Namespace
