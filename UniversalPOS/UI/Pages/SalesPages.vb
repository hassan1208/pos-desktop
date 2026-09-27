Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    ''' <summary>Date range picker with quick presets, shared by list screens.</summary>
    Public Class RangeBar
        Inherits FlowLayoutPanel

        Public ReadOnly FromPicker As DateTimePicker = Ui.DatePick()
        Public ReadOnly ToPicker As DateTimePicker = Ui.DatePick()
        Private ReadOnly _preset As ComboBox = Ui.Combo(130)
        Private _silent As Boolean

        Public Event RangeChanged()

        Public Sub New(Optional defaultPreset As String = "This month")
            AutoSize = True
            WrapContents = False
            Margin = New Padding(0, 0, 8, 0)
            _preset.Items.AddRange({"Today", "Yesterday", "Last 7 days", "This month", "Last month", "This year", "All time", "Custom"})
            Controls.Add(_preset)
            Controls.Add(Ui.Lbl("From"))
            Controls.Add(FromPicker)
            Controls.Add(Ui.Lbl("To"))
            Controls.Add(ToPicker)
            AddHandler _preset.SelectedIndexChanged, Sub() ApplyPreset()
            AddHandler FromPicker.ValueChanged, Sub() Changed()
            AddHandler ToPicker.ValueChanged, Sub() Changed()
            _preset.SelectedItem = defaultPreset
        End Sub

        Public ReadOnly Property FromDate As Date
            Get
                Return FromPicker.Value.Date
            End Get
        End Property

        Public ReadOnly Property ToDate As Date
            Get
                Return ToPicker.Value.Date
            End Get
        End Property

        Public ReadOnly Property Description As String
            Get
                Return FromDate.ToString("dd MMM yyyy", Fmt.Inv) & " - " & ToDate.ToString("dd MMM yyyy", Fmt.Inv)
            End Get
        End Property

        Private Sub ApplyPreset()
            Dim t = Date.Today
            Dim f = t, e = t
            Select Case CStr(_preset.SelectedItem)
                Case "Yesterday" : f = t.AddDays(-1) : e = f
                Case "Last 7 days" : f = t.AddDays(-6)
                Case "This month" : f = New Date(t.Year, t.Month, 1)
                Case "Last month" : f = New Date(t.Year, t.Month, 1).AddMonths(-1) : e = New Date(t.Year, t.Month, 1).AddDays(-1)
                Case "This year" : f = New Date(t.Year, 1, 1)
                Case "All time" : f = New Date(2000, 1, 1)
                Case "Custom" : Return
            End Select
            _silent = True
            FromPicker.Value = f
            ToPicker.Value = e
            _silent = False
            RaiseEvent RangeChanged()
        End Sub

        Private Sub Changed()
            If _silent Then Return
            If CStr(_preset.SelectedItem) <> "Custom" Then
                _silent = True
                _preset.SelectedItem = "Custom"
                _silent = False
            End If
            RaiseEvent RangeChanged()
        End Sub
    End Class

    Public Class SalesPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _range As New RangeBar()
        Private ReadOnly _search As TextBox = Ui.Txt(200, "Customer / phone / product / #")
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Bold, Theme.Text)
        Private ReadOnly _items As DataGridView = Ui.Grid()

        Public Sub New()
            MyBase.New("Sales History", "All sales. Double-click to view the invoice.")
            Toolbar.Controls.Add(_range)
            Toolbar.Controls.Add(_search)
            Toolbar.Controls.Add(Ui.Btn("Invoice", BtnKind.Primary, Sub() WithSelected(Sub(id) DocPrinter.Preview(Documents.SaleInvoice(id), FindForm()))))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, AddressOf EditSelected))
            If Session.IsAdmin Then Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            Toolbar.Controls.Add(Ui.Btn("Print List", BtnKind.Secondary, Sub() DocPrinter.Preview(Documents.FromGrid("SALES REPORT", _range.Description & "   " & _summary.Text, _grid), FindForm())))
            Toolbar.Controls.Add(Ui.Btn("Export CSV", BtnKind.Secondary, Sub() Ui.ExportCsv(_grid, "sales")))
            Toolbar.Controls.Add(_summary)

            Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal, .BackColor = Theme.Background}
            Dim sized = False
            AddHandler split.SizeChanged, Sub()
                                              If sized OrElse split.Height < 200 Then Return
                                              sized = True
                                              Try
                                                  split.SplitterDistance = CInt(split.Height * 0.66)
                                              Catch
                                              End Try
                                          End Sub
            Dim top = CardPanel()
            top.Controls.Add(_grid)
            Dim bottom = CardPanel("Items in selected sale")
            bottom.Controls.Add(_items)
            _items.BringToFront()
            split.Panel1.Controls.Add(top)
            split.Panel2.Controls.Add(bottom)
            Body.Controls.Add(split)

            AddHandler _range.RangeChanged, Sub() RefreshData()
            AddHandler _search.TextChanged, Sub() RefreshData()
            AddHandler _grid.SelectionChanged, Sub() ShowItems()
            Ui.OnFormat(_grid, Sub(r, col, st)
                                   If col = "due" AndAlso GetDec(r, "due") > 0 Then st.ForeColor = Theme.Danger
                               End Sub)
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then WithSelected(Sub(id) DocPrinter.Preview(Documents.SaleInvoice(id), FindForm()))
                                              End Sub
        End Sub

        Public Overrides Sub RefreshData()
            Dim dt = ReportService.SalesList(_range.FromDate, _range.ToDate, _search.Text)
            If Session.IsAdmin Then
                Ui.ShowTable(_grid, dt, "id=Invoice #", "sale_date=Date", "customer=Customer", "customer_phone=Phone", "items=Items", "discount=Discount", "total_amount=Total", "due=Udhaar", "total_profit=Profit", "user=By")
            Else
                Ui.ShowTable(_grid, dt, "id=Invoice #", "sale_date=Date", "customer=Customer", "customer_phone=Phone", "items=Items", "discount=Discount", "total_amount=Total", "due=Udhaar", "user=By")
            End If
            If _grid.Columns.Count > 0 Then _grid.Columns("customer").FillWeight = 160
            Dim total = dt.AsEnumerable().Sum(Function(r) GetDec(r, "total_amount"))
            Dim profit = dt.AsEnumerable().Sum(Function(r) GetDec(r, "total_profit"))
            _summary.Text = dt.Rows.Count & " sales  •  " & Fmt.Money(total) & If(Session.IsAdmin, "  •  profit " & Fmt.Money(profit), "")
            ShowItems()
        End Sub

        Private Sub ShowItems()
            Dim id = Ui.SelectedRowId(_grid)
            Ui.ShowTable(_items, SalesService.GetSaleItems(id), "product_name=Product", "quantity=Qty", "sale_price=Price", "line_total=Total")
        End Sub

        Private Sub WithSelected(action As Action(Of Long))
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 Then
                Ui.Warn("Please select a sale first.")
                Return
            End If
            Ui.Attempt(Sub() action(id))
        End Sub

        Private Sub EditSelected()
            WithSelected(Sub(id)
                             Using f As New SaleEditForm(id)
                                 If f.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
                             End Using
                         End Sub)
        End Sub

        Private Sub DeleteSelected()
            WithSelected(Sub(id)
                             If Ui.Confirm("Delete sale #" & Fmt.DocNo("", id) & "? Its items will be added back to stock.") Then
                                 SalesService.DeleteSale(id)
                                 RefreshData()
                             End If
                         End Sub)
        End Sub

    End Class

    Public Class QuotationsPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _status As ComboBox = Ui.Combo(130)
        Private ReadOnly _search As TextBox = Ui.Txt(200, "Customer / phone")

        Public Sub New()
            MyBase.New("Quotations", "Price quotes for customers - convert to a sale when accepted")
            Toolbar.Controls.Add(Ui.Btn("+ New Quotation", BtnKind.Primary, Sub() Edit(0)))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() Edit(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Print", BtnKind.Secondary, Sub() WithSelected(Sub(id) DocPrinter.Preview(Documents.Quotation(id), FindForm()))))
            Toolbar.Controls.Add(Ui.Btn("Set Status", BtnKind.Secondary, AddressOf SetStatus))
            Toolbar.Controls.Add(Ui.Btn("Convert to Sale", BtnKind.Success, AddressOf Convert))
            Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            _status.Items.Add("All statuses")
            _status.Items.AddRange(QuotationService.Statuses)
            _status.SelectedIndex = 0
            Toolbar.Controls.Add(_status)
            Toolbar.Controls.Add(_search)
            AddGridCard(_grid)
            AddHandler _status.SelectedIndexChanged, Sub() RefreshData()
            Ui.OnFormat(_grid, Sub(r, col, st)
                                   If col <> "status" Then Return
                                   Select Case GetStr(r, "status")
                                       Case "converted", "accepted" : st.ForeColor = Theme.Success
                                       Case "rejected" : st.ForeColor = Theme.Danger
                                       Case "sent" : st.ForeColor = Theme.Primary
                                   End Select
                               End Sub)
            AddHandler _search.TextChanged, Sub() RefreshData()
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then Edit(Ui.SelectedRowId(_grid))
                                              End Sub
        End Sub

        Public Overrides Sub RefreshData()
            Dim st = If(_status.SelectedIndex <= 0, "", CStr(_status.SelectedItem))
            Ui.ShowTable(_grid, QuotationService.Quotations(st, _search.Text), "id=Quote #", "quote_date=Date", "customer_name=Customer", "customer_phone=Phone", "items=Items", "total_amount=Total", "status=Status", "converted_sale_id=Sale #")
        End Sub

        Private Sub WithSelected(action As Action(Of Long))
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 Then
                Ui.Warn("Please select a quotation first.")
                Return
            End If
            Ui.Attempt(Sub() action(id))
        End Sub

        Private Sub Edit(id As Long)
            If id > 0 AndAlso GetStr(QuotationService.GetQuotation(id), "status") = "converted" Then
                Ui.Warn("This quotation was converted to a sale and can't be edited. Use Print to view it.")
                Return
            End If
            Using dlg As New QuotationDialog(id)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub SetStatus()
            WithSelected(Sub(id)
                             Dim menu As New ContextMenuStrip()
                             For Each st In QuotationService.Statuses.Where(Function(x) x <> "converted")
                                 Dim s = st
                                 menu.Items.Add(s, Nothing, Sub()
                                                                If Ui.Attempt(Sub() QuotationService.SetStatus(id, s)) Then RefreshData()
                                                            End Sub)
                             Next
                             menu.Show(Cursor.Position)
                         End Sub)
        End Sub

        Private Sub Convert()
            WithSelected(Sub(id)
                             If Not Ui.Confirm("Convert this quotation into a sale? Stock will be deducted.") Then Return
                             Dim saleId = QuotationService.ConvertToSale(id)
                             RefreshData()
                             If Ui.Confirm("Sale #" & Fmt.DocNo("", saleId) & " created. Print the invoice now?") Then SaleEditor.PrintInvoice(saleId, FindForm())
                         End Sub)
        End Sub

        Private Sub DeleteSelected()
            WithSelected(Sub(id)
                             If Ui.Confirm("Delete this quotation?") Then
                                 QuotationService.DeleteQuotation(id)
                                 RefreshData()
                             End If
                         End Sub)
        End Sub

    End Class

    ''' <summary>
    ''' Line-item editor used by quotations: pick a catalog product or type a custom item.
    ''' </summary>
    Public Class QuotationDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _date As DateTimePicker = Ui.DatePick()
        Private ReadOnly _customer As TextBox = Ui.Txt(260)
        Private ReadOnly _phone As TextBox = Ui.Txt(160)
        Private ReadOnly _status As ComboBox = Ui.Combo(130)
        Private ReadOnly _notes As TextBox = Ui.Txt(360)
        Private ReadOnly _product As ComboBox = Ui.SearchCombo(300)
        Private ReadOnly _qty As NumericUpDown = Ui.NumBox(0, 70, 100000)
        Private ReadOnly _price As NumericUpDown = Ui.NumBox(2, 110)
        Private ReadOnly _lines As New DataTable()
        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _total As Label = Ui.Lbl("", 12, FontStyle.Bold, Theme.Primary)
        Private ReadOnly _catalog As DataTable

        Public Sub New(id As Long)
            MyBase.New(If(id > 0, "Edit Quotation", "New Quotation"), 820, 640)
            _id = id
            _status.Items.AddRange(QuotationService.Statuses.Where(Function(s) s <> "converted").ToArray())
            _status.SelectedIndex = 0
            _qty.Minimum = 1
            AddField("Date", _date)
            AddField("Customer", _customer)
            AddField("Phone", _phone)
            AddField("Status", _status)
            AddField("Notes", _notes)

            _catalog = InventoryService.ProductPicker()
            Ui.Bind(_product, _catalog, "display")
            _product.SelectedIndex = -1
            _product.Text = ""
            AddHandler _product.SelectedIndexChanged, Sub()
                                                          Dim r = PickedProduct()
                                                          If r IsNot Nothing Then _price.Value = Math.Min(_price.Maximum, GetDec(r, "sale_price"))
                                                      End Sub

            _lines.Columns.Add("product_id", GetType(Long))
            _lines.Columns.Add("category_id", GetType(Long))
            _lines.Columns.Add("product_name", GetType(String))
            _lines.Columns.Add("quantity", GetType(Integer))
            _lines.Columns.Add("sale_price", GetType(Decimal))
            _lines.Columns.Add("line_total", GetType(Decimal), "quantity * sale_price")

            Dim adder = Ui.Flow(False)
            adder.Dock = DockStyle.Top
            adder.Padding = New Padding(8, 8, 8, 4)
            adder.Controls.AddRange({Ui.Lbl("Item"), _product, Ui.Lbl("Qty"), _qty, Ui.Lbl("Price"), _price,
                                     Ui.Btn("Add", BtnKind.Primary, Sub() AddLine()), Ui.Btn("Remove", BtnKind.Secondary, Sub() RemoveLine())})
            Dim hint = Ui.Lbl("Pick a product, or type any custom item name (e.g. ""Installation"").", 8.5F, FontStyle.Italic, Theme.Muted)
            hint.Dock = DockStyle.Top
            hint.Padding = New Padding(8, 0, 0, 4)
            _total.Dock = DockStyle.Bottom
            _total.TextAlign = ContentAlignment.MiddleRight
            _total.AutoSize = False
            _total.Height = 32
            _grid.ReadOnly = False
            Dim card = PageBase.CardPanel()
            card.Controls.Add(_grid)
            card.Controls.Add(_total)
            card.Controls.Add(hint)
            card.Controls.Add(adder)
            Content.Controls.Add(card)
            _lines.Columns("product_name").ReadOnly = True
            Ui.ShowTable(_grid, _lines, "product_name=Item", "quantity=Qty", "sale_price=Price", "line_total=Total")
            AddHandler _lines.ColumnChanged, Sub() UpdateTotal()
            AddHandler _lines.RowChanged, Sub() UpdateTotal()
            AddHandler _lines.RowDeleted, Sub() UpdateTotal()
            AddHandler _grid.DataError, Sub(s, e) e.ThrowException = False
            AddHandler _grid.CellEndEdit, Sub() UpdateTotal()
            ' Binding auto-selects the first product once the combo is created; start empty instead.
            AddHandler Shown, Sub()
                                  _product.SelectedIndex = -1
                                  _product.Text = ""
                                  _price.Value = 0
                                  _product.Focus()
                              End Sub

            If id > 0 Then
                Dim q = QuotationService.GetQuotation(id)
                _date.Value = Fmt.ParseDbDate(GetStr(q, "quote_date"))
                _customer.Text = GetStr(q, "customer_name")
                _phone.Text = GetStr(q, "customer_phone")
                _status.SelectedItem = GetStr(q, "status")
                _notes.Text = GetStr(q, "notes")
                For Each r As DataRow In QuotationService.GetItems(id).Rows
                    _lines.Rows.Add(GetLng(r, "product_id"), GetLng(r, "category_id"), GetStr(r, "product_name"), GetInt(r, "quantity"), GetDec(r, "sale_price"))
                Next
            End If
            UpdateTotal()
        End Sub

        Private Function PickedProduct() As DataRow
            If _product.SelectedIndex < 0 Then Return Nothing
            Dim drv = TryCast(_product.SelectedItem, DataRowView)
            If drv Is Nothing OrElse Not String.Equals(CStr(drv("display")), _product.Text, StringComparison.OrdinalIgnoreCase) Then Return Nothing
            Return drv.Row
        End Function

        Private Sub AddLine()
            Dim r = PickedProduct()
            If r IsNot Nothing Then
                _lines.Rows.Add(GetLng(r, "id"), GetLng(r, "category_id"), GetStr(r, "product_name"), CInt(_qty.Value), _price.Value)
            ElseIf _product.Text.Trim() <> "" Then
                _lines.Rows.Add(0L, 0L, _product.Text.Trim(), CInt(_qty.Value), _price.Value)
            Else
                Ui.Warn("Pick a product or type an item name.")
                Return
            End If
            _product.SelectedIndex = -1
            _product.Text = ""
            _qty.Value = 1
            _price.Value = 0
            _product.Focus()
        End Sub

        Private Sub RemoveLine()
            Dim r = Ui.SelectedRow(_grid)
            If r IsNot Nothing Then r.Delete()
        End Sub

        Private Sub UpdateTotal()
            _total.Text = "Total:  " & Fmt.Money(Ui.SumProduct(_lines, "quantity", "sale_price")) & "   "
        End Sub

        Protected Overrides Sub OnSave()
            Ui.CommitGrid(_grid)
            Dim lines As New List(Of QuoteLine)
            For Each r As DataRow In _lines.Rows
                If r.RowState = DataRowState.Deleted Then Continue For
                lines.Add(New QuoteLine With {.ProductId = GetLng(r, "product_id"), .CategoryId = GetLng(r, "category_id"), .ProductName = GetStr(r, "product_name"),
                                              .Quantity = GetInt(r, "quantity"), .SalePrice = GetDec(r, "sale_price")})
            Next
            If Ui.Attempt(Sub() QuotationService.SaveQuotation(_id, _date.Value.Date, _customer.Text, _phone.Text, CStr(_status.SelectedItem), _notes.Text, lines)) Then Done()
        End Sub

    End Class

End Namespace
