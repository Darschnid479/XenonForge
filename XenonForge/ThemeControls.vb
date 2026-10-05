Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Friend Module Theme
    Public ReadOnly Background As Color = Color.FromArgb(9, 13, 26)
    Public ReadOnly Surface As Color = Color.FromArgb(16, 22, 40)
    Public ReadOnly Surface2 As Color = Color.FromArgb(22, 30, 52)
    Public ReadOnly Border As Color = Color.FromArgb(42, 53, 79)
    Public ReadOnly TextPrimary As Color = Color.FromArgb(241, 245, 249)
    Public ReadOnly TextMuted As Color = Color.FromArgb(148, 163, 184)
    Public ReadOnly Accent As Color = Color.FromArgb(62, 207, 142)
    Public ReadOnly AccentHover As Color = Color.FromArgb(76, 224, 158)
    Public ReadOnly Blue As Color = Color.FromArgb(91, 157, 255)
    Public ReadOnly Warning As Color = Color.FromArgb(251, 191, 36)
    Public ReadOnly Danger As Color = Color.FromArgb(248, 113, 113)

    Public Function Font(size As Single, Optional style As FontStyle = FontStyle.Regular) As System.Drawing.Font
        Return New System.Drawing.Font("Segoe UI", size, style, GraphicsUnit.Point)
    End Function
End Module

Friend Class RoundedPanel
    Inherits Panel

    Public Property Radius As Integer = 16
    Public Property BorderColor As Color = Theme.Border
    Public Property FillColor As Color = Theme.Surface

    Public Sub New()
        DoubleBuffered = True
        BackColor = Color.Transparent
        Padding = New Padding(1)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect = New Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1))
        Using path = CreateRoundRect(rect, Radius)
            Using brush As New SolidBrush(FillColor)
                e.Graphics.FillPath(brush, path)
            End Using
            Using pen As New Pen(BorderColor)
                e.Graphics.DrawPath(pen, path)
            End Using
        End Using
    End Sub

    Private Shared Function CreateRoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d = Math.Max(2, radius * 2)
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function
End Class

Friend Class ModernButton
    Inherits Button

    Private _fill As Color = Theme.Surface2
    Private _hover As Color = Color.FromArgb(31, 42, 69)

    Public Property FillColor As Color
        Get
            Return _fill
        End Get
        Set(value As Color)
            _fill = value
            BackColor = value
            Invalidate()
        End Set
    End Property

    Public Property HoverColor As Color
        Get
            Return _hover
        End Get
        Set(value As Color)
            _hover = value
        End Set
    End Property

    Public Sub New()
        FlatStyle = FlatStyle.Flat
        FlatAppearance.BorderSize = 0
        BackColor = _fill
        ForeColor = Theme.TextPrimary
        Font = Theme.Font(10.0F, FontStyle.Bold)
        Cursor = Cursors.Hand
        Height = 42
        Padding = New Padding(12, 0, 12, 0)
        TextAlign = ContentAlignment.MiddleCenter
        UseVisualStyleBackColor = False
        AddHandler MouseEnter, Sub() BackColor = _hover
        AddHandler MouseLeave, Sub() BackColor = _fill
    End Sub
End Class

Friend Class ModernProgressBar
    Inherits Control

    Private _value As Integer
    Public Property Value As Integer
        Get
            Return _value
        End Get
        Set(v As Integer)
            _value = Math.Max(0, Math.Min(100, v))
            Invalidate()
        End Set
    End Property

    Public Sub New()
        DoubleBuffered = True
        Height = 8
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect = New Rectangle(0, 0, Width - 1, Height - 1)
        Using bg As New SolidBrush(Color.FromArgb(32, 42, 65))
            e.Graphics.FillRectangle(bg, rect)
        End Using
        If _value > 0 Then
            Dim w = CInt((Width - 1) * _value / 100.0)
            Using fg As New SolidBrush(Theme.Accent)
                e.Graphics.FillRectangle(fg, New Rectangle(0, 0, w, Height - 1))
            End Using
        End If
    End Sub
End Class
