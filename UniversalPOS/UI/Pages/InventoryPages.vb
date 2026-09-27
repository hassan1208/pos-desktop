Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class ProductsPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _search As TextBox = Ui.Txt(240, "Search name / barcode / category")
        Private ReadOnly _category As ComboBox = Ui.Combo(180)
        Private ReadOnly _lowOnly As New CheckBox With {.Text = "Low stock only", .AutoSize = True, .Margin = New Padding(0, 6, 12, 0)}
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Regular, Theme.Muted)
        Private _loading As Boolean

        Public Sub New()
            MyBase.New("Products", "Your catalog, prices and stock")
            Toolbar.Controls.Add(Ui.Btn("+ Add Product", BtnKind.Primary, Sub() Edit(0)))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() Edit(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Adjust Stock", BtnKind.Secondary, AddressOf AdjustStock))
            If Session.IsAdmin Then Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            Toolbar.Controls.Add(_search)
            Toolbar.Controls.Add(_category)
            Toolbar.Controls.Add(_lowOnly)
            Toolbar.Controls.Add(Ui.Btn("Print", BtnKind.Secondary, Sub() DocPrinter.Preview(Documents.FromGrid("PRODUCT LIST", _summary.Text, _grid), FindForm())))
            Toolbar.Controls.Add(Ui.Btn("Export CSV", BtnKind.Secondary, Sub() Ui.ExportCsv(_grid, "products")))
            Toolbar.Controls.Add(_summary)
            AddGridCard(_grid)
            AddHandler _search.TextChanged, Sub() LoadGrid()
            AddHandler _category.SelectedIndexChanged, Sub()
                                                          If Not _loading Then LoadGrid()
                                                      End Sub
            AddHandler _lowOnly.CheckedChanged, Sub() LoadGrid()
            Ui.OnFormat(_grid, Sub(r, col, st)
                                   If col = "stock_quantity" AndAlso GetLng(r, "stock_quantity") <= AppSettings.LowStockThreshold Then st.ForeColor = Theme.Danger
                               End Sub)
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then Edit(Ui.SelectedRowId(_grid))
                                              End Sub
        End Sub

        Public Overrides Sub RefreshData()
            _loading = True
            Dim keep = Ui.SelectedId(_category)
            Ui.Bind(_category, InventoryService.Categories(), "name", "id", "All categories")
            Ui.SelectId(_category, keep)
            _loading = False
            LoadGrid()
        End Sub

        Private Sub LoadGrid()
            Dim dt = InventoryService.Products(Ui.SelectedId(_category), _search.Text, _lowOnly.Checked)
            If Session.IsAdmin Then
                Ui.ShowTable(_grid, dt, "product_name=Product", "barcode=Barcode", "category=Category", "vendor=Vendor", "purchase_price=Cost", "sale_price=Sale Price", "profit_percent=Profit %", "stock_quantity=Stock", "stock_value=Stock Value")
            Else
                Ui.ShowTable(_grid, dt, "product_name=Product", "barcode=Barcode", "category=Category", "sale_price=Sale Price", "stock_quantity=Stock")
            End If
            If _grid.Columns.Contains("product_name") Then _grid.Columns("product_name").FillWeight = 200
            Dim units = dt.AsEnumerable().Sum(Function(r) GetLng(r, "stock_quantity"))
            Dim value = dt.AsEnumerable().Sum(Function(r) GetDec(r, "stock_value"))
            _summary.Text = dt.Rows.Count & " products  •  " & units.ToString("N0") & " units" & If(Session.IsAdmin, "  •  value " & Fmt.Money(value), "")
        End Sub

        Private Sub Edit(id As Long)
            If id = 0 AndAlso InventoryService.Categories().Rows.Count = 0 Then
                Ui.Warn("Please create a category first (Categories screen).")
                Return
            End If
            Using dlg As New ProductDialog(id, Ui.SelectedId(_category))
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then LoadGrid()
            End Using
        End Sub

        Private Sub AdjustStock()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            Dim input = Microsoft.VisualBasic.Interaction.InputBox(
                "Adjust stock for " & GetStr(r, "product_name") & " (current: " & GetLng(r, "stock_quantity") & ")." & Environment.NewLine &
                "Enter +5 to add or -2 to remove (damaged / lost / correction):", "Adjust Stock", "+1")
            If input.Trim() = "" Then Return
            Dim delta As Integer
            If Not Integer.TryParse(input.Trim().Replace("+", ""), delta) OrElse delta = 0 Then
                Ui.Warn("Please enter a whole number like +5 or -2.")
                Return
            End If
            If Ui.Attempt(Sub() InventoryService.AdjustStock(GetLng(r, "id"), delta)) Then LoadGrid()
        End Sub

        Private Sub DeleteSelected()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            If Not Ui.Confirm("Delete product """ & GetStr(r, "product_name") & """?" & Environment.NewLine & "Past sales keep their record.") Then Return
            If Ui.Attempt(Sub() InventoryService.DeleteProduct(GetLng(r, "id"))) Then LoadGrid()
        End Sub

    End Class

    Public Class ProductDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _category As ComboBox = Ui.Combo(260)
        Private ReadOnly _name As TextBox = Ui.Txt(300)
        Private ReadOnly _barcode As TextBox = Ui.Txt(200, "Optional - scan or type")
        Private ReadOnly _vendor As ComboBox = Ui.Combo(260)
        Private ReadOnly _cost As NumericUpDown = Ui.NumBox()
        Private ReadOnly _price As NumericUpDown = Ui.NumBox()
        Private ReadOnly _profitPct As NumericUpDown = Ui.NumBox(2, 100, 100000)
        Private ReadOnly _stock As NumericUpDown = Ui.NumBox(0, 100, 10000000)
        Private ReadOnly _customPanel As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Dock = DockStyle.Fill}
        Private ReadOnly _customBoxes As New Dictionary(Of Long, TextBox)
        Private _customFields As New List(Of CategoryFieldDef)
        ' first purchase (new product + vendor)
        Private ReadOnly _purchaseBox As New GroupBox With {.Text = "Purchase bill (new product from a vendor)", .AutoSize = True, .Dock = DockStyle.Fill, .Padding = New Padding(8)}
        Private ReadOnly _billNo As TextBox = Ui.Txt(160)
        Private ReadOnly _payType As ComboBox = Ui.Combo(140)
        Private ReadOnly _paid As NumericUpDown = Ui.NumBox()
        Private ReadOnly _billImage As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Text = "No image"}
        Private _billImageFile As String = ""
        Private _syncing As Boolean
        Private _existingValues As New Dictionary(Of Long, String)

        Public Sub New(id As Long, defaultCategory As Long)
            MyBase.New(If(id > 0, "Edit Product", "Add Product"), 660)
            _id = id
            Ui.Bind(_category, InventoryService.Categories(), "name")
            Ui.Bind(_vendor, PurchaseService.VendorList(), "name", "id", "(none)")
            _payType.Items.AddRange({"full", "partial", "credit"})
            _payType.SelectedIndex = 0

            AddField("Category", _category, True)
            AddField("Product name", _name, True)
            AddField("Barcode", _barcode)
            AddField("Vendor", _vendor)
            AddField("Purchase price (cost)", _cost)
            AddField("Profit %", _profitPct)
            AddField("Sale price", _price, True)
            AddField("Stock quantity", _stock)
            AddField("Custom fields", _customPanel)

            Dim pb As New TableLayoutPanel With {.ColumnCount = 4, .AutoSize = True, .Dock = DockStyle.Fill}
            pb.Controls.Add(Ui.Lbl("Bill #"), 0, 0) : pb.Controls.Add(_billNo, 1, 0)
            pb.Controls.Add(Ui.Lbl("Payment"), 2, 0) : pb.Controls.Add(_payType, 3, 0)
            pb.Controls.Add(Ui.Lbl("Paid amount"), 0, 1) : pb.Controls.Add(_paid, 1, 1)
            Dim pick = Ui.Btn("Attach bill image...", BtnKind.Secondary, AddressOf PickImage)
            pb.Controls.Add(pick, 2, 1) : pb.Controls.Add(_billImage, 3, 1)
            _purchaseBox.Controls.Add(pb)
            If id = 0 Then
                Fields.RowCount += 1
                Fields.Controls.Add(_purchaseBox, 0, Fields.RowCount - 1)
                Fields.SetColumnSpan(_purchaseBox, 2)
            End If

            AddHandler _category.SelectedIndexChanged, Sub() BuildCustomFields()
            AddHandler _vendor.SelectedIndexChanged, Sub() UpdatePurchaseBox()
            AddHandler _payType.SelectedIndexChanged, Sub() UpdatePurchaseBox()
            AddHandler _cost.ValueChanged, Sub() SyncFromCost()
            AddHandler _profitPct.ValueChanged, Sub() SyncFromPercent()
            AddHandler _price.ValueChanged, Sub() SyncFromPrice()

            If id > 0 Then
                Dim p = InventoryService.GetProduct(id)
                _existingValues = InventoryService.ProductFieldValues(id)
                Ui.SelectId(_category, GetLng(p, "category_id"))
                _name.Text = GetStr(p, "product_name")
                _barcode.Text = GetStr(p, "barcode")
                Ui.SelectId(_vendor, GetLng(p, "vendor_id"))
                _syncing = True
                _cost.Value = Math.Min(_cost.Maximum, GetDec(p, "purchase_price"))
                _price.Value = Math.Min(_price.Maximum, GetDec(p, "sale_price"))
                _profitPct.Value = Math.Max(0D, Math.Min(_profitPct.Maximum, InventoryService.ProfitPercent(_cost.Value, _price.Value)))
                _syncing = False
                _stock.Value = GetLng(p, "stock_quantity")
            ElseIf defaultCategory > 0 Then
                Ui.SelectId(_category, defaultCategory)
            End If
            BuildCustomFields()
            UpdatePurchaseBox()
            If Not Session.IsAdmin Then
                _cost.Enabled = False
                _profitPct.Enabled = False
            End If
        End Sub

        Private Sub BuildCustomFields()
            _customPanel.SuspendLayout()
            _customPanel.Controls.Clear()
            _customPanel.RowCount = 0
            _customBoxes.Clear()
            _customFields = InventoryService.CategoryFields(Ui.SelectedId(_category))
            If _customFields.Count = 0 Then
                _customPanel.Controls.Add(Ui.Lbl("(this category has no custom fields)", 8.75F, FontStyle.Italic, Theme.Muted))
            End If
            For Each f In _customFields
                Dim tb = Ui.Txt(200)
                Dim v As String = Nothing
                If _existingValues.TryGetValue(f.Id, v) Then tb.Text = v
                _customBoxes(f.Id) = tb
                _customPanel.RowCount += 1
                _customPanel.Controls.Add(Ui.Lbl(f.Name & If(f.IsRequired, " *", "")), 0, _customPanel.RowCount - 1)
                _customPanel.Controls.Add(tb, 1, _customPanel.RowCount - 1)
            Next
            _customPanel.ResumeLayout()
        End Sub

        Private Sub UpdatePurchaseBox()
            _purchaseBox.Enabled = Ui.SelectedId(_vendor) > 0
            _paid.Enabled = CStr(_payType.SelectedItem) = "partial"
        End Sub

        Private Sub SyncFromCost()
            If _syncing Then Return
            SyncFromPercent()
        End Sub

        Private Sub SyncFromPercent()
            If _syncing OrElse _cost.Value <= 0 Then Return
            _syncing = True
            _price.Value = Math.Min(_price.Maximum, Fmt.Round2(_cost.Value * (1 + _profitPct.Value / 100D)))
            _syncing = False
        End Sub

        Private Sub SyncFromPrice()
            If _syncing Then Return
            _syncing = True
            _profitPct.Value = Math.Max(0D, Math.Min(_profitPct.Maximum, InventoryService.ProfitPercent(_cost.Value, _price.Value)))
            _syncing = False
        End Sub

        Private Sub PickImage()
            Using dlg As New OpenFileDialog With {.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
                If dlg.ShowDialog() = DialogResult.OK Then
                    _billImageFile = dlg.FileName
                    _billImage.Text = IO.Path.GetFileName(dlg.FileName)
                End If
            End Using
        End Sub

        Protected Overrides Sub OnSave()
            Dim values = _customBoxes.ToDictionary(Function(k) k.Key, Function(k) k.Value.Text)
            Dim purchase As New InitialPurchase With {.BillNumber = _billNo.Text, .PaymentType = CStr(_payType.SelectedItem), .PaidAmount = _paid.Value, .BillImageFile = _billImageFile}
            If _price.Value < _cost.Value AndAlso Not Ui.Confirm("Sale price is lower than the cost price. Save anyway?") Then Return
            If Ui.Attempt(Sub() InventoryService.SaveProduct(_id, Ui.SelectedId(_category), Ui.SelectedId(_vendor), _name.Text, _barcode.Text,
                                                             _cost.Value, _price.Value, CInt(_stock.Value), values, purchase)) Then
                Done()
            End If
        End Sub

    End Class

    Public Class CategoriesPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()

        Public Sub New()
            MyBase.New("Categories", "Group products and define custom fields (e.g. Size, Color, IMEI)")
            Toolbar.Controls.Add(Ui.Btn("+ Add Category", BtnKind.Primary, Sub() Edit(0)))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() Edit(Ui.SelectedRowId(_grid))))
            If Session.IsAdmin Then Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            AddGridCard(_grid)
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then Edit(Ui.SelectedRowId(_grid))
                                              End Sub
        End Sub

        Public Overrides Sub RefreshData()
            Ui.ShowTable(_grid, InventoryService.Categories(), "name=Category", "products=Products", "stock=Units in Stock", "fields=Custom Fields")
        End Sub

        Private Sub Edit(id As Long)
            Using dlg As New CategoryDialog(id)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub DeleteSelected()
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then Return
            If Not Ui.Confirm("Delete category """ & GetStr(r, "name") & """?") Then Return
            If Ui.Attempt(Sub() InventoryService.DeleteCategory(GetLng(r, "id"))) Then RefreshData()
        End Sub

    End Class

    Public Class CategoryDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _name As TextBox = Ui.Txt(280)
        Private ReadOnly _fields As DataGridView = Ui.Grid()
        Private ReadOnly _table As New DataTable()

        Public Sub New(id As Long)
            MyBase.New(If(id > 0, "Edit Category", "Add Category"), 520, 460)
            _id = id
            AddField("Category name", _name, True)
            AddNote("Custom fields are asked for when adding a product in this category.")

            _table.Columns.Add("id", GetType(Long))
            _table.Columns.Add("Field name", GetType(String))
            _table.Columns.Add("Required", GetType(Boolean))
            _fields.ReadOnly = False
            _fields.AllowUserToAddRows = True
            _fields.AllowUserToDeleteRows = True
            _fields.SelectionMode = DataGridViewSelectionMode.CellSelect
            _fields.DataSource = _table
            _fields.RowHeadersVisible = True
            _fields.RowHeadersWidth = 28
            AddHandler _fields.DataBindingComplete, Sub()
                                                        If _fields.Columns.Contains("id") Then _fields.Columns("id").Visible = False
                                                        If _fields.Columns.Contains("Required") Then _fields.Columns("Required").FillWeight = 35
                                                    End Sub
            Dim card = PageBase.CardPanel("Custom fields (select a row and press Delete to remove)")
            card.Controls.Add(_fields)
            _fields.BringToFront()
            Content.Controls.Add(card)

            If id > 0 Then
                _name.Text = CStr(Data.Db.Scalar("SELECT name FROM categories WHERE id = @p0", id))
                For Each f In InventoryService.CategoryFields(id)
                    _table.Rows.Add(f.Id, f.Name, f.IsRequired)
                Next
            End If
        End Sub

        Protected Overrides Sub OnSave()
            Ui.CommitGrid(_fields)
            Dim defs As New List(Of CategoryFieldDef)
            For Each r As DataRow In _table.Rows
                If r.RowState = DataRowState.Deleted Then Continue For
                defs.Add(New CategoryFieldDef With {
                    .Id = If(r("id") Is DBNull.Value, 0L, CLng(r("id"))),
                    .Name = If(r("Field name") Is DBNull.Value, "", CStr(r("Field name"))),
                    .IsRequired = r("Required") IsNot DBNull.Value AndAlso CBool(r("Required"))})
            Next
            If Ui.Attempt(Sub() InventoryService.SaveCategory(_id, _name.Text, defs)) Then Done()
        End Sub

    End Class

End Namespace
