Imports System.Collections.Generic
Imports System.IO

Namespace Engine
    Public Enum XboxDiscKind
        Unknown = 0
        XSF
        XGD1
        XGD2
        XGD3
    End Enum

    Public Enum XboxContentType As UInteger
        XboxOriginal = &H5000UI
        GamesOnDemand = &H7000UI
    End Enum

    Public NotInheritable Class XboxTitleInfo
        Public Property IsoPath As String = String.Empty
        Public Property DisplayName As String = String.Empty
        Public Property TitleId As UInteger
        Public Property MediaId As UInteger
        Public Property Version As UInteger
        Public Property BaseVersion As UInteger
        Public Property Platform As Byte
        Public Property ExecutableType As Byte
        Public Property DiscNumber As Byte = 1
        Public Property DiscCount As Byte = 1
        Public Property DiscKind As XboxDiscKind
        Public Property PartitionOffset As Long
        Public Property RootDirectorySector As UInteger
        Public Property RootDirectorySize As UInteger
        Public Property ContentType As XboxContentType = XboxContentType.GamesOnDemand
        Public Property IsoSize As Long
        Public Property UsedDataSize As Long

        Public ReadOnly Property TitleIdHex As String
            Get
                Return TitleId.ToString("X8")
            End Get
        End Property

        Public ReadOnly Property MediaIdHex As String
            Get
                Dim value = If(ContentType = XboxContentType.XboxOriginal, TitleId, MediaId)
                Return value.ToString("X8")
            End Get
        End Property

        Public ReadOnly Property ContentTypeHex As String
            Get
                Return CUInt(ContentType).ToString("X8")
            End Get
        End Property

        Public ReadOnly Property FileName As String
            Get
                Return Path.GetFileName(IsoPath)
            End Get
        End Property
    End Class

    Public NotInheritable Class XdvdfsEntry
        Public Property Sector As UInteger
        Public Property Size As UInteger
        Public Property Attributes As Byte
        Public Property Name As String = String.Empty
        Public Property Children As List(Of XdvdfsEntry)

        Public ReadOnly Property IsDirectory As Boolean
            Get
                Return (Attributes And &H10) <> 0
            End Get
        End Property
    End Class

    Public NotInheritable Class ConversionProgress
        Public Property Percent As Integer
        Public Property Stage As String = String.Empty
        Public Property CurrentPart As Integer
        Public Property TotalParts As Integer
        Public Property BytesProcessed As Long
        Public Property TotalBytes As Long
    End Class

    Public NotInheritable Class GodConversionResult
        Public Property Info As XboxTitleInfo = New XboxTitleInfo()
        Public Property OutputTitleFolder As String = String.Empty
        Public Property HeaderFile As String = String.Empty
        Public Property DataFolder As String = String.Empty
        Public Property TotalBytes As Long
    End Class
End Namespace
