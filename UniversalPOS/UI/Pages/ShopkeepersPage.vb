Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class ShopkeepersPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _search As TextBox = Ui.Txt(200, "Search name / phone")
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Bold)

        Public Sub New()
            MyBase.New("Shopkeepers Ledger", "Nearby shops you trade with: products / cash given and received")
            Toolbar.Controls.Add(Ui.Btn("+ Add Shopkeeper", BtnKind.Primary, Sub() EditContact(0)))
            Toolbar.Controls.Add(Ui.Btn("+ New Entry", BtnKind.Success, Sub() NewEntry(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Open Ledger", BtnKind.Secondary, AddressOf OpenLedger))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() EditContact(Ui.SelectedRowId(_grid))))
            If Session.IsAdmin Then Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            Toolbar.Controls.Add(_search)
            Toolbar.Controls.Add(_summary)
            AddGridCard(_grid)
            AddHandler _search.TextChanged, Sub() RefreshData()
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then OpenLedger()
                                              End Sub
            Ui.OnFormat(_grid, Sub(r, col, st)
                                   If col <> "status" Then Return
                                   Dim b = GetDec(r, "balance")
                                   st.ForeColor = If(b > 0, Theme.Success, If(b < 0, Theme.Danger, Theme.Muted))
                               End Sub)
        End Sub

        Public Overrides Sub RefreshData()
            Dim dt = ShopkeeperService.Contacts(_search.Text)
            dt.Columns.Add("status", GetType(String))
            For Each r As DataRow In dt.Rows
                Dim b = GetDec(r, "balance")
                r("status") = If(b > 0, "They owe you", If(b < 0, "You owe them", "Settled"))
            Next
            Ui.ShowTable(_grid, dt, "name=Name", "phone=Phone", "total_given=Given", "total_received=Received", "balance=Balance", "status=Status")
            Dim recv = dt.AsEnumerable().Where(Function(r) GetDec(r, "balance") > 0).Sum(Function(r) GetDec(r, "balance"))
            Dim pay = -dt.AsEnumerable().Where(Function(r) GetDec(r, "balance") < 0).Sum(Function(r) GetDec(r, "balance"))
            _summary.Text = "Receivable " & Fmt.Money(recv) & "   •   Payable " & Fmt.Money(pay)
        End Sub

        Private Sub EditContact(id As Long)
            Using dlg As New ContactDialog("Shopkeeper", id, "shopkeeper_contacts")
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub NewEntry(contactId As Long)
            If contactId = 0 Then
                Ui.Warn("Select a shopkeeper first.")
                Return
            End If
            Using dlg As New ShopkeeperEntryDialog(contactId)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub OpenLedger()
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 Then Return
            Using f As New ShopkeeperLedgerForm(id)
                f.ShowDialog(FindForm())
            End Using
            RefreshData()
        End Sub

        Private Sub DeleteSelected()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            If Not Ui.Confirm("Delete """ & GetStr(r, "name") & """ and their whole ledger?") Then Return
            If Ui.Attempt(Sub() ShopkeeperService.DeleteContact(GetLng(r, "id"))) Then RefreshData()
        End Sub

    End Class

    Public Class ShopkeeperLedgerForm
        Inherits Form

        Private ReadOnly _contactId As Long
        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _balance As Label = Ui.Lbl("", 11, FontStyle.Bold)

        Public Sub New(contactId As Long)
            _contactId = contactId
            Dim c = ShopkeeperService.GetContact(contactId)
            Text = "Ledger - " & GetStr(c, "name")
            Font = Theme.BaseFont
            Size = New Size(1100, 680)
            StartPosition = FormStartPosition.CenterParent
            BackColor = Theme.Background
            Padding = New Padding(14)

            Dim bar = Ui.Flow()
            bar.Dock = DockStyle.Top
            bar.Padding = New Padding(0, 0, 0, 10)
            bar.Controls.Add(Ui.Btn("+ New Entry", BtnKind.Success, Sub()
                                                                      Using d As New ShopkeeperEntryDialog(_contactId)
                                                                          If d.ShowDialog(Me) = DialogResult.OK Then LoadData()
                                                                      End Using
                                                                  End Sub))
            bar.Controls.Add(Ui.Btn("Return Product", BtnKind.Warning, AddressOf ReturnSelected))
            If Session.IsAdmin Then bar.Controls.Add(Ui.Btn("Delete Entry", BtnKind.Danger, AddressOf DeleteSelected))
            bar.Controls.Add(Ui.Btn("Print Statement", BtnKind.Secondary, Sub() Ui.Attempt(Sub() DocPrinter.Preview(Documents.ShopkeeperStatement(_contactId), Me))))
            bar.Controls.Add(_balance)

            Dim card = PageBase.CardPanel()
            card.Controls.Add(_grid)
            Controls.Add(card)
            Controls.Add(bar)
            Ui.OnFormat(_grid, Sub(r, col, st)
                                   If col = "type" Then st.ForeColor = If(GetStr(r, "direction") = "given", Theme.Primary, Theme.Success)
                                   If GetLng(r, "returned") = 1 Then st.ForeColor = Theme.Muted
                               End Sub)
            LoadData()
        End Sub

        Private Sub LoadData()
            Dim dt = ShopkeeperService.Ledger(_contactId)
            dt.Columns.Add("type", GetType(String))
            For Each r As DataRow In dt.Rows
                r("type") = If(GetStr(r, "direction") = "given", "Given", "Received") & " " & GetStr(r, "entry_type") & If(GetLng(r, "returned") = 1, " (returned)", "")
            Next
            Ui.ShowTable(_grid, dt, "transaction_date=Date", "type=Type", "description=Description", "quantity=Qty", "given=Given", "received=Received", "balance=Balance", "notes=Notes")
            If _grid.Columns.Contains("description") Then _grid.Columns("description").FillWeight = 180
            Dim bal = ShopkeeperService.Balance(_contactId)
            _balance.Text = "   " & If(bal > 0, "They owe you " & Fmt.Money(bal), If(bal < 0, "You owe them " & Fmt.Money(-bal), "Settled"))
            _balance.ForeColor = If(bal > 0, Theme.Success, If(bal < 0, Theme.Danger, Theme.Muted))
        End Sub

        Private Sub ReturnSelected()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            If GetStr(r, "entry_type") <> "product" OrElse GetLng(r, "returned") = 1 Then
                Ui.Warn("Only product entries that were not already returned can be returned.")
                Return
            End If
            If Not Ui.Confirm("Record a return of """ & GetStr(r, "description") & """ (qty " & GetLng(r, "quantity") & ")?") Then Return
            If Ui.Attempt(Sub() ShopkeeperService.ReturnEntry(GetLng(r, "id"))) Then LoadData()
        End Sub

        Private Sub DeleteSelected()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            If Not Ui.Confirm("Delete this ledger line? (Stock and sales are NOT changed - use Return Product for that.)") Then Return
            If Ui.Attempt(Sub() ShopkeeperService.DeleteEntry(GetLng(r, "id"))) Then LoadData()
        End Sub

    End Class

    Public Class ShopkeeperEntryDialog
        Inherits DialogBase

        Private ReadOnly _contactId As Long
        Private ReadOnly _direction As ComboBox = Ui.Combo(320)
        Private ReadOnly _type As ComboBox = Ui.Combo(180)
        Private ReadOnly _date As DateTimePicker = Ui.DatePick()
        ' cash
        Private ReadOnly _desc As TextBox = Ui.Txt(300)
        Private ReadOnly _cash As NumericUpDown = Ui.NumBox(2, 150)
        ' product
        Private ReadOnly _category As ComboBox = Ui.Combo(240)
        Private ReadOnly _product As ComboBox = Ui.SearchCombo(300)
        Private ReadOnly _newName As TextBox = Ui.Txt(300, "Only if receiving a product you don't stock yet")
        Private ReadOnly _qty As NumericUpDown = Ui.NumBox(0, 90, 1000000)
        Private ReadOnly _price As NumericUpDown = Ui.NumBox(2, 150)
        Private ReadOnly _settle As ComboBox = Ui.Combo(200)
        Private ReadOnly _settled As NumericUpDown = Ui.NumBox(2, 150)
        Private ReadOnly _notes As TextBox = Ui.Txt(300)
        Private ReadOnly _info As Label = Ui.Lbl("", 9, FontStyle.Italic, Theme.Muted)
        Private _cashRows As New List(Of Control)
        Private _productRows As New List(Of Control)

        Public Sub New(contactId As Long)
            MyBase.New("New Entry - " & GetStr(ShopkeeperService.GetContact(contactId), "name"), 560)
            _contactId = contactId
            _direction.Items.AddRange({"I GAVE them (they owe me more)", "I RECEIVED from them (I owe them more)"})
            _direction.SelectedIndex = 0
            _type.Items.AddRange({"Product", "Cash"})
            _type.SelectedIndex = 0
            _settle.Items.AddRange({"No payment now (credit)", "Partly settled now", "Fully settled now"})
            _settle.SelectedIndex = 0
            _qty.Minimum = 1

            AddField("Direction", _direction)
            AddField("Entry type", _type)
            AddField("Date", _date)
            _cashRows.Add(AddField("Description", _desc, True))
            _cashRows.Add(AddField("Amount", _cash, True))
            _productRows.Add(AddField("Category", _category, True))
            _productRows.Add(AddField("Product", _product))
            _productRows.Add(AddField("or new product", _newName))
            _productRows.Add(AddField("Quantity", _qty))
            _productRows.Add(AddField("Unit price", _price))
            AddField("Settlement", _settle)
            AddField("Settled amount", _settled)
            AddField("Notes", _notes)
            AddField("", _info)

            Ui.Bind(_category, InventoryService.Categories(), "name")
            AddHandler _category.SelectedIndexChanged, Sub() LoadProducts()
            AddHandler _direction.SelectedIndexChanged, Sub() UpdateMode()
            AddHandler _type.SelectedIndexChanged, Sub() UpdateMode()
            AddHandler _settle.SelectedIndexChanged, Sub() UpdateMode()
            AddHandler _product.SelectedIndexChanged, Sub()
                                                          Dim drv = TryCast(_product.SelectedItem, DataRowView)
                                                          If drv IsNot Nothing Then
                                                              _price.Value = Math.Min(_price.Maximum, CDec(drv("sale_price")))
                                                              _info.Text = "In stock: " & CLng(drv("stock_quantity"))
                                                          End If
                                                      End Sub
            LoadProducts()
            UpdateMode()
            ' Binding auto-selects the first product once the combo is created; start empty instead.
            AddHandler Shown, Sub() LoadProducts()
        End Sub

        Private ReadOnly Property Giving As Boolean
            Get
                Return _direction.SelectedIndex = 0
            End Get
        End Property

        Private Sub LoadProducts()
            Ui.Bind(_product, InventoryService.ProductPicker(Ui.SelectedId(_category)), "product_name")
            _product.SelectedIndex = -1
            _product.Text = ""
            _price.Value = 0
            _info.Text = ""
        End Sub

        Private Sub UpdateMode()
            Dim isCash = _type.SelectedIndex = 1
            For Each c In _cashRows
                c.Enabled = isCash
            Next
            For Each c In _productRows
                c.Enabled = Not isCash
            Next
            _newName.Enabled = Not isCash AndAlso Not Giving
            _settled.Enabled = _settle.SelectedIndex = 1
        End Sub

        Protected Overrides Sub OnSave()
            Dim productId As Long = 0
            Dim drv = TryCast(_product.SelectedItem, DataRowView)
            If drv IsNot Nothing AndAlso String.Equals(CStr(drv("product_name")), _product.Text, StringComparison.OrdinalIgnoreCase) Then productId = CLng(drv("id"))
            Dim e As New ShopkeeperEntry With {
                .ContactId = _contactId,
                .Direction = If(Giving, "given", "received"),
                .EntryType = If(_type.SelectedIndex = 1, "cash", "product"),
                .TransactionDate = _date.Value.Date,
                .Notes = _notes.Text,
                .Description = _desc.Text,
                .CashAmount = _cash.Value,
                .CategoryId = Ui.SelectedId(_category),
                .ProductId = productId,
                .NewProductName = If(productId = 0, _newName.Text, ""),
                .Quantity = CInt(_qty.Value),
                .UnitPrice = _price.Value,
                .SettlementType = {"credit", "partial", "full"}(Math.Max(0, _settle.SelectedIndex)),
                .SettledAmount = _settled.Value
            }
            If Ui.Attempt(Sub() ShopkeeperService.AddEntry(e)) Then Done()
        End Sub

    End Class

End Namespace
