Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class VendorsPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _search As TextBox = Ui.Txt(200, "Search vendor")
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Bold)

        Public Sub New()
            MyBase.New("Vendors & Purchases", "Suppliers, purchase bills and what you owe them")
            Toolbar.Controls.Add(Ui.Btn("+ Add Vendor", BtnKind.Primary, Sub() EditVendor(0)))
            Toolbar.Controls.Add(Ui.Btn("+ New Purchase Bill", BtnKind.Success, Sub() NewPurchase(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Open Ledger", BtnKind.Secondary, AddressOf OpenLedger))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() EditVendor(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            Toolbar.Controls.Add(_search)
            Toolbar.Controls.Add(_summary)
            AddGridCard(_grid)
            AddHandler _search.TextChanged, Sub() RefreshData()
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then OpenLedger()
                                              End Sub
            Ui.OnFormat(_grid, AddressOf DueInRed)
        End Sub

        ' Shows an unpaid balance in red.
        Public Shared Sub DueInRed(r As DataRow, col As String, st As DataGridViewCellStyle)
            If col = "balance_due" AndAlso GetDec(r, "balance_due") > 0 Then st.ForeColor = Theme.Danger
        End Sub

        Public Overrides Sub RefreshData()
            Dim dt = PurchaseService.Vendors(_search.Text)
            Ui.ShowTable(_grid, dt, "name=Vendor", "phone=Phone", "address=Address", "bills=Bills", "total_purchased=Purchased", "total_paid=Paid", "balance_due=Balance Due")
            _summary.Text = "Total payable: " & Fmt.Money(dt.AsEnumerable().Sum(Function(r) GetDec(r, "balance_due")))
        End Sub

        Private Sub EditVendor(id As Long)
            Using dlg As New ContactDialog("Vendor", id, "vendors")
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub NewPurchase(vendorId As Long)
            If PurchaseService.VendorList().Rows.Count = 0 Then
                Ui.Warn("Add a vendor first.")
                Return
            End If
            Using dlg As New PurchaseDialog(vendorId)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub OpenLedger()
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 Then Return
            Using f As New VendorLedgerForm(id)
                f.ShowDialog(FindForm())
            End Using
            RefreshData()
        End Sub

        Private Sub DeleteSelected()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            If Not Ui.Confirm("Delete vendor """ & GetStr(r, "name") & """?") Then Return
            If Ui.Attempt(Sub() PurchaseService.DeleteVendor(GetLng(r, "id"))) Then RefreshData()
        End Sub

    End Class

    ''' <summary>Add/edit dialog for a vendor or a shopkeeper contact (same fields).</summary>
    Public Class ContactDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _table As String
        Private ReadOnly _name As TextBox = Ui.Txt(300)
        Private ReadOnly _phone As TextBox = Ui.Txt(200)
        Private ReadOnly _address As TextBox = Ui.Txt(300)
        Public Property SavedId As Long

        Public Sub New(kind As String, id As Long, table As String)
            MyBase.New(If(id > 0, "Edit ", "Add ") & kind, 480)
            _id = id
            _table = table
            AddField("Name", _name, True)
            AddField("Phone", _phone)
            AddField("Address", _address)
            If id > 0 Then
                Dim r = Data.Db.QueryRow("SELECT name, phone, address FROM " & table & " WHERE id = @p0", id)
                _name.Text = GetStr(r, "name")
                _phone.Text = GetStr(r, "phone")
                _address.Text = GetStr(r, "address")
            End If
        End Sub

        Protected Overrides Sub OnSave()
            If Ui.Attempt(Sub()
                              If _table = "vendors" Then
                                  SavedId = PurchaseService.SaveVendor(_id, _name.Text, _phone.Text, _address.Text)
                              Else
                                  SavedId = ShopkeeperService.SaveContact(_id, _name.Text, _phone.Text, _address.Text)
                              End If
                          End Sub) Then Done()
        End Sub

    End Class

    ''' <summary>A purchase bill with several products (restocks existing ones, creates new ones).</summary>
    Public Class PurchaseDialog
        Inherits DialogBase

        Private ReadOnly _vendor As ComboBox = Ui.Combo(260)
        Private ReadOnly _billNo As TextBox = Ui.Txt(160)
        Private ReadOnly _date As DateTimePicker = Ui.DatePick()
        Private ReadOnly _payType As ComboBox = Ui.Combo(140)
        Private ReadOnly _paid As NumericUpDown = Ui.NumBox()
        Private ReadOnly _notes As TextBox = Ui.Txt(320)
        Private ReadOnly _imageLbl As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Text = "No image", .Margin = New Padding(0, 8, 0, 0)}
        Private _imageFile As String = ""
        Private ReadOnly _lines As New DataTable()
        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _total As Label = Ui.Lbl("", 12, FontStyle.Bold, Theme.Primary)
        Private ReadOnly _categories As DataTable

        Public Sub New(vendorId As Long)
            MyBase.New("New Purchase Bill", 900, 680)
            Ui.Bind(_vendor, PurchaseService.VendorList(), "name")
            If vendorId > 0 Then Ui.SelectId(_vendor, vendorId)
            _payType.Items.AddRange({"credit", "partial", "full"})
            _payType.SelectedIndex = 0
            Dim payRow = Ui.Flow(False)
            payRow.Controls.AddRange({_payType, Ui.Lbl("Paid now"), _paid})
            Dim imgRow = Ui.Flow(False)
            imgRow.Controls.AddRange({Ui.Btn("Attach bill photo...", BtnKind.Secondary, AddressOf PickImage), _imageLbl})
            AddField("Vendor", _vendor, True)
            AddField("Bill number", _billNo)
            AddField("Date", _date)
            AddField("Payment", payRow)
            AddField("Notes", _notes)
            AddField("Bill image", imgRow)
            AddHandler _payType.SelectedIndexChanged, Sub() _paid.Enabled = CStr(_payType.SelectedItem) = "partial"
            _paid.Enabled = False

            _categories = InventoryService.Categories()
            _lines.Columns.Add("category_id", GetType(Long))
            _lines.Columns.Add("product_name", GetType(String))
            _lines.Columns.Add("quantity", GetType(Integer))
            _lines.Columns.Add("purchase_price", GetType(Decimal))
            _lines.Columns.Add("sale_price", GetType(Decimal))
            _lines.Columns.Add("line_total", GetType(Decimal), "ISNULL(quantity,0) * ISNULL(purchase_price,0)")
            _lines.Columns("quantity").DefaultValue = 1
            _lines.Columns("purchase_price").DefaultValue = 0D
            _lines.Columns("sale_price").DefaultValue = 0D
            If _categories.Rows.Count > 0 Then _lines.Columns("category_id").DefaultValue = GetLng(_categories.Rows(0), "id")

            _grid.ReadOnly = False
            _grid.AllowUserToAddRows = True
            _grid.AllowUserToDeleteRows = True
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect
            _grid.AutoGenerateColumns = False
            Dim catCol As New DataGridViewComboBoxColumn With {.DataPropertyName = "category_id", .HeaderText = "Category", .DataSource = _categories, .DisplayMember = "name", .ValueMember = "id", .FillWeight = 110, .FlatStyle = FlatStyle.Flat}
            _grid.Columns.Add(catCol)
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "product_name", .HeaderText = "Product name (existing name = restock)", .FillWeight = 200})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "quantity", .HeaderText = "Qty", .FillWeight = 50})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "purchase_price", .HeaderText = "Cost", .FillWeight = 70, .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "#,##0.00", .Alignment = DataGridViewContentAlignment.MiddleRight}})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "sale_price", .HeaderText = "Sale Price", .FillWeight = 70, .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "#,##0.00", .Alignment = DataGridViewContentAlignment.MiddleRight}})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "line_total", .HeaderText = "Total", .ReadOnly = True, .FillWeight = 80, .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "#,##0.00", .Alignment = DataGridViewContentAlignment.MiddleRight}})
            _grid.DataSource = _lines
            AddHandler _grid.DataError, Sub(s, e) e.ThrowException = False
            AddHandler _grid.CellEndEdit, Sub() UpdateTotal()
            AddHandler _grid.RowValidated, Sub() UpdateTotal()
            AddHandler _lines.ColumnChanged, Sub() UpdateTotal()
            AddHandler _lines.RowDeleted, Sub() UpdateTotal()

            ' Product name suggestions from the catalog.
            Dim names As New AutoCompleteStringCollection()
            For Each r As DataRow In Data.Db.Query("SELECT DISTINCT product_name FROM products ORDER BY product_name").Rows
                names.Add(GetStr(r, "product_name"))
            Next
            AddHandler _grid.EditingControlShowing, Sub(s, e)
                                                        Dim tb = TryCast(e.Control, TextBox)
                                                        If tb Is Nothing Then Return
                                                        If _grid.CurrentCell.OwningColumn.DataPropertyName = "product_name" Then
                                                            tb.AutoCompleteCustomSource = names
                                                            tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend
                                                            tb.AutoCompleteSource = AutoCompleteSource.CustomSource
                                                        Else
                                                            tb.AutoCompleteMode = AutoCompleteMode.None
                                                        End If
                                                    End Sub

            _total.Dock = DockStyle.Bottom
            _total.AutoSize = False
            _total.Height = 32
            _total.TextAlign = ContentAlignment.MiddleRight
            Dim card = PageBase.CardPanel("Products on this bill (type in the empty last row to add; Delete key removes a row)")
            card.Controls.Add(_grid)
            card.Controls.Add(_total)
            _grid.BringToFront()
            Content.Controls.Add(card)
            UpdateTotal()
        End Sub

        Private Sub PickImage()
            Using dlg As New OpenFileDialog With {.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
                If dlg.ShowDialog() = DialogResult.OK Then
                    _imageFile = dlg.FileName
                    _imageLbl.Text = IO.Path.GetFileName(dlg.FileName)
                End If
            End Using
        End Sub

        Private Sub UpdateTotal()
            _total.Text = "Bill total:  " & Fmt.Money(Ui.SumProduct(_lines, "quantity", "purchase_price")) & "   "
        End Sub

        Protected Overrides Sub OnSave()
            Ui.CommitGrid(_grid)
            Dim lines As New List(Of PurchaseLine)
            For Each r As DataRow In _lines.Rows
                If r.RowState = DataRowState.Deleted Then Continue For
                lines.Add(New PurchaseLine With {.CategoryId = GetLng(r, "category_id"), .ProductName = GetStr(r, "product_name"), .Quantity = GetInt(r, "quantity"),
                                                 .PurchasePrice = GetDec(r, "purchase_price"), .SalePrice = GetDec(r, "sale_price")})
            Next
            If Ui.Attempt(Sub() PurchaseService.SavePurchase(Ui.SelectedId(_vendor), _billNo.Text, _date.Value.Date, CStr(_payType.SelectedItem), _paid.Value, _notes.Text, _imageFile, lines)) Then
                Done()
            End If
        End Sub

    End Class

    ''' <summary>Vendor ledger: bills, payments and the running statement.</summary>
    Public Class VendorLedgerForm
        Inherits Form

        Private ReadOnly _vendorId As Long
        Private ReadOnly _bills As DataGridView = Ui.Grid()
        Private ReadOnly _payments As DataGridView = Ui.Grid()
        Private ReadOnly _statement As DataGridView = Ui.Grid()
        Private ReadOnly _header As Label = Ui.Lbl("", 11, FontStyle.Bold)

        Public Sub New(vendorId As Long)
            _vendorId = vendorId
            Dim v = Data.Db.QueryRow("SELECT name, phone, address FROM vendors WHERE id = @p0", vendorId)
            Text = "Vendor Ledger - " & GetStr(v, "name")
            Font = Theme.BaseFont
            Size = New Size(1100, 720)
            StartPosition = FormStartPosition.CenterParent
            BackColor = Theme.Background
            Padding = New Padding(14)

            Dim bar = Ui.Flow()
            bar.Dock = DockStyle.Top
            bar.Padding = New Padding(0, 0, 0, 10)
            bar.Controls.Add(Ui.Btn("+ New Purchase Bill", BtnKind.Success, Sub()
                                                                              Using d As New PurchaseDialog(_vendorId)
                                                                                  If d.ShowDialog(Me) = DialogResult.OK Then LoadData()
                                                                              End Using
                                                                          End Sub))
            bar.Controls.Add(Ui.Btn("Pay Selected Bill", BtnKind.Primary, AddressOf PaySelected))
            bar.Controls.Add(Ui.Btn("Pay Vendor (oldest bills first)", BtnKind.Secondary, AddressOf PayVendor))
            bar.Controls.Add(Ui.Btn("View / Print Bill", BtnKind.Secondary, Sub() WithBill(Sub(id) DocPrinter.Preview(Documents.PurchaseBill(id), Me))))
            bar.Controls.Add(Ui.Btn("Bill Photo", BtnKind.Secondary, AddressOf BillPhoto))
            bar.Controls.Add(Ui.Btn("Print Statement", BtnKind.Secondary, Sub() Ui.Attempt(Sub() DocPrinter.Preview(Documents.VendorStatement(_vendorId), Me))))
            bar.Controls.Add(_header)

            Dim tabs As New TabControl With {.Dock = DockStyle.Fill, .Font = Theme.BaseFont}
            Dim t1 As New TabPage("Purchase bills") With {.BackColor = Theme.Card}
            t1.Controls.Add(_bills)
            Dim t2 As New TabPage("Payments") With {.BackColor = Theme.Card}
            t2.Controls.Add(_payments)
            Dim t3 As New TabPage("Statement") With {.BackColor = Theme.Card}
            t3.Controls.Add(_statement)
            tabs.TabPages.AddRange({t1, t2, t3})
            Controls.Add(tabs)
            Controls.Add(bar)
            AddHandler _bills.CellDoubleClick, Sub(s, e)
                                                   If e.RowIndex >= 0 Then WithBill(Sub(id) DocPrinter.Preview(Documents.PurchaseBill(id), Me))
                                               End Sub
            Ui.OnFormat(_bills, AddressOf VendorsPage.DueInRed)
            LoadData()
        End Sub

        Private Sub LoadData()
            Ui.ShowTable(_bills, PurchaseService.VendorPurchases(_vendorId), "id=#", "bill_number=Bill #", "purchase_date=Date", "total_amount=Total", "paid_amount=Paid", "balance_due=Balance", "payment_type=Status", "notes=Notes")
            Ui.ShowTable(_payments, PurchaseService.VendorPayments(_vendorId), "payment_date=Date", "amount=Amount", "bill_number=Against Bill #", "notes=Notes")
            Ui.ShowTable(_statement, PurchaseService.VendorStatement(_vendorId), "date=Date", "description=Description", "debit=Bill Amount", "credit=Paid", "balance=Balance")
            Dim due = Data.Db.ScalarDec("SELECT COALESCE(SUM(balance_due),0) FROM purchases WHERE vendor_id = @p0", _vendorId)
            _header.Text = "   Balance payable: " & Fmt.Money(due)
            _header.ForeColor = If(due > 0, Theme.Danger, Theme.Success)
        End Sub

        Private Sub WithBill(action As Action(Of Long))
            Dim id = Ui.SelectedRowId(_bills)
            If id = 0 Then
                Ui.Warn("Select a bill in the 'Purchase bills' tab first.")
                Return
            End If
            Ui.Attempt(Sub() action(id))
        End Sub

        Private Sub PaySelected()
            WithBill(Sub(id)
                         Dim bal = Data.Db.ScalarDec("SELECT balance_due FROM purchases WHERE id = @p0", id)
                         Using d As New PaymentDialog("Pay bill (balance " & Fmt.Money(bal) & ")", bal)
                             If d.ShowDialog(Me) = DialogResult.OK Then
                                 PurchaseService.AddPayment(id, d.Amount, d.PayDate, d.Notes)
                                 LoadData()
                             End If
                         End Using
                     End Sub)
        End Sub

        Private Sub PayVendor()
            Dim due = Data.Db.ScalarDec("SELECT COALESCE(SUM(balance_due),0) FROM purchases WHERE vendor_id = @p0", _vendorId)
            Using d As New PaymentDialog("Pay vendor (total due " & Fmt.Money(due) & ")", due)
                If d.ShowDialog(Me) = DialogResult.OK Then
                    Dim applied As Decimal
                    If Ui.Attempt(Sub() applied = PurchaseService.PayVendor(_vendorId, d.Amount, d.PayDate, d.Notes)) Then
                        If applied < d.Amount Then Ui.Info("Only " & Fmt.Money(applied) & " was due, so that much was recorded.")
                        LoadData()
                    End If
                End If
            End Using
        End Sub

        Private Sub BillPhoto()
            WithBill(Sub(id)
                         Dim rel = CStr(If(Data.Db.Scalar("SELECT bill_image FROM purchases WHERE id = @p0", id), ""))
                         Dim full = If(rel = "", "", IO.Path.Combine(AppPaths.DataDir, rel))
                         If full <> "" AndAlso IO.File.Exists(full) Then
                             Process.Start(New ProcessStartInfo(full) With {.UseShellExecute = True})
                         ElseIf Ui.Confirm("This bill has no photo. Attach one now?") Then
                             Using dlg As New OpenFileDialog With {.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
                                 If dlg.ShowDialog(Me) = DialogResult.OK Then PurchaseService.SetBillImage(id, dlg.FileName)
                             End Using
                         End If
                     End Sub)
        End Sub

    End Class

    Public Class PaymentDialog
        Inherits DialogBase

        Private ReadOnly _amount As NumericUpDown = Ui.NumBox(2, 160)
        Private ReadOnly _date As DateTimePicker = Ui.DatePick()
        Private ReadOnly _notes As TextBox = Ui.Txt(260, "e.g. Cash / Bank transfer / Cheque #")

        Public ReadOnly Property Amount As Decimal
            Get
                Return _amount.Value
            End Get
        End Property

        Public ReadOnly Property PayDate As Date
            Get
                Return _date.Value.Date
            End Get
        End Property

        Public ReadOnly Property Notes As String
            Get
                Return _notes.Text
            End Get
        End Property

        Public Sub New(title As String, suggested As Decimal)
            MyBase.New(title, 460)
            AddField("Amount", _amount, True)
            AddField("Date", _date)
            AddField("Notes", _notes)
            _amount.Value = Math.Max(0D, Math.Min(_amount.Maximum, suggested))
            SaveButton.Text = "Record Payment"
        End Sub

        Protected Overrides Sub OnSave()
            If _amount.Value <= 0 Then
                Ui.Warn("Please enter an amount.")
                Return
            End If
            Done()
        End Sub

    End Class

End Namespace
