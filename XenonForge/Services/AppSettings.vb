Imports System.IO
Imports System.Text.Json

Namespace Services
    Public NotInheritable Class AppSettings
        Public Property OutputFolder As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XenonForge")
        Public Property SmartTrim As Boolean = True
        Public Property AutoDeployAfterConvert As Boolean = False
        Public Property LastUsbRoot As String = String.Empty

        Private Shared ReadOnly SettingsFile As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XenonForge", "settings.json")

        Public Shared Function Load() As AppSettings
            Try
                If File.Exists(SettingsFile) Then
                    Dim value = JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(SettingsFile))
                    If value IsNot Nothing Then Return value
                End If
            Catch
            End Try
            Return New AppSettings()
        End Function

        Public Sub Save()
            Try
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile))
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
            Catch
            End Try
        End Sub
    End Class
End Namespace
