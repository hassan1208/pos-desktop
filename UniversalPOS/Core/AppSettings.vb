Imports System.Data
Imports System.IO

Namespace Core

    ''' <summary>Shop profile and preferences, stored as key/value rows in the settings table.</summary>
    Public Module AppSettings

        Private _cache As Dictionary(Of String, String)

        Public Sub Reload()
            _cache = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each r As DataRow In Data.Db.Query("SELECT setting_key, setting_value FROM settings").Rows
                _cache(Fmt.GetStr(r, "setting_key")) = Fmt.GetStr(r, "setting_value")
            Next
        End Sub

        Public Function GetValue(key As String, Optional defaultValue As String = "") As String
            If _cache Is Nothing Then Reload()
            Dim v As String = Nothing
            If _cache.TryGetValue(key, v) AndAlso v IsNot Nothing Then Return v
            Return defaultValue
        End Function

        Public Sub SetValue(key As String, value As String)
            Data.Db.Exec("INSERT INTO settings (setting_key, setting_value) VALUES (@p0, @p1) " &
                         "ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value", key, value)
            If _cache Is Nothing Then Reload()
            _cache(key) = value
        End Sub

        Public ReadOnly Property IsSetupDone As Boolean
            Get
                Return GetValue("setup_done") = "1"
            End Get
        End Property

        Public ReadOnly Property ShopName As String
            Get
                Return GetValue("shop_name", "My Shop")
            End Get
        End Property

        Public ReadOnly Property OwnerName As String
            Get
                Return GetValue("owner_name")
            End Get
        End Property

        Public ReadOnly Property Phone As String
            Get
                Return GetValue("phone")
            End Get
        End Property

        Public ReadOnly Property Email As String
            Get
                Return GetValue("email")
            End Get
        End Property

        Public ReadOnly Property Address As String
            Get
                Return GetValue("address")
            End Get
        End Property

        Public ReadOnly Property Slogan As String
            Get
                Return GetValue("slogan")
            End Get
        End Property

        Public ReadOnly Property CurrencySymbol As String
            Get
                Return GetValue("currency_symbol", "Rs.")
            End Get
        End Property

        Public ReadOnly Property LowStockThreshold As Integer
            Get
                Dim v = Fmt.ParseInt(GetValue("low_stock_threshold", "5"))
                Return Math.Max(0, v)
            End Get
        End Property

        ''' <summary>a4 | half_a4 | thermal</summary>
        Public ReadOnly Property InvoiceFormat As String
            Get
                Return GetValue("invoice_format", "a4")
            End Get
        End Property

        Public ReadOnly Property WatermarkText As String
            Get
                Return GetValue("watermark_text")
            End Get
        End Property

        Public ReadOnly Property InvoiceFooter As String
            Get
                Return GetValue("invoice_footer", "Thank you! Please visit again.")
            End Get
        End Property

        Public ReadOnly Property FontScale As Integer
            Get
                Dim v = Fmt.ParseInt(GetValue("ui_font_scale", "100"))
                Return Math.Min(150, Math.Max(80, If(v = 0, 100, v)))
            End Get
        End Property

        ''' <summary>Absolute path of the shop logo, or "" when none is set / file missing.</summary>
        Public ReadOnly Property LogoFile As String
            Get
                Dim rel = GetValue("logo_path")
                If rel = "" Then Return ""
                Dim full = Path.Combine(AppPaths.DataDir, rel)
                Return If(File.Exists(full), full, "")
            End Get
        End Property

        ''' <summary>Extra contact numbers printed on documents, e.g. "Shop: 0300-1234567".</summary>
        Public Function ContactLines() As List(Of String)
            Dim list As New List(Of String)
            For Each r As DataRow In Data.Db.Query("SELECT label, phone FROM shop_contact_numbers ORDER BY id").Rows
                Dim label = Fmt.GetStr(r, "label")
                list.Add(If(label = "", "", label & ": ") & Fmt.GetStr(r, "phone"))
            Next
            Return list
        End Function

    End Module

    ''' <summary>Where the app keeps its data (database, logo, bill images, backups).</summary>
    Public Module AppPaths

        Public ReadOnly Property DataDir As String
            Get
                ' Portable mode: if a "data" folder sits next to the exe, use it (USB / shared folder installs).
                Dim portable = Path.Combine(AppContext.BaseDirectory, "data")
                If Directory.Exists(portable) Then Return portable
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniversalPOS")
            End Get
        End Property

        Public ReadOnly Property DatabaseFile As String
            Get
                Return Path.Combine(DataDir, "pos.db")
            End Get
        End Property

        Public ReadOnly Property BillsDir As String
            Get
                Dim p = Path.Combine(DataDir, "bills")
                Directory.CreateDirectory(p)
                Return p
            End Get
        End Property

        Public ReadOnly Property BackupsDir As String
            Get
                Dim p = Path.Combine(DataDir, "backups")
                Directory.CreateDirectory(p)
                Return p
            End Get
        End Property

        ''' <summary>Copies a file into the data folder and returns its path relative to DataDir.</summary>
        Public Function StoreFile(sourceFile As String, subFolder As String, prefix As String) As String
            Dim dir = Path.Combine(DataDir, subFolder)
            Directory.CreateDirectory(dir)
            Dim name = prefix & "_" & Date.Now.ToString("yyyyMMddHHmmss") & "_" & Guid.NewGuid().ToString("N").Substring(0, 6) & Path.GetExtension(sourceFile).ToLowerInvariant()
            File.Copy(sourceFile, Path.Combine(dir, name), True)
            Return Path.Combine(subFolder, name)
        End Function

    End Module

End Namespace
