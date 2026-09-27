Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports UniversalPOS.Core

Namespace UI

    Public Enum BtnKind
        Primary
        Secondary
        Success
        Danger
        Warning
        Link
    End Enum

    ''' <summary>Colors, fonts and small factory helpers so every screen looks the same.</summary>
    Public Module Theme
        Public ReadOnly Primary As Color = Color.FromArgb(37, 99, 235)
        Public ReadOnly PrimaryDark As Color = Color.FromArgb(29, 78, 216)
        Public ReadOnly Sidebar As Color = Color.FromArgb(17, 24, 39)
        Public ReadOnly SidebarHover As Color = Color.FromArgb(31, 41, 55)
        Public ReadOnly SidebarActive As Color = Color.FromArgb(37, 99, 235)
        Public ReadOnly Background As Color = Color.FromArgb(243, 244, 246)
        Public ReadOnly Card As Color = Color.White
        Public ReadOnly Border As Color = Color.FromArgb(229, 231, 235)
        Public ReadOnly Text As Color = Color.FromArgb(17, 24, 39)
        Public ReadOnly Muted As Color = Color.FromArgb(107, 114, 128)
        Public ReadOnly Success As Color = Color.FromArgb(22, 163, 74)
        Public ReadOnly Danger As Color = Color.FromArgb(220, 38, 38)
        Public ReadOnly Warning As Color = Color.FromArgb(217, 119, 6)
        Public ReadOnly InfoColor As Color = Color.FromArgb(8, 145, 178)
        Public ReadOnly Purple As Color = Color.FromArgb(124, 58, 237)

        Private _scale As Single = 1.0F

        Public Sub SetScale(percent As Integer)
            _scale = Math.Max(0.8F, Math.Min(1.5F, percent / 100.0F))
        End Sub

        Public Function Fnt(size As Single, Optional style As FontStyle = FontStyle.Regular) As Font
            Return New Font("Segoe UI", size * _scale, style)
        End Function

        Private _icon As Icon

        ''' <summary>The exe's own icon, for window title bars.</summary>
        Public ReadOnly Property AppIcon As Icon
            Get
                If _icon Is Nothing Then
                    Try
                        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath)
                    Catch
                    End Try
                End If
                Return _icon
            End Get
        End Property

        Public ReadOnly Property BaseFont As Font
            Get
                Return Fnt(9.75F)
            End Get
        End Property
    End Module

    Public Module Ui

        Public Function Btn(text As String, Optional kind As BtnKind = BtnKind.Primary, Optional onClick As Action = Nothing) As Button
            Dim b As New Button With {
                .Text = text,
                .AutoSize = True,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Padding = New Padding(10, 4, 10, 4),
                .Margin = New Padding(0, 0, 8, 0),
                .Font = Theme.Fnt(9.5F, FontStyle.Bold),
                .UseVisualStyleBackColor = False
            }
            Dim back As Color, fore As Color = Color.White
            Select Case kind
                Case BtnKind.Primary : back = Theme.Primary
                Case BtnKind.Success : back = Theme.Success
                Case BtnKind.Danger : back = Theme.Danger
                Case BtnKind.Warning : back = Theme.Warning
                Case BtnKind.Link : back = Theme.Card : fore = Theme.Primary
                Case Else : back = Color.White : fore = Theme.Text
            End Select
            b.BackColor = back
            b.ForeColor = fore
            b.FlatAppearance.BorderColor = If(kind = BtnKind.Secondary, Theme.Border, back)
            b.FlatAppearance.BorderSize = 1
            b.FlatAppearance.MouseOverBackColor = If(kind = BtnKind.Secondary OrElse kind = BtnKind.Link, Theme.Background, ControlPaint.Dark(back, 0.05F))
            If onClick IsNot Nothing Then AddHandler b.Click, Sub() onClick()
            Return b
        End Function

        Public Function Lbl(text As String, Optional size As Single = 9.75F, Optional style As FontStyle = FontStyle.Regular, Optional color As Color = Nothing) As Label
            Return New Label With {
                .Text = text,
                .AutoSize = True,
                .Font = Theme.Fnt(size, style),
                .ForeColor = If(color.IsEmpty, Theme.Text, color),
                .Margin = New Padding(0, 6, 8, 0),
                .UseMnemonic = False
            }
        End Function

        Public Function Txt(Optional width As Integer = 220, Optional placeholder As String = "") As TextBox
            Return New TextBox With {.Width = width, .Font = Theme.BaseFont, .PlaceholderText = placeholder, .Margin = New Padding(0, 2, 8, 2)}
        End Function

        Public Function NumBox(Optional decimals As Integer = 2, Optional width As Integer = 120, Optional max As Decimal = 999999999D) As NumericUpDown
            Return New NumericUpDown With {
                .DecimalPlaces = decimals,
                .Maximum = max,
                .Minimum = 0,
                .Width = width,
                .ThousandsSeparator = True,
                .Font = Theme.BaseFont,
                .TextAlign = HorizontalAlignment.Right,
                .Margin = New Padding(0, 2, 8, 2)
            }
        End Function

        Public Function DatePick(Optional value As Date = Nothing) As DateTimePicker
            Return New DateTimePicker With {
                .Format = DateTimePickerFormat.Custom,
                .CustomFormat = "dd MMM yyyy",
                .Width = 130,
                .Font = Theme.BaseFont,
                .Value = If(value = Date.MinValue, Date.Today, value),
                .Margin = New Padding(0, 2, 8, 2)
            }
        End Function

        Public Function Combo(Optional width As Integer = 220) As ComboBox
            Return New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = width, .Font = Theme.BaseFont, .Margin = New Padding(0, 2, 8, 2)}
        End Function

        ''' <summary>A searchable combo (type to filter, like the web version's searchable select).</summary>
        Public Function SearchCombo(Optional width As Integer = 260) As ComboBox
            Dim c As New ComboBox With {
                .DropDownStyle = ComboBoxStyle.DropDown,
                .AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                .AutoCompleteSource = AutoCompleteSource.ListItems,
                .Width = width,
                .Font = Theme.BaseFont,
                .Margin = New Padding(0, 2, 8, 2)
            }
            Return c
        End Function

        Public Sub Bind(combo As ComboBox, table As DataTable, display As String, Optional value As String = "id", Optional addBlank As String = Nothing)
            Dim t = table.Copy()
            If addBlank IsNot Nothing Then
                Dim r = t.NewRow()
                r(value) = 0L
                r(display) = addBlank
                t.Rows.InsertAt(r, 0)
            End If
            combo.DataSource = Nothing
            combo.DisplayMember = display
            combo.ValueMember = value
            combo.DataSource = t
        End Sub

        Public Function SelectedId(combo As ComboBox) As Long
            If combo.SelectedValue Is Nothing OrElse combo.SelectedValue Is DBNull.Value Then Return 0
            Dim v As Long
            If Long.TryParse(Convert.ToString(combo.SelectedValue, Fmt.Inv), v) Then Return v
            Return 0
        End Function

        Public Sub SelectId(combo As ComboBox, id As Long)
            If combo.DataSource Is Nothing Then Return
            combo.SelectedValue = id
            If combo.SelectedIndex < 0 AndAlso combo.Items.Count > 0 Then combo.SelectedIndex = 0
        End Sub

        Public Function Flow(Optional wrap As Boolean = True) As FlowLayoutPanel
            Return New FlowLayoutPanel With {
                .AutoSize = True,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .WrapContents = wrap,
                .FlowDirection = FlowDirection.LeftToRight,
                .Margin = New Padding(0),
                .Padding = New Padding(0)
            }
        End Function

        ' ---------------- Grids ----------------

        Public Function Grid() As DataGridView
            Dim g As New DataGridView With {
                .Dock = DockStyle.Fill,
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                .BackgroundColor = Theme.Card,
                .BorderStyle = BorderStyle.None,
                .CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                .GridColor = Theme.Border,
                .EnableHeadersVisualStyles = False,
                .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                .ColumnHeadersHeight = CInt(34 * Theme.Fnt(10).Size / 10),
                .Font = Theme.BaseFont
            }
            g.RowTemplate.Height = CInt(30 * Theme.Fnt(10).Size / 10)
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251)
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted
            g.ColumnHeadersDefaultCellStyle.Font = Theme.Fnt(9, FontStyle.Bold)
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(249, 250, 251)
            g.ColumnHeadersDefaultCellStyle.Padding = New Padding(4, 0, 4, 0)
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254)
            g.DefaultCellStyle.SelectionForeColor = Theme.Text
            g.DefaultCellStyle.Padding = New Padding(4, 0, 4, 0)
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 251)
            ' Double buffering stops flicker when scrolling big lists.
            GetType(DataGridView).GetProperty("DoubleBuffered", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).SetValue(g, True, Nothing)
            Return g
        End Function

        ''' <summary>
        ''' Binds a table and applies friendly headers. headers: "col=Header" pairs; columns not
        ''' listed are hidden. Decimal columns get money format, date-like columns get dd MMM yyyy.
        ''' </summary>
        Private ReadOnly _gridHeaders As New Dictionary(Of DataGridView, String())

        Public Sub ShowTable(g As DataGridView, table As DataTable, ParamArray headers As String())
            If Not _gridHeaders.ContainsKey(g) Then
                ' Re-apply whenever the grid (re)binds, e.g. when a hidden tab becomes visible.
                AddHandler g.DataBindingComplete, Sub() ApplyColumns(g)
                AddHandler g.CellFormatting, AddressOf FormatDates
                AddHandler g.Disposed, Sub() _gridHeaders.Remove(g)
            End If
            _gridHeaders(g) = headers
            g.DataSource = Nothing
            g.AutoGenerateColumns = True
            g.DataSource = table
            ApplyColumns(g)
        End Sub

        Private Sub ApplyColumns(g As DataGridView)
            Dim headers As String() = Nothing
            If Not _gridHeaders.TryGetValue(g, headers) Then Return
            Dim table = TryCast(g.DataSource, DataTable)
            If table Is Nothing Then Return
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim order As New List(Of String)
            For Each h In headers
                Dim parts = h.Split("="c)
                map(parts(0)) = If(parts.Length > 1, parts(1), parts(0))
                order.Add(parts(0).ToLowerInvariant())
            Next
            For Each col As DataGridViewColumn In g.Columns
                Dim name = col.DataPropertyName
                If Not map.ContainsKey(name) OrElse Not table.Columns.Contains(name) Then
                    col.Visible = False
                    Continue For
                End If
                col.HeaderText = map(name)
                col.SortMode = DataGridViewColumnSortMode.Automatic
                Dim dtype = table.Columns(name).DataType
                If dtype Is GetType(Decimal) OrElse dtype Is GetType(Double) Then
                    col.DefaultCellStyle.Format = "#,##0.00"
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                    col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
                ElseIf dtype Is GetType(Long) OrElse dtype Is GetType(Integer) Then
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                    col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
                End If
            Next
            ' Order visible columns as listed.
            Dim visible = g.Columns.Cast(Of DataGridViewColumn)().Where(Function(c) c.Visible).OrderBy(Function(c) order.IndexOf(c.DataPropertyName.ToLowerInvariant())).ToList()
            For i = 0 To visible.Count - 1
                visible(i).DisplayIndex = i
            Next
        End Sub

        ''' <summary>
        ''' Registers per-cell styling for a data-bound grid (call once, e.g. in the constructor).
        ''' The styler gets the data row, the column name and the style to change. Using the
        ''' formatting event keeps the styling correct after sorting / rebinding.
        ''' </summary>
        Public Sub OnFormat(g As DataGridView, styler As Action(Of DataRow, String, DataGridViewCellStyle))
            AddHandler g.CellFormatting, Sub(s, e)
                                             If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.RowIndex >= g.Rows.Count Then Return
                                             Dim drv = TryCast(g.Rows(e.RowIndex).DataBoundItem, DataRowView)
                                             If drv Is Nothing Then Return
                                             styler(drv.Row, g.Columns(e.ColumnIndex).DataPropertyName, e.CellStyle)
                                         End Sub
        End Sub

        Private Sub FormatDates(sender As Object, e As DataGridViewCellFormattingEventArgs)
            Dim g = DirectCast(sender, DataGridView)
            If e.ColumnIndex < 0 OrElse e.Value Is Nothing OrElse e.Value Is DBNull.Value Then Return
            Dim name = g.Columns(e.ColumnIndex).DataPropertyName.ToLowerInvariant()
            If TypeOf e.Value Is String AndAlso (name.EndsWith("_date") OrElse name = "date" OrElse name = "day") Then
                Dim s = CStr(e.Value)
                If s.Length = 10 AndAlso s(4) = "-"c Then
                    e.Value = Fmt.ShowDate(s)
                    e.FormattingApplied = True
                End If
            End If
        End Sub

        ''' <summary>Ends any cell edit and commits the row being typed into the bound DataTable.</summary>
        Public Sub CommitGrid(g As DataGridView)
            g.EndEdit()
            If g.DataSource Is Nothing OrElse g.BindingContext Is Nothing Then Return
            Dim cm = TryCast(g.BindingContext(g.DataSource, g.DataMember), CurrencyManager)
            If cm IsNot Nothing Then cm.EndCurrentEdit()
        End Sub

        ''' <summary>Sums qty * price over a table's view, including a row still being typed.</summary>
        Public Function SumProduct(t As DataTable, qtyCol As String, priceCol As String) As Decimal
            Dim total = 0D
            For Each drv As DataRowView In t.DefaultView
                Dim q = drv(qtyCol)
                Dim p = drv(priceCol)
                If q Is DBNull.Value OrElse p Is DBNull.Value Then Continue For
                total += Convert.ToDecimal(q) * Convert.ToDecimal(p)
            Next
            Return total
        End Function

        Public Function SelectedRow(g As DataGridView) As DataRow
            If g.CurrentRow Is Nothing Then Return Nothing
            Dim drv = TryCast(g.CurrentRow.DataBoundItem, DataRowView)
            Return If(drv Is Nothing, Nothing, drv.Row)
        End Function

        Public Function SelectedRowId(g As DataGridView, Optional col As String = "id") As Long
            Dim r = SelectedRow(g)
            Return If(r Is Nothing, 0L, Fmt.GetLng(r, col))
        End Function

        ''' <summary>Exports the visible columns of a grid to a CSV file (opens in Excel).</summary>
        Public Sub ExportCsv(g As DataGridView, suggestedName As String)
            Using dlg As New SaveFileDialog With {.Filter = "CSV (Excel) (*.csv)|*.csv", .FileName = suggestedName & "_" & Date.Today.ToString("yyyyMMdd") & ".csv"}
                If dlg.ShowDialog() <> DialogResult.OK Then Return
                Dim cols = g.Columns.Cast(Of DataGridViewColumn)().Where(Function(c) c.Visible).OrderBy(Function(c) c.DisplayIndex).ToList()
                Dim sb As New StringBuilder()
                sb.AppendLine(String.Join(",", cols.Select(Function(c) Csv(c.HeaderText))))
                For Each row As DataGridViewRow In g.Rows
                    sb.AppendLine(String.Join(",", cols.Select(Function(c) Csv(Convert.ToString(row.Cells(c.Index).FormattedValue, Fmt.Inv)))))
                Next
                File.WriteAllText(dlg.FileName, sb.ToString(), New UTF8Encoding(True))
                Info("Exported " & g.Rows.Count & " rows to" & Environment.NewLine & dlg.FileName)
            End Using
        End Sub

        Private Function Csv(v As String) As String
            v = If(v, "")
            If v.Contains(","c) OrElse v.Contains(""""c) OrElse v.Contains(ControlChars.Lf) Then Return """" & v.Replace("""", """""") & """"
            Return v
        End Function

        ' ---------------- Messages ----------------

        Public Sub Info(msg As String)
            MessageBox.Show(msg, "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Public Sub Warn(msg As String)
            MessageBox.Show(msg, "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Sub

        Public Function Confirm(msg As String) As Boolean
            Return MessageBox.Show(msg, "Please confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.Yes
        End Function

        ''' <summary>Runs an action and shows business errors as friendly messages. Returns True on success.</summary>
        Public Function Attempt(action As Action) As Boolean
            Try
                Cursor.Current = Cursors.WaitCursor
                action()
                Return True
            Catch ex As BusinessException
                Warn(ex.Message)
                Return False
            Catch ex As Exception
                MessageBox.Show("Something went wrong:" & Environment.NewLine & ex.Message, "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            Finally
                Cursor.Current = Cursors.Default
            End Try
        End Function

    End Module

End Namespace
