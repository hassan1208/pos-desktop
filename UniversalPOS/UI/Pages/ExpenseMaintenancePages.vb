Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class ExpensesPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _cats As DataGridView = Ui.Grid()
        Private ReadOnly _range As New RangeBar()
        Private ReadOnly _category As ComboBox = Ui.Combo(160)
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Bold)
        Private _loading As Boolean

        Public Sub New()
            MyBase.New("Expenses", "Rent, bills, salaries and other running costs")
            Toolbar.Controls.Add(Ui.Btn("+ Add Expense", BtnKind.Primary, Sub() Edit(0)))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() Edit(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            Toolbar.Controls.Add(_range)
            Toolbar.Controls.Add(_category)
            Toolbar.Controls.Add(Ui.Btn("Print", BtnKind.Secondary, Sub() DocPrinter.Preview(Documents.FromGrid("EXPENSES", _range.Description & "   " & _summary.Text, _grid), FindForm())))
            Toolbar.Controls.Add(Ui.Btn("Export CSV", BtnKind.Secondary, Sub() Ui.ExportCsv(_grid, "expenses")))
            Toolbar.Controls.Add(_summary)

            Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2}
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 70))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
            Dim left = CardPanel()
            left.Controls.Add(_grid)
            left.Margin = New Padding(0, 0, 8, 0)
            Dim right = CardPanel("Expense categories")
            Dim catBar = Ui.Flow(False)
            catBar.Dock = DockStyle.Bottom
            catBar.Padding = New Padding(8)
            catBar.Controls.Add(Ui.Btn("+ Add", BtnKind.Primary, Sub() EditCategory(0)))
            catBar.Controls.Add(Ui.Btn("Rename", BtnKind.Secondary, Sub() EditCategory(Ui.SelectedRowId(_cats))))
            catBar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteCategory))
            right.Controls.Add(_cats)
            right.Controls.Add(catBar)
            _cats.BringToFront()
            right.Margin = New Padding(8, 0, 0, 0)
            layout.Controls.Add(left, 0, 0)
            layout.Controls.Add(right, 1, 0)
            Body.Controls.Add(layout)

            AddHandler _range.RangeChanged, Sub() LoadExpenses()
            AddHandler _category.SelectedIndexChanged, Sub()
                                                          If Not _loading Then LoadExpenses()
                                                      End Sub
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then Edit(Ui.SelectedRowId(_grid))
                                              End Sub
        End Sub

        Public Overrides Sub RefreshData()
            _loading = True
            Dim cats = ExpenseService.Categories()
            Dim keep = Ui.SelectedId(_category)
            Ui.Bind(_category, cats, "name", "id", "All categories")
            Ui.SelectId(_category, keep)
            _loading = False
            Ui.ShowTable(_cats, cats, "name=Category", "entries=Entries", "total=All-time Total")
            LoadExpenses()
        End Sub

        Private Sub LoadExpenses()
            Dim dt = ExpenseService.Expenses(_range.FromDate, _range.ToDate, Ui.SelectedId(_category))
            Ui.ShowTable(_grid, dt, "expense_date=Date", "category=Category", "amount=Amount", "notes=Notes")
            If _grid.Columns.Count > 0 Then _grid.Columns("notes").FillWeight = 200
            _summary.Text = dt.Rows.Count & " entries  •  " & Fmt.Money(dt.AsEnumerable().Sum(Function(r) GetDec(r, "amount")))
        End Sub

        Private Sub Edit(id As Long)
            If ExpenseService.Categories().Rows.Count = 0 Then
                Ui.Warn("Add an expense category first (right side).")
                Return
            End If
            Using dlg As New ExpenseDialog(id)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub DeleteSelected()
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 OrElse Not Ui.Confirm("Delete this expense?") Then Return
            If Ui.Attempt(Sub() ExpenseService.DeleteExpense(id)) Then RefreshData()
        End Sub

        Private Sub EditCategory(id As Long)
            Dim current = If(id > 0, GetStr(Ui.SelectedRow(_cats), "name"), "")
            Dim name = Microsoft.VisualBasic.Interaction.InputBox("Category name:", If(id > 0, "Rename Category", "Add Category"), current)
            If name.Trim() = "" Then Return
            If Ui.Attempt(Sub() ExpenseService.SaveCategory(id, name)) Then RefreshData()
        End Sub

        Private Sub DeleteCategory()
            Dim id = Ui.SelectedRowId(_cats)
            If id = 0 OrElse Not Ui.Confirm("Delete this category?") Then Return
            If Ui.Attempt(Sub() ExpenseService.DeleteCategory(id)) Then RefreshData()
        End Sub

    End Class

    Public Class ExpenseDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _category As ComboBox = Ui.Combo(240)
        Private ReadOnly _amount As NumericUpDown = Ui.NumBox(2, 160)
        Private ReadOnly _date As DateTimePicker = Ui.DatePick()
        Private ReadOnly _notes As TextBox = Ui.Txt(300)

        Public Sub New(id As Long)
            MyBase.New(If(id > 0, "Edit Expense", "Add Expense"), 460)
            _id = id
            Ui.Bind(_category, ExpenseService.Categories(), "name")
            AddField("Category", _category, True)
            AddField("Amount", _amount, True)
            AddField("Date", _date)
            AddField("Notes", _notes)
            If id > 0 Then
                Dim r = Data.Db.QueryRow("SELECT * FROM expenses WHERE id = @p0", id)
                Ui.SelectId(_category, GetLng(r, "category_id"))
                _amount.Value = Math.Min(_amount.Maximum, GetDec(r, "amount"))
                _date.Value = Fmt.ParseDbDate(GetStr(r, "expense_date"))
                _notes.Text = GetStr(r, "notes")
            End If
        End Sub

        Protected Overrides Sub OnSave()
            If Ui.Attempt(Sub() ExpenseService.SaveExpense(_id, Ui.SelectedId(_category), _amount.Value, _date.Value.Date, _notes.Text)) Then Done()
        End Sub

    End Class

    Public Class MaintenancePage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _range As New RangeBar()
        Private ReadOnly _search As TextBox = Ui.Txt(180, "Item / customer / phone")
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Bold)

        Public Sub New()
            MyBase.New("Maintenance / Repairs", "Repair jobs: parts cost, customer charge and profit")
            Toolbar.Controls.Add(Ui.Btn("+ New Job", BtnKind.Primary, Sub() Edit(0)))
            Toolbar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() Edit(Ui.SelectedRowId(_grid))))
            Toolbar.Controls.Add(Ui.Btn("Receipt", BtnKind.Secondary, AddressOf PrintReceipt))
            If Session.IsAdmin Then Toolbar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteSelected))
            Toolbar.Controls.Add(_range)
            Toolbar.Controls.Add(_search)
            Toolbar.Controls.Add(Ui.Btn("Print List", BtnKind.Secondary, Sub() DocPrinter.Preview(Documents.FromGrid("MAINTENANCE JOBS", _range.Description & "   " & _summary.Text, _grid), FindForm())))
            Toolbar.Controls.Add(Ui.Btn("Export CSV", BtnKind.Secondary, Sub() Ui.ExportCsv(_grid, "maintenance")))
            Toolbar.Controls.Add(_summary)
            AddGridCard(_grid)
            AddHandler _range.RangeChanged, Sub() RefreshData()
            AddHandler _search.TextChanged, Sub() RefreshData()
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then Edit(Ui.SelectedRowId(_grid))
                                              End Sub
        End Sub

        Public Overrides Sub RefreshData()
            Dim dt = MaintenanceService.Jobs(_range.FromDate, _range.ToDate, _search.Text)
            If Session.IsAdmin Then
                Ui.ShowTable(_grid, dt, "id=Job #", "maintenance_date=Date", "customer_name=Customer", "customer_phone=Phone", "item_description=Item", "issue_notes=Issue", "parts_cost=Parts Cost", "customer_charge=Charged", "profit=Profit")
            Else
                Ui.ShowTable(_grid, dt, "id=Job #", "maintenance_date=Date", "customer_name=Customer", "customer_phone=Phone", "item_description=Item", "issue_notes=Issue", "customer_charge=Charged")
            End If
            Dim charged = dt.AsEnumerable().Sum(Function(r) GetDec(r, "customer_charge"))
            Dim profit = dt.AsEnumerable().Sum(Function(r) GetDec(r, "profit"))
            _summary.Text = dt.Rows.Count & " jobs  •  charged " & Fmt.Money(charged) & If(Session.IsAdmin, "  •  profit " & Fmt.Money(profit), "")
        End Sub

        Private Sub Edit(id As Long)
            Using dlg As New MaintenanceDialog(id)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub PrintReceipt()
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 Then Return
            Ui.Attempt(Sub()
                           Dim j = MaintenanceService.GetJob(id)
                           Dim doc As New PrintDoc With {.Title = "REPAIR RECEIPT", .DocNumber = Fmt.DocNo("M-", id), .DocDate = Fmt.ShowDate(GetStr(j, "maintenance_date"))}
                           doc.PartyLines.Add("Customer: " & If(GetStr(j, "customer_name") = "", "-", GetStr(j, "customer_name")))
                           If GetStr(j, "customer_phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(j, "customer_phone"))
                           doc.Columns.Add(New PrintColumn("Item", 4))
                           doc.Columns.Add(New PrintColumn("Issue / Work", 4))
                           doc.Rows.Add({GetStr(j, "item_description"), GetStr(j, "issue_notes")})
                           doc.AddTotal("Total Charges:", Fmt.Money(GetDec(j, "customer_charge")))
                           DocPrinter.Preview(doc, FindForm())
                       End Sub)
        End Sub

        Private Sub DeleteSelected()
            Dim id = Ui.SelectedRowId(_grid)
            If id = 0 OrElse Not Ui.Confirm("Delete this job?") Then Return
            If Ui.Attempt(Sub() MaintenanceService.DeleteJob(id)) Then RefreshData()
        End Sub

    End Class

    Public Class MaintenanceDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _date As DateTimePicker = Ui.DatePick()
        Private ReadOnly _customer As TextBox = Ui.Txt(260)
        Private ReadOnly _phone As TextBox = Ui.Txt(160)
        Private ReadOnly _item As TextBox = Ui.Txt(320, "e.g. Samsung A52 / HP Laptop")
        Private ReadOnly _issue As TextBox = Ui.Txt(320, "e.g. Screen broken, not charging")
        Private ReadOnly _charge As NumericUpDown = Ui.NumBox(2, 160)
        Private ReadOnly _parts As New DataTable()
        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _totals As Label = Ui.Lbl("", 10.5F, FontStyle.Bold, Theme.Primary)

        Public Sub New(id As Long)
            MyBase.New(If(id > 0, "Edit Repair Job", "New Repair Job"), 640, 620)
            _id = id
            AddField("Date", _date)
            AddField("Customer", _customer)
            AddField("Phone", _phone)
            AddField("Item", _item, True)
            AddField("Issue / notes", _issue)
            AddField("Charge to customer", _charge)

            _parts.Columns.Add("Description", GetType(String))
            _parts.Columns.Add("Amount", GetType(Decimal))
            _parts.Columns("Amount").DefaultValue = 0D
            _grid.ReadOnly = False
            _grid.AllowUserToAddRows = True
            _grid.AllowUserToDeleteRows = True
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect
            _grid.DataSource = _parts
            AddHandler _grid.DataBindingComplete, Sub()
                                                      If _grid.Columns.Contains("Amount") Then
                                                          _grid.Columns("Amount").DefaultCellStyle.Format = "#,##0.00"
                                                          _grid.Columns("Amount").FillWeight = 40
                                                      End If
                                                  End Sub
            AddHandler _grid.DataError, Sub(s, e) e.ThrowException = False
            AddHandler _parts.ColumnChanged, Sub() UpdateTotals()
            AddHandler _grid.CellEndEdit, Sub() UpdateTotals()
            AddHandler _parts.RowDeleted, Sub() UpdateTotals()
            AddHandler _charge.ValueChanged, Sub() UpdateTotals()
            _totals.Dock = DockStyle.Bottom
            _totals.AutoSize = False
            _totals.Height = 30
            _totals.TextAlign = ContentAlignment.MiddleRight
            Dim card = PageBase.CardPanel("Parts / costs (what the shop spent)")
            card.Controls.Add(_grid)
            card.Controls.Add(_totals)
            _grid.BringToFront()
            Content.Controls.Add(card)

            If id > 0 Then
                Dim j = MaintenanceService.GetJob(id)
                _date.Value = Fmt.ParseDbDate(GetStr(j, "maintenance_date"))
                _customer.Text = GetStr(j, "customer_name")
                _phone.Text = GetStr(j, "customer_phone")
                _item.Text = GetStr(j, "item_description")
                _issue.Text = GetStr(j, "issue_notes")
                _charge.Value = Math.Min(_charge.Maximum, GetDec(j, "customer_charge"))
                For Each r As DataRow In MaintenanceService.GetParts(id).Rows
                    _parts.Rows.Add(GetStr(r, "description"), GetDec(r, "amount"))
                Next
            End If
            UpdateTotals()
        End Sub

        Private Function PartsCost() As Decimal
            Dim t = 0D
            For Each drv As DataRowView In _parts.DefaultView
                If drv("Amount") IsNot DBNull.Value Then t += CDec(drv("Amount"))
            Next
            Return t
        End Function

        Private Sub UpdateTotals()
            Dim cost = PartsCost()
            _totals.Text = "Parts: " & Fmt.Money(cost) & "    Profit: " & Fmt.Money(_charge.Value - cost) & "   "
        End Sub

        Protected Overrides Sub OnSave()
            Ui.CommitGrid(_grid)
            Dim parts As New List(Of PartLine)
            For Each r As DataRow In _parts.Rows
                If r.RowState = DataRowState.Deleted Then Continue For
                parts.Add(New PartLine With {.Description = GetStr(r, "Description"), .Amount = GetDec(r, "Amount")})
            Next
            If Ui.Attempt(Sub() MaintenanceService.SaveJob(_id, _date.Value.Date, _customer.Text, _phone.Text, _item.Text, _issue.Text, _charge.Value, parts)) Then Done()
        End Sub

    End Class

End Namespace
