Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports XenonForge.Engine
Imports XenonForge.Services

Public NotInheritable Class LibraryForm
    Inherits Form

    Private ReadOnly _root As String
    Private ReadOnly _covers As New CoverArtService()
    Private ReadOnly list As New ListView()
    Private ReadOnly cover As New PictureBox()
    Private ReadOnly titleLabel As New Label()
    Private ReadOnly metaLabel As New Label()
    Private _coverCts As CancellationTokenSource

    Public Sub New(root As String)
        _root = root
        Text = "XenonForge — Library"
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(980, 620)
        MinimumSize = New Size(900, 560)
        BackColor = Theme.Background
        ForeColor = Theme.TextPrimary
        Font = Theme.Font(10.0F)
        BuildUi()
        ScanLibrary()
    End Sub

    Private Sub BuildUi()
        Controls.Add(New Label With {
            .Text = "GOD LIBRARY",
            .Font = Theme.Font(20.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(28, 24)
        })
        Controls.Add(New Label With {
            .Text = "Finished packages found in your XenonForge output folder.",
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(30, 62)
        })

        list.View = View.Details
        list.FullRowSelect = True
        list.HideSelection = False
        list.BorderStyle = BorderStyle.None
        list.BackColor = Theme.Surface
        list.ForeColor = Theme.TextPrimary
        list.Font = Theme.Font(9.0F)
        list.Location = New Point(28, 102)
        list.Size = New Size(590, 486)
        list.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left
        list.Columns.Add("Game", 280)
        list.Columns.Add("Title ID", 100)
        list.Columns.Add("Type", 100)
        list.Columns.Add("Size", 90)
        AddHandler list.SelectedIndexChanged, AddressOf SelectionChanged
        Controls.Add(list)

        Dim card As New RoundedPanel With {
            .Location = New Point(638, 102),
            .Size = New Size(314, 486),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
            .FillColor = Theme.Surface,
            .BorderColor = Theme.Border,
            .Radius = 16
        }
        Controls.Add(card)

        cover.Location = New Point(72, 24)
        cover.Size = New Size(170, 238)
        cover.SizeMode = PictureBoxSizeMode.Zoom
        cover.BackColor = Color.FromArgb(10, 15, 27)
        card.Controls.Add(cover)

        titleLabel.Text = "Select a game"
        titleLabel.Font = Theme.Font(14.0F, FontStyle.Bold)
        titleLabel.ForeColor = Theme.TextPrimary
        titleLabel.TextAlign = ContentAlignment.TopCenter
        titleLabel.Location = New Point(20, 282)
        titleLabel.Size = New Size(274, 62)
        titleLabel.BackColor = card.FillColor
        card.Controls.Add(titleLabel)

        metaLabel.ForeColor = Theme.TextMuted
        metaLabel.Font = Theme.Font(9.0F)
        metaLabel.Location = New Point(24, 354)
        metaLabel.Size = New Size(266, 90)
        metaLabel.BackColor = card.FillColor
        card.Controls.Add(metaLabel)

        Dim openFolder As New ModernButton With {
            .Text = "OPEN PACKAGE FOLDER",
            .FillColor = Theme.Surface2,
            .HoverColor = Color.FromArgb(36, 48, 78),
            .ForeColor = Theme.TextPrimary,
            .Location = New Point(54, 438),
            .Size = New Size(206, 38)
        }
        AddHandler openFolder.Click, AddressOf OpenSelectedFolder
        card.Controls.Add(openFolder)
    End Sub

    Private Sub ScanLibrary()
        list.Items.Clear()
        If Not Directory.Exists(_root) Then Return

        For Each titleDir In Directory.EnumerateDirectories(_root)
            Dim titleHex = Path.GetFileName(titleDir)
            Dim titleId As UInteger
            If titleHex.Length <> 8 OrElse Not UInteger.TryParse(titleHex, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, titleId) Then Continue For

            For Each contentDir In Directory.EnumerateDirectories(titleDir)
                Dim contentType = Path.GetFileName(contentDir)
                If contentType <> "00007000" AndAlso contentType <> "00005000" Then Continue For

                Dim packageFiles = Directory.EnumerateFiles(contentDir).ToList()
                If packageFiles.Count = 0 Then Continue For

                Dim row As New ListViewItem(GameCatalog.ResolveTitle(titleId, titleHex))
                row.SubItems.Add(titleHex)
                row.SubItems.Add(If(contentType = "00007000", "Xbox 360 GOD", "Xbox Original"))
                row.SubItems.Add(FormatBytes(GetDirectorySize(titleDir)))
                row.Tag = New LibraryEntry With {
                    .TitleIdHex = titleHex,
                    .Name = row.Text,
                    .ContentType = contentType,
                    .Folder = titleDir
                }
                list.Items.Add(row)
            Next
        Next

        If list.Items.Count > 0 Then list.Items(0).Selected = True
    End Sub

    Private Async Sub SelectionChanged(sender As Object, e As EventArgs)
        If list.SelectedItems.Count = 0 Then Return
        Dim entry = TryCast(list.SelectedItems(0).Tag, LibraryEntry)
        If entry Is Nothing Then Return

        titleLabel.Text = entry.Name
        metaLabel.Text = $"Title ID   {entry.TitleIdHex}" & Environment.NewLine &
                         $"Package    {If(entry.ContentType = "00007000", "Games on Demand", "Xbox Original")}" & Environment.NewLine &
                         $"Folder     {entry.Folder}"

        _coverCts?.Cancel()
        _coverCts?.Dispose()
        _coverCts = New CancellationTokenSource()

        Try
            Dim bytes = Await _covers.GetCoverBytesAsync(entry.TitleIdHex, _coverCts.Token)
            If bytes Is Nothing Then
                SetCover(Nothing)
                Return
            End If
            Using ms As New MemoryStream(bytes)
                Using img = Image.FromStream(ms)
                    SetCover(New Bitmap(img))
                End Using
            End Using
        Catch ex As OperationCanceledException
        Catch
            SetCover(Nothing)
        End Try
    End Sub

    Private Sub SetCover(image As Image)
        Dim old = cover.Image
        cover.Image = image
        If old IsNot Nothing Then old.Dispose()
    End Sub

    Private Sub OpenSelectedFolder(sender As Object, e As EventArgs)
        If list.SelectedItems.Count = 0 Then Return
        Dim entry = TryCast(list.SelectedItems(0).Tag, LibraryEntry)
        If entry Is Nothing OrElse Not Directory.Exists(entry.Folder) Then Return
        Process.Start(New ProcessStartInfo("explorer.exe", $"""{entry.Folder}""") With {.UseShellExecute = True})
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

    Private NotInheritable Class LibraryEntry
        Public Property TitleIdHex As String
        Public Property Name As String
        Public Property ContentType As String
        Public Property Folder As String
    End Class

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _coverCts?.Cancel()
            _coverCts?.Dispose()
            SetCover(Nothing)
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
