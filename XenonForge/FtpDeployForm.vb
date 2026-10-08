Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports XenonForge.Engine
Imports XenonForge.Services

Public NotInheritable Class FtpDeployForm
    Inherits Form

    Private ReadOnly _results As List(Of GodConversionResult)
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _service As FtpDeploymentService

    Private ReadOnly gameBox As New ComboBox()
    Private ReadOnly hostBox As New TextBox()
    Private ReadOnly portBox As New NumericUpDown()
    Private ReadOnly userBox As New TextBox()
    Private ReadOnly passwordBox As New TextBox()
    Private ReadOnly remotePathBox As New TextBox()
    Private ReadOnly passiveCheck As New CheckBox()
    Private ReadOnly progress As New ModernProgressBar()
    Private ReadOnly status As New Label()
    Private _cts As CancellationTokenSource

    Public Sub New(results As IEnumerable(Of GodConversionResult),
                   settings As AppSettings,
                   service As FtpDeploymentService)
        _results = results.Where(Function(x) x IsNot Nothing).ToList()
        _settings = settings
        _service = service

        Text = "XenonForge — Xbox 360 FTP Deploy"
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(820, 650)
        MinimumSize = New Size(820, 650)
        BackColor = Theme.Background
        ForeColor = Theme.TextPrimary
        Font = Theme.Font(10.0F)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        BuildUi()
        LoadDefaults()
    End Sub

    Private Sub BuildUi()
        Controls.Add(New Label With {
            .Text = "FTP DEPLOY",
            .Font = Theme.Font(20.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(28, 24)
        })

        Controls.Add(New Label With {
            .Text = "Send a completed GOD package directly to an Xbox 360 FTP server.",
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(30, 62)
        })

        Dim card As New RoundedPanel With {
            .Location = New Point(28, 104),
            .Size = New Size(764, 452),
            .FillColor = Theme.Surface,
            .BorderColor = Theme.Border,
            .Radius = 16
        }
        Controls.Add(card)

        AddFieldLabel(card, "Completed game", 20, 18)
        gameBox.DropDownStyle = ComboBoxStyle.DropDownList
        gameBox.BackColor = Theme.Surface2
        gameBox.ForeColor = Theme.TextPrimary
        gameBox.FlatStyle = FlatStyle.Flat
        gameBox.Location = New Point(20, 46)
        gameBox.Size = New Size(724, 30)
        For Each result In _results
            gameBox.Items.Add(New ResultItem(result))
        Next
        If gameBox.Items.Count > 0 Then gameBox.SelectedIndex = 0
        card.Controls.Add(gameBox)

        AddFieldLabel(card, "Xbox IP / host", 20, 96)
        hostBox.Location = New Point(20, 124)
        hostBox.Size = New Size(330, 30)
        StyleTextBox(hostBox)
        card.Controls.Add(hostBox)

        AddFieldLabel(card, "Port", 370, 96)
        portBox.Location = New Point(370, 124)
        portBox.Size = New Size(100, 30)
        portBox.Minimum = 1
        portBox.Maximum = 65535
        portBox.Value = 21
        portBox.BackColor = Theme.Surface2
        portBox.ForeColor = Theme.TextPrimary
        card.Controls.Add(portBox)

        AddFieldLabel(card, "Username", 20, 174)
        userBox.Location = New Point(20, 202)
        userBox.Size = New Size(220, 30)
        StyleTextBox(userBox)
        card.Controls.Add(userBox)

        AddFieldLabel(card, "Password", 260, 174)
        passwordBox.Location = New Point(260, 202)
        passwordBox.Size = New Size(210, 30)
        passwordBox.UseSystemPasswordChar = True
        StyleTextBox(passwordBox)
        card.Controls.Add(passwordBox)

        passiveCheck.Text = "Passive mode"
        passiveCheck.ForeColor = Theme.TextPrimary
        passiveCheck.BackColor = card.FillColor
        passiveCheck.AutoSize = True
        passiveCheck.Location = New Point(500, 204)
        card.Controls.Add(passiveCheck)

        AddFieldLabel(card, "Remote Xbox Content folder", 20, 252)
        remotePathBox.Location = New Point(20, 280)
        remotePathBox.Size = New Size(724, 30)
        StyleTextBox(remotePathBox)
        card.Controls.Add(remotePathBox)

        card.Controls.Add(New Label With {
            .Text = "Typical Aurora/FSD path: /Hdd1/Content/0000000000000000",
            .ForeColor = Theme.TextMuted,
            .Font = Theme.Font(8.5F),
            .AutoSize = True,
            .Location = New Point(20, 316),
            .BackColor = card.FillColor
        })

        Dim test As New ModernButton With {
            .Text = "TEST CONNECTION",
            .FillColor = Theme.Surface2,
            .HoverColor = Color.FromArgb(36, 48, 78),
            .ForeColor = Theme.TextPrimary,
            .Location = New Point(20, 350),
            .Size = New Size(176, 40)
        }
        AddHandler test.Click, Async Sub() Await TestConnectionAsync()
        card.Controls.Add(test)

        progress.Location = New Point(20, 408)
        progress.Size = New Size(724, 8)
        card.Controls.Add(progress)

        status.Text = If(_results.Count = 0, "No completed GOD conversions yet.", "Ready.")
        status.ForeColor = Theme.TextMuted
        status.AutoSize = True
        status.Location = New Point(20, 424)
        card.Controls.Add(status)

        Controls.Add(New Label With {
            .Text = "FTP sends credentials unencrypted. Use this only on a trusted local network.",
            .ForeColor = Theme.TextMuted,
            .Font = Theme.Font(8.5F),
            .AutoSize = True,
            .Location = New Point(30, 574)
        })

        Dim deploy As New ModernButton With {
            .Text = "DEPLOY TO XBOX",
            .FillColor = Theme.Accent,
            .HoverColor = Theme.AccentHover,
            .ForeColor = Theme.Background,
            .Location = New Point(604, 596),
            .Size = New Size(188, 42)
        }
        AddHandler deploy.Click, Async Sub() Await DeployAsync()
        Controls.Add(deploy)

        Dim cancel As New ModernButton With {
            .Text = "CANCEL",
            .FillColor = Theme.Surface2,
            .HoverColor = Color.FromArgb(36, 48, 78),
            .ForeColor = Theme.TextPrimary,
            .Location = New Point(480, 596),
            .Size = New Size(112, 42)
        }
        AddHandler cancel.Click,
            Sub()
                If _cts Is Nothing Then
                    Close()
                Else
                    _cts.Cancel()
                End If
            End Sub
        Controls.Add(cancel)
    End Sub

    Private Sub LoadDefaults()
        hostBox.Text = _settings.FtpHost
        portBox.Value = Math.Min(65535D, Math.Max(1D, CDec(_settings.FtpPort)))
        userBox.Text = If(String.IsNullOrWhiteSpace(_settings.FtpUsername), "xbox", _settings.FtpUsername)
        remotePathBox.Text = If(String.IsNullOrWhiteSpace(_settings.FtpRemoteContentPath),
                                "/Hdd1/Content/0000000000000000",
                                _settings.FtpRemoteContentPath)
        passiveCheck.Checked = _settings.FtpUsePassive
    End Sub

    Private Function BuildOptions() As FtpDeploymentOptions
        Return New FtpDeploymentOptions With {
            .Host = hostBox.Text.Trim(),
            .Port = CInt(portBox.Value),
            .Username = userBox.Text.Trim(),
            .Password = passwordBox.Text,
            .RemoteContentPath = remotePathBox.Text.Trim(),
            .UsePassive = passiveCheck.Checked
        }
    End Function

    Private Sub SaveDefaults()
        _settings.FtpHost = hostBox.Text.Trim()
        _settings.FtpPort = CInt(portBox.Value)
        _settings.FtpUsername = userBox.Text.Trim()
        _settings.FtpRemoteContentPath = remotePathBox.Text.Trim()
        _settings.FtpUsePassive = passiveCheck.Checked
        _settings.Save()
    End Sub

    Private Async Function TestConnectionAsync() As Task
        If _cts IsNot Nothing Then Return

        _cts = New CancellationTokenSource()
        Try
            SaveDefaults()
            status.Text = "Connecting to Xbox 360..."
            progress.Value = 10
            Await _service.TestConnectionAsync(BuildOptions(), _cts.Token)
            progress.Value = 100
            status.Text = "FTP connection successful."
            MessageBox.Show(Me, "Connected successfully to the Xbox 360 FTP server.", "XenonForge FTP", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As OperationCanceledException
            status.Text = "Connection test cancelled."
        Catch ex As Exception
            progress.Value = 0
            status.Text = "FTP connection failed."
            MessageBox.Show(Me, ex.Message, "XenonForge FTP", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            _cts.Dispose()
            _cts = Nothing
        End Try
    End Function

    Private Async Function DeployAsync() As Task
        If _cts IsNot Nothing Then Return

        Dim selected = TryCast(gameBox.SelectedItem, ResultItem)
        If selected Is Nothing Then
            MessageBox.Show(Me, "Convert a GOD game first, then open FTP Deploy.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        _cts = New CancellationTokenSource()
        Try
            SaveDefaults()
            progress.Value = 0
            status.Text = "Connecting..."

            Await _service.TestConnectionAsync(BuildOptions(), _cts.Token)

            Dim p As New Progress(Of FtpDeploymentProgress)(
                Sub(x)
                    progress.Value = x.Percent
                    status.Text = $"FTP {x.Percent}% — {x.CurrentFile}"
                End Sub)

            Dim destination = Await _service.DeployAsync(selected.Result, BuildOptions(), p, _cts.Token)
            progress.Value = 100
            status.Text = "FTP deployment complete."
            MessageBox.Show(
                Me,
                $"Uploaded successfully to:{Environment.NewLine}{destination}",
                "XenonForge FTP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch ex As OperationCanceledException
            status.Text = "FTP deployment cancelled."
        Catch ex As Exception
            status.Text = "FTP deployment failed."
            MessageBox.Show(Me, ex.Message, "XenonForge FTP Deploy", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            _cts.Dispose()
            _cts = Nothing
        End Try
    End Function

    Private Shared Sub AddFieldLabel(parent As Control, text As String, x As Integer, y As Integer)
        parent.Controls.Add(New Label With {
            .Text = text,
            .ForeColor = Theme.TextPrimary,
            .Font = Theme.Font(9.0F, FontStyle.Bold),
            .AutoSize = True,
            .Location = New Point(x, y),
            .BackColor = DirectCast(parent, RoundedPanel).FillColor
        })
    End Sub

    Private Shared Sub StyleTextBox(box As TextBox)
        box.BackColor = Theme.Surface2
        box.ForeColor = Theme.TextPrimary
        box.BorderStyle = BorderStyle.FixedSingle
    End Sub

    Private NotInheritable Class ResultItem
        Public ReadOnly Property Result As GodConversionResult

        Public Sub New(value As GodConversionResult)
            Result = value
        End Sub

        Public Overrides Function ToString() As String
            Return $"{Result.Info.DisplayName}  [{Result.Info.TitleIdHex}]"
        End Function
    End Class
End Class
