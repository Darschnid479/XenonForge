Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports XenonForge.Engine
Imports XenonForge.Services

Public Class MainForm
    Inherits Form

    Private ReadOnly _settings As AppSettings = AppSettings.Load()
    Private ReadOnly _usb As New UsbDeploymentService()
    Private _cts As CancellationTokenSource
    Private _busy As Boolean

    Private ReadOnly queue As New ListView()
    Private ReadOnly outputBox As New TextBox()
    Private ReadOnly usbBox As New ComboBox()
    Private ReadOnly trimCheck As New CheckBox()
    Private ReadOnly progress As New ModernProgressBar()
    Private ReadOnly status As New Label()
    Private ReadOnly detailName As New Label()
    Private ReadOnly detailMeta As New Label()
    Private ReadOnly logBox As New RichTextBox()

    Public Sub New()
        Text = "XenonForge — Xbox 360 GOD Studio"
        StartPosition = FormStartPosition.CenterScreen
        MinimumSize = New Size(1180, 760)
        ClientSize = New Size(1420, 880)
        BackColor = Theme.Background
        ForeColor = Theme.TextPrimary
        Font = Theme.Font(10.0F)
        AllowDrop = True
        DoubleBuffered = True

        BuildUi()
        LoadSettings()
        RefreshUsb()

        AddHandler DragEnter, AddressOf OnDragEnterFiles
        AddHandler DragDrop, AddressOf OnDragDropFiles
        AddHandler FormClosing, AddressOf OnClosing
    End Sub

    Private Sub BuildUi()
        Dim sidebar As New Panel With {
            .Dock = DockStyle.Left,
            .Width = 220,
            .BackColor = Color.FromArgb(11, 16, 30),
            .Padding = New Padding(18)
        }
        Controls.Add(sidebar)

        Dim brand As New Label With {
            .Text = "XF",
            .Font = Theme.Font(15.0F, FontStyle.Bold),
            .ForeColor = Theme.Background,
            .BackColor = Theme.Accent,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Location = New Point(18, 24),
            .Size = New Size(44, 44)
        }
        sidebar.Controls.Add(brand)

        sidebar.Controls.Add(New Label With {
            .Text = "XENONFORGE",
            .Font = Theme.Font(13.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(72, 25)
        })
        sidebar.Controls.Add(New Label With {
            .Text = "GOD STUDIO",
            .Font = Theme.Font(8.5F, FontStyle.Bold),
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(73, 50)
        })

        AddSideLabel(sidebar, "●  CONVERT", 120, True)
        AddSideLabel(sidebar, "▣  USB DEPLOY", 166, False)
        AddSideLabel(sidebar, "◫  LIBRARY", 212, False)
        AddSideLabel(sidebar, "⚙  SETTINGS", 258, False)

        Dim engineCard As New RoundedPanel With {
            .Location = New Point(18, 660),
            .Size = New Size(184, 132),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Bottom,
            .FillColor = Color.FromArgb(14, 21, 38),
            .BorderColor = Color.FromArgb(36, 51, 72),
            .Radius = 14
        }
        engineCard.Controls.Add(New Label With {
            .Text = "NATIVE ENGINE",
            .ForeColor = Theme.Accent,
            .Font = Theme.Font(8.0F, FontStyle.Bold),
            .AutoSize = True,
            .Location = New Point(14, 14),
            .BackColor = engineCard.FillColor
        })
        engineCard.Controls.Add(New Label With {
            .Text = "Pure VB.NET" & Environment.NewLine & "No iso2god.exe",
            .ForeColor = Theme.TextPrimary,
            .Font = Theme.Font(10.0F, FontStyle.Bold),
            .Size = New Size(155, 50),
            .Location = New Point(14, 38),
            .BackColor = engineCard.FillColor
        })
        engineCard.Controls.Add(New Label With {
            .Text = "XGD1 / XGD2 / XGD3 / XSF",
            .ForeColor = Theme.TextMuted,
            .Font = Theme.Font(8.0F),
            .Size = New Size(160, 24),
            .Location = New Point(14, 98),
            .BackColor = engineCard.FillColor
        })
        sidebar.Controls.Add(engineCard)

        Dim main As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(28, 22, 28, 22), .BackColor = Theme.Background}
        Controls.Add(main)
        main.BringToFront()

        main.Controls.Add(New Label With {
            .Text = "Turn clean ISOs into dashboard-ready GOD packages.",
            .Font = Theme.Font(21.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(28, 22)
        })
        main.Controls.Add(New Label With {
            .Text = "Identify the game • convert natively • deploy straight to USB",
            .Font = Theme.Font(10.0F),
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(31, 64)
        })

        Dim addIso = MakeButton("+  ADD ISO", Theme.Accent, Theme.AccentHover, Theme.Background)
        addIso.Location = New Point(28, 104)
        addIso.Width = 124
        AddHandler addIso.Click, AddressOf AddIsoClick
        main.Controls.Add(addIso)

        Dim addFolder = MakeButton("ADD FOLDER", Theme.Surface2, Color.FromArgb(36, 48, 78), Theme.TextPrimary)
        addFolder.Location = New Point(162, 104)
        addFolder.Width = 126
        AddHandler addFolder.Click, AddressOf AddFolderClick
        main.Controls.Add(addFolder)

        Dim convert = MakeButton("CONVERT QUEUE", Theme.Blue, Color.FromArgb(112, 172, 255), Color.White)
        convert.Location = New Point(302, 104)
        convert.Width = 154
        AddHandler convert.Click, Async Sub() Await ConvertQueueAsync(False)
        main.Controls.Add(convert)

        Dim convertUsb = MakeButton("CONVERT + USB", Color.FromArgb(115, 88, 220), Color.FromArgb(136, 108, 235), Color.White)
        convertUsb.Location = New Point(466, 104)
        convertUsb.Width = 154
        AddHandler convertUsb.Click, Async Sub() Await ConvertQueueAsync(True)
        main.Controls.Add(convertUsb)

        Dim cancel = MakeButton("CANCEL", Color.FromArgb(80, 38, 47), Color.FromArgb(112, 47, 58), Theme.Danger)
        cancel.Location = New Point(630, 104)
        cancel.Width = 92
        AddHandler cancel.Click, Sub() _cts?.Cancel()
        main.Controls.Add(cancel)

        Dim split As New TableLayoutPanel With {
            .Location = New Point(28, 162),
            .Size = New Size(main.ClientSize.Width - 56, main.ClientSize.Height - 246),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
            .ColumnCount = 2,
            .RowCount = 1,
            .BackColor = Theme.Background
        }
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 66.0F))
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34.0F))
        main.Controls.Add(split)

        Dim queueCard As New RoundedPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 12, 0), .FillColor = Theme.Surface, .BorderColor = Theme.Border, .Radius = 16}
        split.Controls.Add(queueCard, 0, 0)
        queueCard.Controls.Add(New Label With {
            .Text = "CONVERSION QUEUE",
            .Font = Theme.Font(9.0F, FontStyle.Bold),
            .ForeColor = Theme.TextPrimary,
            .AutoSize = True,
            .Location = New Point(18, 16),
            .BackColor = queueCard.FillColor
        })
        queueCard.Controls.Add(New Label With {
            .Text = "Drop Redump/Xbox ISO files here. Metadata is read before conversion.",
            .Font = Theme.Font(8.5F),
            .ForeColor = Theme.TextMuted,
            .AutoSize = True,
            .Location = New Point(18, 40),
            .BackColor = queueCard.FillColor
        })

        queue.View = View.Details
        queue.FullRowSelect = True
        queue.HideSelection = False
        queue.BorderStyle = BorderStyle.None
        queue.BackColor = Theme.Surface
        queue.ForeColor = Theme.TextPrimary
        queue.Font = Theme.Font(9.0F)
        queue.Location = New Point(18, 72)
        queue.Size = New Size(queueCard.Width - 36, queueCard.Height - 90)
        queue.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        queue.Columns.Add("Game", 250)
        queue.Columns.Add("Title ID", 90)
        queue.Columns.Add("Disc", 60)
        queue.Columns.Add("Format", 70)
        queue.Columns.Add("Size", 85)
        queue.Columns.Add("Status", 110)
        AddHandler queue.SelectedIndexChanged, AddressOf QueueSelectionChanged
        queueCard.Controls.Add(queue)

        Dim right As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0), .RowCount = 2, .ColumnCount = 1, .BackColor = Theme.Background}
        right.RowStyles.Add(New RowStyle(SizeType.Percent, 54.0F))
        right.RowStyles.Add(New RowStyle(SizeType.Percent, 46.0F))
        split.Controls.Add(right, 1, 0)

        Dim details As New RoundedPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 10), .FillColor = Theme.Surface, .BorderColor = Theme.Border, .Radius = 16}
        right.Controls.Add(details, 0, 0)
        details.Controls.Add(New Label With {.Text = "GAME DETAILS", .Font = Theme.Font(9.0F, FontStyle.Bold), .ForeColor = Theme.TextPrimary, .AutoSize = True, .Location = New Point(18, 16), .BackColor = details.FillColor})
        detailName.Text = "Select a game"
        detailName.Font = Theme.Font(16.0F, FontStyle.Bold)
        detailName.ForeColor = Theme.TextPrimary
        detailName.Location = New Point(18, 52)
        detailName.Size = New Size(350, 60)
        detailName.BackColor = details.FillColor
        details.Controls.Add(detailName)
        detailMeta.Text = "Title ID —" & Environment.NewLine & "Media ID —" & Environment.NewLine & "Disc —" & Environment.NewLine & "Format —"
        detailMeta.Font = Theme.Font(9.5F)
        detailMeta.ForeColor = Theme.TextMuted
        detailMeta.Location = New Point(18, 116)
        detailMeta.Size = New Size(350, 100)
        detailMeta.BackColor = details.FillColor
        details.Controls.Add(detailMeta)

        trimCheck.Text = "Smart trim unused tail space"
        trimCheck.ForeColor = Theme.TextPrimary
        trimCheck.BackColor = details.FillColor
        trimCheck.AutoSize = True
        trimCheck.Location = New Point(18, 224)
        details.Controls.Add(trimCheck)

        outputBox.BackColor = Theme.Surface2
        outputBox.ForeColor = Theme.TextPrimary
        outputBox.BorderStyle = BorderStyle.FixedSingle
        outputBox.Location = New Point(18, 258)
        outputBox.Size = New Size(270, 28)
        outputBox.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        details.Controls.Add(outputBox)

        Dim browse = MakeButton("…", Theme.Surface2, Color.FromArgb(36, 48, 78), Theme.TextPrimary)
        browse.Location = New Point(298, 254)
        browse.Size = New Size(46, 36)
        browse.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        AddHandler browse.Click, AddressOf BrowseOutput
        details.Controls.Add(browse)

        usbBox.DropDownStyle = ComboBoxStyle.DropDownList
        usbBox.BackColor = Theme.Surface2
        usbBox.ForeColor = Theme.TextPrimary
        usbBox.FlatStyle = FlatStyle.Flat
        usbBox.Location = New Point(18, 300)
        usbBox.Size = New Size(326, 30)
        usbBox.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        details.Controls.Add(usbBox)

        Dim refreshUsb = MakeButton("REFRESH DRIVES", Theme.Surface2, Color.FromArgb(36, 48, 78), Theme.TextPrimary)
        refreshUsb.Location = New Point(18, 340)
        refreshUsb.Width = 128
        refreshUsb.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        AddHandler refreshUsb.Click, Sub() RefreshUsb()
        details.Controls.Add(refreshUsb)

        Dim deploy = MakeButton("DEPLOY SELECTED", Theme.Accent, Theme.AccentHover, Theme.Background)
        deploy.Location = New Point(156, 340)
        deploy.Width = 150
        deploy.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        AddHandler deploy.Click, Async Sub() Await DeploySelectedAsync()
        details.Controls.Add(deploy)

        Dim activity As New RoundedPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 0), .FillColor = Theme.Surface, .BorderColor = Theme.Border, .Radius = 16}
        right.Controls.Add(activity, 0, 1)
        activity.Controls.Add(New Label With {.Text = "ACTIVITY", .Font = Theme.Font(9.0F, FontStyle.Bold), .ForeColor = Theme.TextPrimary, .AutoSize = True, .Location = New Point(18, 14), .BackColor = activity.FillColor})
        logBox.Location = New Point(14, 42)
        logBox.Size = New Size(activity.Width - 28, activity.Height - 56)
        logBox.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        logBox.BackColor = Color.FromArgb(10, 15, 27)
        logBox.ForeColor = Color.FromArgb(167, 180, 200)
        logBox.BorderStyle = BorderStyle.None
        logBox.Font = New Font("Consolas", 8.7F)
        logBox.ReadOnly = True
        activity.Controls.Add(logBox)

        progress.Location = New Point(28, main.ClientSize.Height - 54)
        progress.Size = New Size(main.ClientSize.Width - 56, 8)
        progress.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        main.Controls.Add(progress)
        status.Text = "Ready. Drop an ISO or click ADD ISO."
        status.ForeColor = Theme.TextMuted
        status.Font = Theme.Font(8.5F)
        status.AutoSize = True
        status.Location = New Point(28, main.ClientSize.Height - 35)
        status.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        main.Controls.Add(status)
    End Sub

    Private Shared Sub AddSideLabel(parent As Control, text As String, y As Integer, active As Boolean)
        parent.Controls.Add(New Label With {
            .Text = text,
            .Location = New Point(18, y),
            .Size = New Size(184, 40),
            .Padding = New Padding(10, 0, 0, 0),
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = Theme.Font(9.0F, FontStyle.Bold),
            .ForeColor = If(active, Theme.Accent, Theme.TextMuted),
            .BackColor = If(active, Color.FromArgb(18, 43, 48), Color.Transparent)
        })
    End Sub

    Private Shared Function MakeButton(text As String, fill As Color, hover As Color, fore As Color) As ModernButton
        Return New ModernButton With {.Text = text, .FillColor = fill, .HoverColor = hover, .ForeColor = fore, .Height = 42}
    End Function

    Private Sub LoadSettings()
        outputBox.Text = _settings.OutputFolder
        trimCheck.Checked = _settings.SmartTrim
    End Sub

    Private Sub SaveSettings()
        _settings.OutputFolder = outputBox.Text.Trim()
        _settings.SmartTrim = trimCheck.Checked
        Dim selected = TryCast(usbBox.SelectedItem, UsbTarget)
        _settings.LastUsbRoot = If(selected Is Nothing, String.Empty, selected.RootPath)
        _settings.Save()
    End Sub

    Private Sub AddIsoClick(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog With {.Filter = "Xbox ISO (*.iso)|*.iso", .Multiselect = True, .Title = "Add Xbox ISO"}
            If dialog.ShowDialog(Me) = DialogResult.OK Then AddIsoFiles(dialog.FileNames)
        End Using
    End Sub

    Private Sub AddFolderClick(sender As Object, e As EventArgs)
        Using dialog As New FolderBrowserDialog With {.Description = "Scan a folder for Xbox ISO files", .UseDescriptionForTitle = True}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            AddIsoFiles(Directory.EnumerateFiles(dialog.SelectedPath, "*.iso", SearchOption.AllDirectories))
        End Using
    End Sub

    Private Sub AddIsoFiles(paths As IEnumerable(Of String))
        For Each path In paths
            If Not File.Exists(path) Then Continue For
            If Not Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase) Then Continue For
            If queue.Items.Cast(Of ListViewItem)().Any(Function(x) String.Equals(CStr(x.Tag), path, StringComparison.OrdinalIgnoreCase)) Then Continue For

            Dim row As New ListViewItem("Reading metadata…")
            row.SubItems.Add("—")
            row.SubItems.Add("—")
            row.SubItems.Add("—")
            row.SubItems.Add(FormatBytes(New FileInfo(path).Length))
            row.SubItems.Add("Scanning")
            row.Tag = path
            queue.Items.Add(row)
            ScanRowAsync(row, path)
        Next
    End Sub

    Private Async Sub ScanRowAsync(row As ListViewItem, path As String)
        Try
            Dim info = Await Task.Run(Function() XboxExecutableParser.ReadTitleInfo(path))
            row.Tag = info
            row.Text = info.DisplayName
            row.SubItems(1).Text = info.TitleIdHex
            row.SubItems(2).Text = $"{Math.Max(1, CInt(info.DiscNumber))}/{Math.Max(1, CInt(info.DiscCount))}"
            row.SubItems(3).Text = info.DiscKind.ToString()
            row.SubItems(4).Text = FormatBytes(info.IsoSize)
            row.SubItems(5).Text = "Ready"
            row.ToolTipText = info.IsoPath
            AppendLog($"SCAN  {info.DisplayName}  [{info.TitleIdHex}]  {info.DiscKind}")
            If queue.SelectedItems.Count = 0 Then row.Selected = True
        Catch ex As Exception
            row.Text = Path.GetFileNameWithoutExtension(path)
            row.SubItems(5).Text = "Invalid"
            row.ToolTipText = ex.Message
            AppendLog($"ERROR {Path.GetFileName(path)} — {ex.Message}")
        End Try
    End Sub

    Private Async Function ConvertQueueAsync(deployAfter As Boolean) As Task
        If _busy Then Return
        Dim ready = queue.Items.Cast(Of ListViewItem)().Where(Function(x) TypeOf x.Tag Is XboxTitleInfo).ToList()
        If ready.Count = 0 Then
            MessageBox.Show(Me, "Add a valid Xbox ISO first.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim target As UsbTarget = Nothing
        If deployAfter Then
            target = TryCast(usbBox.SelectedItem, UsbTarget)
            If target Is Nothing Then
                MessageBox.Show(Me, "Select a USB/destination drive first.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
        End If

        Dim root = outputBox.Text.Trim()
        If String.IsNullOrWhiteSpace(root) Then root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XenonForge")
        Directory.CreateDirectory(root)

        _busy = True
        _cts = New CancellationTokenSource()
        SetBusy(True)
        Try
            For Each row In ready
                _cts.Token.ThrowIfCancellationRequested()
                Dim info = DirectCast(row.Tag, XboxTitleInfo)
                row.SubItems(5).Text = "Converting"
                Dim converter As New GodConverter()
                Dim cp As New Progress(Of ConversionProgress)(
                    Sub(p)
                        progress.Value = p.Percent
                        status.Text = $"{p.Stage}  {p.Percent}%"
                        row.SubItems(5).Text = $"{p.Percent}%"
                    End Sub)
                AppendLog($"START {info.DisplayName}")
                Dim result = Await converter.ConvertAsync(info, root, If(trimCheck.Checked, GodTrimMode.SmartTrim, GodTrimMode.FullImage), cp, _cts.Token)
                row.Tag = New RowState With {.Info = info, .Result = result}
                row.SubItems(5).Text = "Done"
                AppendLog($"DONE  {info.DisplayName} → {result.OutputTitleFolder}")
                If deployAfter Then Await DeployResultAsync(row, result, target, _cts.Token)
            Next
            progress.Value = 100
            status.Text = "Queue complete."
        Catch ex As OperationCanceledException
            AppendLog("CANCEL Conversion cancelled.")
            status.Text = "Cancelled."
        Catch ex As Exception
            AppendLog($"FAIL  {ex.Message}")
            MessageBox.Show(Me, ex.Message, "XenonForge conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            status.Text = "Failed."
        Finally
            _busy = False
            SetBusy(False)
            _cts.Dispose()
            _cts = Nothing
        End Try
    End Function

    Private Async Function DeploySelectedAsync() As Task
        If _busy OrElse queue.SelectedItems.Count = 0 Then Return
        Dim state = TryCast(queue.SelectedItems(0).Tag, RowState)
        If state Is Nothing OrElse state.Result Is Nothing Then
            MessageBox.Show(Me, "Convert the selected game first.", "XenonForge", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim target = TryCast(usbBox.SelectedItem, UsbTarget)
        If target Is Nothing Then Return

        _busy = True
        _cts = New CancellationTokenSource()
        SetBusy(True)
        Try
            Await DeployResultAsync(queue.SelectedItems(0), state.Result, target, _cts.Token)
        Catch ex As Exception
            AppendLog($"USBERR {ex.Message}")
            MessageBox.Show(Me, ex.Message, "USB deploy failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            _busy = False
            SetBusy(False)
            _cts.Dispose()
            _cts = Nothing
        End Try
    End Function

    Private Async Function DeployResultAsync(row As ListViewItem, result As GodConversionResult, target As UsbTarget, token As CancellationToken) As Task
        AppendLog($"USB   {result.Info.DisplayName} → {target.RootPath}")
        Dim dp As New Progress(Of UsbDeploymentProgress)(
            Sub(p)
                progress.Value = p.Percent
                status.Text = $"USB {p.Percent}% — {p.CurrentFile}"
                row.SubItems(5).Text = $"USB {p.Percent}%"
            End Sub)
        Dim destination = Await _usb.DeployAsync(result, target, dp, token)
        row.SubItems(5).Text = "On USB"
        AppendLog($"USBOK {destination}")
    End Function

    Private Sub RefreshUsb()
        Dim previous = _settings.LastUsbRoot
        Dim targets = _usb.GetTargets()
        usbBox.Items.Clear()
        For Each target In targets
            usbBox.Items.Add(target)
        Next
        If usbBox.Items.Count > 0 Then
            Dim index = targets.FindIndex(Function(t) t.RootPath.Equals(previous, StringComparison.OrdinalIgnoreCase))
            usbBox.SelectedIndex = If(index >= 0, index, 0)
        End If
        AppendLog($"DRIVE {targets.Count} destination drive(s) detected.")
    End Sub

    Private Sub QueueSelectionChanged(sender As Object, e As EventArgs)
        If queue.SelectedItems.Count = 0 Then Return
        Dim tag = queue.SelectedItems(0).Tag
        Dim info = TryCast(tag, XboxTitleInfo)
        If info Is Nothing Then
            Dim state = TryCast(tag, RowState)
            If state IsNot Nothing Then info = state.Info
        End If
        If info Is Nothing Then Return
        detailName.Text = info.DisplayName
        detailMeta.Text = $"Title ID   {info.TitleIdHex}" & Environment.NewLine &
                          $"Media ID   {info.MediaIdHex}" & Environment.NewLine &
                          $"Disc       {Math.Max(1, CInt(info.DiscNumber))} / {Math.Max(1, CInt(info.DiscCount))}" & Environment.NewLine &
                          $"Format     {info.DiscKind}   •   {FormatBytes(info.IsoSize)}"
    End Sub

    Private Sub BrowseOutput(sender As Object, e As EventArgs)
        Using dialog As New FolderBrowserDialog With {.Description = "Choose GOD output folder", .UseDescriptionForTitle = True}
            If Directory.Exists(outputBox.Text) Then dialog.SelectedPath = outputBox.Text
            If dialog.ShowDialog(Me) = DialogResult.OK Then outputBox.Text = dialog.SelectedPath
        End Using
    End Sub

    Private Sub OnDragEnterFiles(sender As Object, e As DragEventArgs)
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
    End Sub

    Private Sub OnDragDropFiles(sender As Object, e As DragEventArgs)
        Dim dropped = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
        If dropped Is Nothing Then Return
        Dim all As New List(Of String)()
        For Each item In dropped
            If File.Exists(item) Then
                all.Add(item)
            ElseIf Directory.Exists(item) Then
                all.AddRange(Directory.EnumerateFiles(item, "*.iso", SearchOption.AllDirectories))
            End If
        Next
        AddIsoFiles(all)
    End Sub

    Private Sub SetBusy(value As Boolean)
        progress.Value = If(value, 1, progress.Value)
        trimCheck.Enabled = Not value
        outputBox.Enabled = Not value
        usbBox.Enabled = Not value
    End Sub

    Private Sub AppendLog(text As String)
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}")
        logBox.SelectionStart = logBox.TextLength
        logBox.ScrollToCaret()
    End Sub

    Private Shared Function FormatBytes(value As Long) As String
        If value >= 1024L * 1024L * 1024L Then Return $"{value / 1024.0 / 1024.0 / 1024.0:0.00} GB"
        If value >= 1024L * 1024L Then Return $"{value / 1024.0 / 1024.0:0.0} MB"
        Return $"{value / 1024.0:0} KB"
    End Function

    Private Sub OnClosing(sender As Object, e As FormClosingEventArgs)
        SaveSettings()
        If _busy Then _cts?.Cancel()
    End Sub

    Private NotInheritable Class RowState
        Public Property Info As XboxTitleInfo
        Public Property Result As GodConversionResult
    End Class
End Class
