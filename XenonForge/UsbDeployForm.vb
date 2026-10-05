Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports XenonForge.Engine
Imports XenonForge.Services

Public NotInheritable Class UsbDeployForm
    Inherits Form

    Private ReadOnly _results As List(Of GodConversionResult)
    Private ReadOnly _service As UsbDeploymentService
    Private ReadOnly gameBox As New ComboBox()
    Private ReadOnly driveBox As New ComboBox()
    Private ReadOnly progress As New ModernProgressBar()
    Private ReadOnly status As New Label()
    Private _cts As CancellationTokenSource

    Public Sub New(results As IEnumerable(Of GodConversionResult), service As UsbDeploymentService)
        _results = results.Where(Function(x) x IsNot Nothing).ToList()
        _service = service
        Text = "XenonForge — USB Deploy"
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(760, 470)
        BackColor = Theme.Background
        ForeColor = Theme.TextPrimary
        Font = Theme.Font(10.0F)
        BuildUi()
        RefreshTargets()
    End Sub

    Private Sub BuildUi()
        Controls.Add(New Label With {
            .Text = "USB DEPLOY",
            .Font = Theme.Font(20.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(28, 24)
        })
        Controls.Add(New Label With {
            .Text = "Copy a completed GOD package into the Xbox 360 Content structure.",
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(30, 62)
        })

        Dim card As New RoundedPanel With {
            .Location = New Point(28, 104),
            .Size = New Size(704, 270),
            .FillColor = Theme.Surface,
            .BorderColor = Theme.Border,
            .Radius = 16
        }
        Controls.Add(card)

        card.Controls.Add(New Label With {.Text = "Completed game", .ForeColor = Theme.TextPrimary, .Font = Theme.Font(9.0F, FontStyle.Bold), .AutoSize = True, .Location = New Point(20, 20), .BackColor = card.FillColor})
        gameBox.DropDownStyle = ComboBoxStyle.DropDownList
        gameBox.BackColor = Theme.Surface2
        gameBox.ForeColor = Theme.TextPrimary
        gameBox.FlatStyle = FlatStyle.Flat
        gameBox.Location = New Point(20, 50)
        gameBox.Size = New Size(660, 30)
        For Each result In _results
            gameBox.Items.Add(New ResultItem(result))
        Next
        If gameBox.Items.Count > 0 Then gameBox.SelectedIndex = 0
        card.Controls.Add(gameBox)

        card.Controls.Add(New Label With {.Text = "USB / destination drive", .ForeColor = Theme.TextPrimary, .Font = Theme.Font(9.0F, FontStyle.Bold), .AutoSize = True, .Location = New Point(20, 102), .BackColor = card.FillColor})
        driveBox.DropDownStyle = ComboBoxStyle.DropDownList
        driveBox.BackColor = Theme.Surface2
        driveBox.ForeColor = Theme.TextPrimary
        driveBox.FlatStyle = FlatStyle.Flat
        driveBox.Location = New Point(20, 132)
        driveBox.Size = New Size(520, 30)
        card.Controls.Add(driveBox)

        Dim refresh As New ModernButton With {.Text = "REFRESH", .FillColor = Theme.Surface2, .HoverColor = Color.FromArgb(36, 48, 78), .ForeColor = Theme.TextPrimary, .Location = New Point(552, 128), .Size = New Size(128, 38)}
        AddHandler refresh.Click, Sub() RefreshTargets()
        card.Controls.Add(refresh)

        progress.Location = New Point(20, 194)
        progress.Size = New Size(660, 8)
        card.Controls.Add(progress)

        status.Text = If(_results.Count = 0, "No completed conversions yet.", "Ready to deploy.")
        status.ForeColor = Theme.TextMuted
        status.AutoSize = True
        status.Location = New Point(20, 216)
        card.Controls.Add(status)

        Dim deploy As New ModernButton With {.Text = "DEPLOY TO USB", .FillColor = Theme.Accent, .HoverColor = Theme.AccentHover, .ForeColor = Theme.Background, .Location = New Point(548, 398), .Size = New Size(184, 42)}
        AddHandler deploy.Click, Async Sub() Await DeployAsync()
        Controls.Add(deploy)

        Dim cancel As New ModernButton With {.Text = "CANCEL", .FillColor = Theme.Surface2, .HoverColor = Color.FromArgb(36, 48, 78), .ForeColor = Theme.TextPrimary, .Location = New Point(424, 398), .Size = New Size(112, 42)}
        AddHandler cancel.Click, Sub()
                                     If _cts Is Nothing Then
                                         Close()
                                     Else
                                         _cts.Cancel()
                                     End If
                                 End Sub
        Controls.Add(cancel)
    End Sub

    Private Sub RefreshTargets()
        driveBox.Items.Clear()
        For Each target In _service.GetTargets()
            driveBox.Items.Add(target)
        Next
        If driveBox.Items.Count > 0 Then driveBox.SelectedIndex = 0
    End Sub

    Private Async Function DeployAsync() As Task
        Dim selectedResult = TryCast(gameBox.SelectedItem, ResultItem)
        Dim target = TryCast(driveBox.SelectedItem, UsbTarget)
        If selectedResult Is Nothing Then
            MessageBox.Show(Me, "Convert a game first, then open USB Deploy.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If target Is Nothing Then
            MessageBox.Show(Me, "Select a destination drive.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        _cts = New CancellationTokenSource()
        Try
            Dim p As New Progress(Of UsbDeploymentProgress)(
                Sub(x)
                    progress.Value = x.Percent
                    status.Text = $"{x.Percent}% — {x.CurrentFile}"
                End Sub)
            Dim destination = Await _service.DeployAsync(selectedResult.Result, target, p, _cts.Token)
            progress.Value = 100
            status.Text = "Deployment complete."
            MessageBox.Show(Me, $"Copied successfully to:{Environment.NewLine}{destination}", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As OperationCanceledException
            status.Text = "Deployment cancelled."
        Catch ex As Exception
            status.Text = "Deployment failed."
            MessageBox.Show(Me, ex.Message, "XenonForge USB Deploy", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            _cts.Dispose()
            _cts = Nothing
        End Try
    End Function

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
