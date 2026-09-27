Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class CartRow
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Public Property ProductId As Long
        Public Property ProductName As String = ""
        ''' <summary>Units available for this line (current stock + what this sale already holds when editing).</summary>
        Public Property Available As Integer

        Private _qty As Integer = 1
        Public Property Qty As Integer
            Get
                Return _qty
            End Get
            Set(value As Integer)
                _qty = Math.Max(1, value)
                RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(Total)))
            End Set
        End Property

        Private _price As Decimal
        Public Property Price As Decimal
            Get
                Return _price
            End Get
            Set(value As Decimal)
                _price = Math.Max(0D, value)
                RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(Total)))
            End Set
        End Property

        Public ReadOnly Property Total As Decimal
            Get
                Return Fmt.Round2(_qty * _price)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' The point-of-sale screen: search / scan products on the left, cart and payment on the right.
    ''' Used for new sales (SalePage) and for editing an existing sale (SaleEditForm).
    ''' </summary>
    Public Class SaleEditor
        Inherits UserControl

        Private ReadOnly _search As TextBox
        Private ReadOnly _category As ComboBox
        Private ReadOnly _products As DataGridView
        Private ReadOnly _cartGrid As DataGridView
        Private ReadOnly _cart As New BindingList(Of CartRow)
        Private ReadOnly _customer As TextBox
        Private ReadOnly _phone As TextBox
        Private ReadOnly _date As DateTimePicker
        Private ReadOnly _discount As NumericUpDown
        Private ReadOnly _cash As NumericUpDown
        Private ReadOnly _credit As New CheckBox With {.Text = "Credit sale (udhaar) - customer pays later", .AutoSize = True, .ForeColor = Theme.Warning}
        Private _cashLbl As Label
        Private _changeCaption As Label
        Private ReadOnly _subtotalLbl As Label
        Private ReadOnly _totalLbl As Label
        Private ReadOnly _changeLbl As Label
        Private ReadOnly _saveBtn As Button
        Private ReadOnly _printBtn As Button
        Private _productTable As DataTable
        Private _saleId As Long
        Private ReadOnly _heldStock As New Dictionary(Of Long, Integer)

        ''' <summary>Raised after a sale was saved (sale id).</summary>
        Public Event Saved(saleId As Long)

        Public Sub New()
            Dock = DockStyle.Fill
            BackColor = Theme.Background
            Font = Theme.BaseFont

            Dim split As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1}
            split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))

            ' ---------- left: product finder ----------
            Dim left = PageBase.CardPanel()
            left.Margin = New Padding(0, 0, 8, 0)
            Dim finder = Ui.Flow(False)
            finder.Dock = DockStyle.Top
            finder.Padding = New Padding(10, 10, 10, 8)
            _search = Ui.Txt(300, "Scan barcode or type name, then Enter")
            _category = Ui.Combo(170)
            finder.Controls.Add(_search)
            finder.Controls.Add(_category)
            _products = Ui.Grid()
            Dim hint = Ui.Lbl("Double-click / Enter adds to cart.  ↓ moves into the list.", 8.5F, FontStyle.Regular, Theme.Muted)
            hint.Dock = DockStyle.Bottom
            hint.Padding = New Padding(10, 4, 0, 6)
            left.Controls.Add(_products)
            left.Controls.Add(hint)
            left.Controls.Add(finder)

            ' ---------- right: cart ----------
            Dim right = PageBase.CardPanel()
            right.Margin = New Padding(8, 0, 0, 0)

            Dim cust As New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 3, .AutoSize = True, .Padding = New Padding(10, 10, 10, 4)}
            cust.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45))
            cust.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
            cust.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
            _customer = Ui.Txt(100, "Customer name (optional)")
            _customer.Dock = DockStyle.Fill
            _phone = Ui.Txt(100, "Phone")
            _phone.Dock = DockStyle.Fill
            _date = Ui.DatePick()
            _date.Dock = DockStyle.Fill
            cust.Controls.Add(_customer, 0, 0)
            cust.Controls.Add(_phone, 1, 0)
            cust.Controls.Add(_date, 2, 0)

            _cartGrid = Ui.Grid()
            _cartGrid.ReadOnly = False
            _cartGrid.AutoGenerateColumns = False
            _cartGrid.SelectionMode = DataGridViewSelectionMode.CellSelect
            _cartGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            _cartGrid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "ProductName", .HeaderText = "Product", .ReadOnly = True, .FillWeight = 180})
            _cartGrid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Qty", .HeaderText = "Qty", .FillWeight = 45, .DefaultCellStyle = New DataGridViewCellStyle With {.Alignment = DataGridViewContentAlignment.MiddleCenter, .BackColor = Color.FromArgb(254, 252, 232)}})
            _cartGrid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Price", .HeaderText = "Price", .FillWeight = 70, .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "#,##0.00", .Alignment = DataGridViewContentAlignment.MiddleRight, .BackColor = Color.FromArgb(254, 252, 232)}})
            _cartGrid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Total", .HeaderText = "Total", .ReadOnly = True, .FillWeight = 80, .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "#,##0.00", .Alignment = DataGridViewContentAlignment.MiddleRight}})
            _cartGrid.Columns.Add(New DataGridViewButtonColumn With {.HeaderText = "", .Text = "×", .UseColumnTextForButtonValue = True, .FillWeight = 22, .FlatStyle = FlatStyle.Flat})
            _cartGrid.DataSource = _cart

            ' totals panel
            Dim totals As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .ColumnCount = 2, .AutoSize = True, .Padding = New Padding(10, 6, 10, 10), .BackColor = Color.FromArgb(249, 250, 251)}
            totals.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            totals.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            _subtotalLbl = Ui.Lbl("0.00", 11, FontStyle.Bold)
            _discount = Ui.NumBox(2, 140)
            _totalLbl = Ui.Lbl("0.00", 20, FontStyle.Bold, Theme.Primary)
            _cash = Ui.NumBox(2, 140)
            _changeLbl = Ui.Lbl("0.00", 11, FontStyle.Bold, Theme.Success)
            AddTotalRow(totals, "Subtotal", _subtotalLbl)
            AddTotalRow(totals, "Discount", _discount)
            AddTotalRow(totals, "TOTAL", _totalLbl)
            totals.RowCount += 1
            totals.Controls.Add(_credit, 0, totals.RowCount - 1)
            totals.SetColumnSpan(_credit, 2)
            _cashLbl = AddTotalRow(totals, "Cash received", _cash)
            _changeCaption = AddTotalRow(totals, "Change", _changeLbl)
            Dim btns = Ui.Flow(False)
            btns.Margin = New Padding(0, 8, 0, 0)
            _printBtn = Ui.Btn("Save && Print  (F10)", BtnKind.Success, Sub() Save(True))
            _saveBtn = Ui.Btn("Save  (F9)", BtnKind.Primary, Sub() Save(False))
            btns.Controls.Add(_printBtn)
            btns.Controls.Add(_saveBtn)
            btns.Controls.Add(Ui.Btn("Clear", BtnKind.Secondary, Sub()
                                                                      If _cart.Count = 0 OrElse Ui.Confirm("Clear the cart?") Then ResetForm()
                                                                  End Sub))
            totals.RowCount += 1
            totals.Controls.Add(btns, 0, totals.RowCount - 1)
            totals.SetColumnSpan(btns, 2)

            right.Controls.Add(_cartGrid)
            right.Controls.Add(totals)
            right.Controls.Add(cust)

            split.Controls.Add(left, 0, 0)
            split.Controls.Add(right, 1, 0)
            Controls.Add(split)

            ' ---------- events ----------
            AddHandler _search.TextChanged, Sub() FilterProducts()
            AddHandler _search.KeyDown, AddressOf SearchKeyDown
            AddHandler _category.SelectedIndexChanged, Sub() FilterProducts()
            AddHandler _products.CellDoubleClick, Sub(s, e)
                                                      If e.RowIndex >= 0 Then AddSelectedProduct()
                                                  End Sub
            AddHandler _products.KeyDown, Sub(s, e)
                                              If e.KeyCode = Keys.Enter Then
                                                  AddSelectedProduct()
                                                  e.Handled = True
                                                  _search.Focus()
                                                  _search.SelectAll()
                                              End If
                                          End Sub
            AddHandler _cart.ListChanged, Sub() UpdateTotals()
            AddHandler _discount.ValueChanged, Sub() UpdateTotals()
            AddHandler _cash.ValueChanged, Sub() UpdateTotals()
            AddHandler _credit.CheckedChanged, Sub() UpdateTotals()
            AddHandler _cartGrid.CellContentClick, Sub(s, e)
                                                       If e.RowIndex >= 0 AndAlso e.ColumnIndex = 4 Then _cart.RemoveAt(e.RowIndex)
                                                   End Sub
            AddHandler _cartGrid.DataError, Sub(s, e)
                                                e.ThrowException = False
                                                Ui.Warn("Please enter a valid number.")
                                            End Sub
            AddHandler _cartGrid.CellEndEdit, Sub() UpdateTotals()
            AddHandler _customer.Leave, Sub() FillPhoneFromHistory()
            Ui.OnFormat(_products, Sub(r, col, st)
                                       Dim stock = GetLng(r, "stock_quantity")
                                       If stock <= 0 Then
                                           st.ForeColor = Theme.Muted
                                       ElseIf col = "stock_quantity" AndAlso stock <= AppSettings.LowStockThreshold Then
                                           st.ForeColor = Theme.Danger
                                       End If
                                   End Sub)
        End Sub

        Private Shared Function AddTotalRow(t As TableLayoutPanel, label As String, ctl As Control) As Label
            t.RowCount += 1
            Dim l = Ui.Lbl(label, If(label = "TOTAL", 12, 9.5F), If(label = "TOTAL", FontStyle.Bold, FontStyle.Regular), Theme.Muted)
            l.Anchor = AnchorStyles.Left
            ctl.Anchor = AnchorStyles.Right
            t.Controls.Add(l, 0, t.RowCount - 1)
            t.Controls.Add(ctl, 1, t.RowCount - 1)
            Return l
        End Function

        ''' <summary>Reloads the product list and customer suggestions (call when the screen is shown).</summary>
        Public Sub ReloadLookups()
            Dim keepCat = Ui.SelectedId(_category)
            Ui.Bind(_category, InventoryService.Categories(), "name", "id", "All categories")
            Ui.SelectId(_category, keepCat)
            _productTable = InventoryService.ProductPicker()
            FilterProducts()
            Dim names As New AutoCompleteStringCollection()
            For Each r As DataRow In Data.Db.Query("SELECT DISTINCT customer_name FROM sales WHERE customer_name IS NOT NULL ORDER BY customer_name").Rows
                names.Add(GetStr(r, "customer_name"))
            Next
            _customer.AutoCompleteCustomSource = names
            _customer.AutoCompleteMode = AutoCompleteMode.SuggestAppend
            _customer.AutoCompleteSource = AutoCompleteSource.CustomSource
        End Sub

        Private Sub FilterProducts()
            If _productTable Is Nothing Then Return
            Dim view = New DataView(_productTable)
            Dim parts As New List(Of String)
            Dim cat = Ui.SelectedId(_category)
            If cat > 0 Then parts.Add("category_id = " & cat)
            Dim q = _search.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]")
            If q <> "" Then parts.Add("(product_name LIKE '%" & q & "%' OR barcode LIKE '%" & q & "%')")
            view.RowFilter = String.Join(" AND ", parts)
            Ui.ShowTable(_products, view.ToTable(), "product_name=Product", "sale_price=Price", "stock_quantity=Stock")
            If _products.Columns.Contains("product_name") Then _products.Columns("product_name").FillWeight = 220
        End Sub

        Private Sub SearchKeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode = Keys.Down AndAlso _products.Rows.Count > 0 Then
                _products.Focus()
                e.Handled = True
            ElseIf e.KeyCode = Keys.Enter Then
                e.Handled = True
                e.SuppressKeyPress = True
                ' Barcode first (scanners type the code + Enter), else the single / first match.
                Dim hit = InventoryService.FindByBarcode(_search.Text)
                If hit IsNot Nothing Then
                    AddProduct(GetLng(hit, "id"))
                ElseIf _products.Rows.Count > 0 Then
                    _products.CurrentCell = _products.Rows(0).Cells(_products.Columns("product_name").Index)
                    AddSelectedProduct()
                Else
                    Ui.Warn("No product found for """ & _search.Text & """.")
                End If
                _search.SelectAll()
            End If
        End Sub

        Private Sub AddSelectedProduct()
            Dim r = Ui.SelectedRow(_products)
            If r IsNot Nothing Then AddProduct(GetLng(r, "id"))
        End Sub

        Private Sub AddProduct(productId As Long)
            Dim p = InventoryService.GetProduct(productId)
            If p Is Nothing Then Return
            Dim available = GetInt(p, "stock_quantity") + If(_heldStock.ContainsKey(productId), _heldStock(productId), 0)
            Dim existing = _cart.FirstOrDefault(Function(c) c.ProductId = productId)
            Dim inCart = If(existing Is Nothing, 0, existing.Qty)
            If inCart + 1 > available Then
                Ui.Warn(GetStr(p, "product_name") & " has only " & available & " unit(s) in stock.")
                Return
            End If
            If existing IsNot Nothing Then
                existing.Qty += 1
                _cartGrid.Refresh()
                UpdateTotals()
            Else
                _cart.Add(New CartRow With {.ProductId = productId, .ProductName = GetStr(p, "product_name"), .Price = GetDec(p, "sale_price"), .Qty = 1, .Available = available})
            End If
        End Sub

        Private Sub UpdateTotals()
            Dim subtotal = _cart.Sum(Function(c) c.Total)
            _discount.Maximum = Math.Max(0D, subtotal)
            Dim total = subtotal - _discount.Value
            _subtotalLbl.Text = Fmt.Money(subtotal)
            _totalLbl.Text = Fmt.Money(total)
            If _credit.Checked Then
                _cashLbl.Text = "Paid now"
                _changeCaption.Text = "Balance (udhaar)"
                _changeLbl.Text = Fmt.Money(Math.Max(0D, total - _cash.Value))
                _changeLbl.ForeColor = Theme.Warning
            Else
                _cashLbl.Text = "Cash received"
                _changeCaption.Text = "Change"
                _changeLbl.Text = If(_cash.Value > 0, Fmt.Money(_cash.Value - total), "-")
                _changeLbl.ForeColor = If(_cash.Value > 0 AndAlso _cash.Value < total, Theme.Danger, Theme.Success)
            End If
        End Sub

        Private Sub FillPhoneFromHistory()
            If _phone.Text <> "" OrElse _customer.Text.Trim() = "" Then Return
            Dim ph = Data.Db.Scalar("SELECT customer_phone FROM sales WHERE customer_name = @p0 AND customer_phone IS NOT NULL ORDER BY id DESC LIMIT 1", _customer.Text.Trim())
            If ph IsNot Nothing Then _phone.Text = CStr(ph)
        End Sub

        ''' <summary>Loads an existing sale for editing.</summary>
        Public Sub LoadSale(saleId As Long)
            Dim sale = SalesService.GetSale(saleId)
            If sale Is Nothing Then Throw New BusinessException("Sale not found.")
            _saleId = saleId
            _customer.Text = GetStr(sale, "customer_name")
            _phone.Text = GetStr(sale, "customer_phone")
            _date.Value = Fmt.ParseDbDate(GetStr(sale, "sale_date"))
            _cart.Clear()
            _heldStock.Clear()
            For Each r As DataRow In SalesService.GetSaleItems(saleId).Rows
                Dim pid = GetLng(r, "product_id")
                If pid = 0 Then Continue For ' product deleted since; cannot be re-sold
                _heldStock(pid) = If(_heldStock.ContainsKey(pid), _heldStock(pid), 0) + GetInt(r, "quantity")
                _cart.Add(New CartRow With {.ProductId = pid, .ProductName = GetStr(r, "product_name"), .Qty = GetInt(r, "quantity"), .Price = GetDec(r, "sale_price")})
            Next
            _discount.Maximum = Math.Max(_discount.Maximum, GetDec(sale, "discount"))
            _discount.Value = GetDec(sale, "discount")
            Dim paidSoFar = GetDec(sale, "paid_amount")
            _credit.Checked = paidSoFar < GetDec(sale, "total_amount")
            _cash.Value = If(_credit.Checked, Math.Min(_cash.Maximum, paidSoFar), 0D)
            _printBtn.Text = "Update && Print  (F10)"
            _saveBtn.Text = "Update  (F9)"
            UpdateTotals()
        End Sub

        Public Sub ResetForm()
            _saleId = 0
            _heldStock.Clear()
            _cart.Clear()
            _customer.Text = ""
            _phone.Text = ""
            _date.Value = Date.Today
            _discount.Value = 0
            _cash.Value = 0
            _credit.Checked = False
            _search.Text = ""
            UpdateTotals()
            _search.Focus()
        End Sub

        Public Sub FocusSearch()
            _search.Focus()
        End Sub

        Private Sub Save(print As Boolean)
            Ui.CommitGrid(_cartGrid)
            If _cart.Count = 0 Then
                Ui.Warn("The cart is empty. Add at least one product.")
                Return
            End If
            Dim id As Long
            Dim lines = _cart.Select(Function(c) New SaleLine With {.ProductId = c.ProductId, .Quantity = c.Qty, .SalePrice = c.Price}).ToList()
            Dim paid = If(_credit.Checked, _cash.Value, -1D)
            If _credit.Checked AndAlso _customer.Text.Trim() = "" AndAlso _phone.Text.Trim() = "" Then
                Ui.Warn("For a credit (udhaar) sale, please enter the customer's name or phone.")
                _customer.Focus()
                Return
            End If
            If Not Ui.Attempt(Sub() id = SalesService.SaveSale(_saleId, _date.Value.Date, _customer.Text, _phone.Text, _discount.Value, lines, paid)) Then Return
            If print Then PrintInvoice(id, FindForm())
            RaiseEvent Saved(id)
        End Sub

        ''' <summary>Prints (or previews, per settings) the invoice of a sale.</summary>
        Public Shared Sub PrintInvoice(saleId As Long, owner As IWin32Window)
            Ui.Attempt(Sub()
                           Dim doc = Documents.SaleInvoice(saleId)
                           If AppSettings.GetValue("direct_print") = "1" Then
                               DocPrinter.PrintNow(doc, owner)
                           Else
                               DocPrinter.Preview(doc, owner)
                           End If
                       End Sub)
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.F9 : Save(False) : Return True
                Case Keys.F10 : Save(True) : Return True
                Case Keys.Control Or Keys.F : _search.Focus() : _search.SelectAll() : Return True
            End Select
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

    End Class

    Public Class SalePage
        Inherits PageBase

        Private ReadOnly _editor As New SaleEditor()
        Private ReadOnly _last As Label

        Public Sub New()
            MyBase.New("New Sale", "F9 save  •  F10 save & print  •  Ctrl+F search  •  Enter adds scanned barcode")
            _last = Ui.Lbl("", 9.5F, FontStyle.Regular, Theme.Success)
            Toolbar.Controls.Add(_last)
            Body.Controls.Add(_editor)
            AddHandler _editor.Saved, Sub(id)
                                          _last.Text = "Saved: sale #" & Fmt.DocNo("", id) & " saved at " & Date.Now.ToString("hh:mm tt", Fmt.Inv)
                                          _editor.ResetForm()
                                          _editor.ReloadLookups()
                                      End Sub
        End Sub

        Public Overrides Sub RefreshData()
            _editor.ReloadLookups()
            _editor.FocusSearch()
        End Sub

    End Class

    ''' <summary>Edits an existing sale in a window.</summary>
    Public Class SaleEditForm
        Inherits Form

        Public Sub New(saleId As Long)
            Text = "Edit Sale #" & Fmt.DocNo("", saleId)
            Font = Theme.BaseFont
            Size = New Size(1200, 760)
            StartPosition = FormStartPosition.CenterParent
            BackColor = Theme.Background
            Padding = New Padding(12)
            Dim editor As New SaleEditor()
            Controls.Add(editor)
            editor.ReloadLookups()
            editor.LoadSale(saleId)
            AddHandler editor.Saved, Sub()
                                         DialogResult = DialogResult.OK
                                         Close()
                                     End Sub
        End Sub

    End Class

End Namespace
