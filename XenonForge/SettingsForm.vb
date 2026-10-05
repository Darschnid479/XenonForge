Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports XenonForge.Services

Public NotInheritable Class SettingsForm
    Inherits Form

    Private ReadOnly _settings As AppSettings
    Private ReadOnly outputBox As New TextBox()
    Private ReadOnly smartTrimCheck As New CheckBox()
    Private ReadOnly autoDeployCheck As New CheckBox()

    Public Sub New(settings As AppSettings)
        _settings = settings
        Text = "XenonForge — Settings"
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(720, 430)
        MinimumSize = New Size(720, 430)
        BackColor = Theme.Background
        ForeColor = Theme.TextPrimary
        Font = Theme.Font(10.0F)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        BuildUi()
    End Sub

    Private Sub BuildUi()
        Controls.Add(New Label With {
            .Text = "SETTINGS",
            .Font = Theme.Font(20.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(28, 24)
        })
        Controls.Add(New Label With {
            .Text = "Configure XenonForge defaults. Changes are saved locally on this PC.",
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(30, 62)
        })

        Dim card As New RoundedPanel With {
            .Location = New Point(28, 104),
            .Size = New Size(664, 230),
            .FillColor = Theme.Surface,
            .BorderColor = Theme.Border,
            .Radius = 16
        }
        Controls.Add(card)

        card.Controls.Add(New Label With {
            .Text = "Default GOD output folder",
            .ForeColor = Theme.TextPrimary,
            .Font = Theme.Font(9.0F, FontStyle.Bold),
            .AutoSize = True,
            .Location = New Point(20, 20),
            .BackColor = card.FillColor
        })

        outputBox.Text = _settings.OutputFolder
        outputBox.BackColor = Theme.Surface2
        outputBox.ForeColor = Theme.TextPrimary
        outputBox.BorderStyle = BorderStyle.FixedSingle
        outputBox.Location = New Point(20, 50)
        outputBox.Size = New Size(540, 30)
        card.Controls.Add(outputBox)

        Dim browse As New ModernButton With {
            .Text = "BROWSE",
            .FillColor = Theme.Surface2,
            .HoverColor = Color.FromArgb(36, 48, 78),
            .ForeColor = Theme.TextPrimary,
            .Location = New Point(572, 46),
            .Size = New Size(74, 38)
        }
        AddHandler browse.Click, AddressOf BrowseOutput
        card.Controls.Add(browse)

        smartTrimCheck.Text = "Use Smart Trim by default"
        smartTrimCheck.Checked = _settings.SmartTrim
        smartTrimCheck.ForeColor = Theme.TextPrimary
        smartTrimCheck.BackColor = card.FillColor
        smartTrimCheck.AutoSize = True
        smartTrimCheck.Location = New Point(20, 106)
        card.Controls.Add(smartTrimCheck)

        autoDeployCheck.Text = "Automatically deploy after conversion when a USB target is selected"
        autoDeployCheck.Checked = _settings.AutoDeployAfterConvert
        autoDeployCheck.ForeColor = Theme.TextPrimary
        autoDeployCheck.BackColor = card.FillColor
        autoDeployCheck.AutoSize = True
        autoDeployCheck.Location = New Point(20, 142)
        card.Controls.Add(autoDeployCheck)

        Dim clearCovers As New ModernButton With {
            .Text = "CLEAR COVER CACHE",
            .FillColor = Theme.Surface2,
            .HoverColor = Color.FromArgb(36, 48, 78),
            .ForeColor = Theme.TextPrimary,
            .Location = New Point(20, 178),
            .Size = New Size(176, 38)
        }
        AddHandler clearCovers.Click,
            Sub()
                New CoverArtService().ClearCache()
                MessageBox.Show(Me, "Cover cache cleared.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End Sub
        card.Controls.Add(clearCovers)

        Dim save As New ModernButton With {
            .Text = "SAVE SETTINGS",
            .FillColor = Theme.Accent,
            .HoverColor = Theme.AccentHover,
            .ForeColor = Theme.Background,
            .Location = New Point(516, 362),
            .Size = New Size(176, 42)
        }
        AddHandler save.Click, AddressOf SaveAndClose
        Controls.Add(save)

        Dim cancel As New ModernButton With {
            .Text = "CANCEL",
            .FillColor = Theme.Surface2,
            .HoverColor = Color.FromArgb(36, 48, 78),
            .ForeColor = Theme.TextPrimary,
            .Location = New Point(392, 362),
            .Size = New Size(112, 42)
        }
        AddHandler cancel.Click, Sub() DialogResult = DialogResult.Cancel
        Controls.Add(cancel)
    End Sub

    Private Sub BrowseOutput(sender As Object, e As EventArgs)
        Using dialog As New FolderBrowserDialog With {.Description = "Choose XenonForge output folder", .UseDescriptionForTitle = True}
            If Directory.Exists(outputBox.Text) Then dialog.SelectedPath = outputBox.Text
            If dialog.ShowDialog(Me) = DialogResult.OK Then outputBox.Text = dialog.SelectedPath
        End Using
    End Sub

    Private Sub SaveAndClose(sender As Object, e As EventArgs)
        Dim output = outputBox.Text.Trim()
        If String.IsNullOrWhiteSpace(output) Then
            MessageBox.Show(Me, "Choose an output folder.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        _settings.OutputFolder = output
        _settings.SmartTrim = smartTrimCheck.Checked
        _settings.AutoDeployAfterConvert = autoDeployCheck.Checked
        _settings.Save()
        DialogResult = DialogResult.OK
    End Sub
End Class
