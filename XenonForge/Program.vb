Imports System
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

Friend Module Program
    <STAThread>
    Public Sub Main()
        Dim logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XenonForge",
            "startup-crash.log")

        Try
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            AddHandler Application.ThreadException,
                Sub(sender, e)
                    WriteCrashLog(logPath, "UI thread exception", e.Exception)
                    MessageBox.Show(
                        "XenonForge encountered an error." & Environment.NewLine & Environment.NewLine &
                        e.Exception.Message & Environment.NewLine & Environment.NewLine &
                        "Crash log:" & Environment.NewLine & logPath,
                        "XenonForge",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error)
                End Sub

            AddHandler AppDomain.CurrentDomain.UnhandledException,
                Sub(sender, e)
                    Dim ex = TryCast(e.ExceptionObject, Exception)
                    If ex IsNot Nothing Then WriteCrashLog(logPath, "Unhandled exception", ex)
                End Sub

            Application.Run(New MainForm())
        Catch ex As Exception
            WriteCrashLog(logPath, "Startup failure", ex)
            MessageBox.Show(
                "XenonForge could not start." & Environment.NewLine & Environment.NewLine &
                ex.ToString() & Environment.NewLine & Environment.NewLine &
                "Crash log:" & Environment.NewLine & logPath,
                "XenonForge startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub WriteCrashLog(path As String, category As String, ex As Exception)
        Try
            Dim folder = Path.GetDirectoryName(path)
            If Not String.IsNullOrWhiteSpace(folder) Then Directory.CreateDirectory(folder)

            Dim sb As New StringBuilder()
            sb.AppendLine("XenonForge startup diagnostics")
            sb.AppendLine($"UTC: {DateTime.UtcNow:O}")
            sb.AppendLine($"Category: {category}")
            sb.AppendLine($"OS: {Environment.OSVersion}")
            sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}")
            sb.AppendLine($"64-bit process: {Environment.Is64BitProcess}")
            sb.AppendLine($".NET: {Environment.Version}")
            sb.AppendLine()
            sb.AppendLine(ex.ToString())
            File.WriteAllText(path, sb.ToString())
        Catch
            ' Never let diagnostic logging cause a second startup failure.
        End Try
    End Sub
End Module
