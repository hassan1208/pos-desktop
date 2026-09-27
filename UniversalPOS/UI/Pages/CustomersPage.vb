Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    ''' <summary>Customer credit (udhaar khata): who owes what, payments received, statements.</summary>
    Public Class CustomersPage
        Inherits PageBase

        Private ReadOnly _grid As DataGridView = Ui.Grid()
        Private ReadOnly _search As TextBox = Ui.Txt(200, "Search name / phone")
        Private ReadOnly _dueOnly As New CheckBox With {.Text = "Only customers who owe", .Checked = True, .AutoSize = True, .Margin = New Padding(0, 6, 12, 0)}
        Private ReadOnly _summary As Label = Ui.Lbl("", 9.5F, FontStyle.Bold)

        Public Sub New()
            MyBase.New("Customers (Udhaar)", "Credit sales, payments received and customer statements")
            Toolbar.Controls.Add(Ui.Btn("Receive Payment", BtnKind.Success, AddressOf ReceivePayment))
            Toolbar.Controls.Add(Ui.Btn("Open Ledger", BtnKind.Primary, AddressOf OpenLedger))
            Toolbar.Controls.Add(Ui.Btn("Print Statement", BtnKind.Secondary, Sub() WithSelected(Sub(r) DocPrinter.Preview(Documents.CustomerStatement(GetStr(r, "ckey")), FindForm()))))
            Toolbar.Controls.Add(_search)
            Toolbar.Controls.Add(_dueOnly)
            Toolbar.Controls.Add(Ui.Btn("Print List", BtnKind.Secondary, Sub() DocPrinter.Preview(Documents.FromGrid("CUSTOMER CREDIT", _summary.Text, _grid), FindForm())))
            Toolbar.Controls.Add(Ui.Btn("Export CSV", BtnKind.Secondary, Sub() Ui.ExportCsv(_grid, "customers")))
            Toolbar.Controls.Add(_summary)
            AddGridCard(_grid)
            AddHandler _search.TextChanged, Sub() RefreshData()
            AddHandler _dueOnly.CheckedChanged, Sub() RefreshData()
            AddHandler _grid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then OpenLedger()
                                              End Sub
            Ui.OnFormat(_grid, Sub(r, col, st)
                                   If col = "balance" AndAlso GetDec(r, "balance") > 0 Then st.ForeColor = Theme.Danger
                               End Sub)
        End Sub

        Public Overrides Sub RefreshData()
            Dim dt = CustomerService.Customers(_search.Text, _dueOnly.Checked)
            Ui.ShowTable(_grid, dt, "name=Customer", "phone=Phone", "sales=Sales", "total=Total Bought", "paid=Paid", "balance=Balance Due", "last_sale_date=Last Sale")
            If _grid.Columns.Contains("name") Then _grid.Columns("name").FillWeight = 160
            _summary.Text = "Total udhaar: " & Fmt.Money(CustomerService.TotalOutstanding())
        End Sub

        Private Sub WithSelected(action As Action(Of DataRow))
            Dim r = Ui.SelectedRow(_grid)
            If r Is Nothing Then
                Ui.Warn("Please select a customer first.")
                Return
            End If
            Ui.Attempt(Sub() action(r))
        End Sub

        Private Sub ReceivePayment()
            WithSelected(Sub(r)
                             If CustomerLedgerForm.AskPayment(GetStr(r, "ckey"), GetStr(r, "name"), GetDec(r, "balance"), FindForm()) Then RefreshData()
                         End Sub)
        End Sub

        Private Sub OpenLedger()
            WithSelected(Sub(r)
                             Using f As New CustomerLedgerForm(GetStr(r, "ckey"), GetStr(r, "name"), GetStr(r, "phone"))
                                 f.ShowDialog(FindForm())
                             End Using
                             RefreshData()
                         End Sub)
        End Sub

    End Class

    Public Class CustomerLedgerForm
        Inherits Form

        Private ReadOnly _key As String
        Private ReadOnly _name As String
        Private ReadOnly _sales As DataGridView = Ui.Grid()
        Private ReadOnly _payments As DataGridView = Ui.Grid()
        Private ReadOnly _statement As DataGridView = Ui.Grid()
        Private ReadOnly _header As Label = Ui.Lbl("", 11, FontStyle.Bold)

        Public Sub New(ckey As String, name As String, phone As String)
            _key = ckey
            _name = If(name = "", phone, name)
            Text = "Customer Ledger - " & _name & If(phone <> "" AndAlso name <> "", "  (" & phone & ")", "")
            Font = Theme.BaseFont
            Size = New Size(1050, 680)
            StartPosition = FormStartPosition.CenterParent
            BackColor = Theme.Background
            Padding = New Padding(14)

            Dim bar = Ui.Flow()
            bar.Dock = DockStyle.Top
            bar.Padding = New Padding(0, 0, 0, 10)
            bar.Controls.Add(Ui.Btn("Receive Payment", BtnKind.Success, Sub()
                                                                          If AskPayment(_key, _name, CurrentBalance(), Me) Then LoadData()
                                                                      End Sub))
            bar.Controls.Add(Ui.Btn("View Invoice", BtnKind.Secondary, Sub()
                                                                         Dim id = Ui.SelectedRowId(_sales)
                                                                         If id > 0 Then Ui.Attempt(Sub() DocPrinter.Preview(Documents.SaleInvoice(id), Me))
                                                                     End Sub))
            If Session.IsAdmin Then bar.Controls.Add(Ui.Btn("Delete Payment", BtnKind.Danger, AddressOf DeletePayment))
            bar.Controls.Add(Ui.Btn("Print Statement", BtnKind.Secondary, Sub() Ui.Attempt(Sub() DocPrinter.Preview(Documents.CustomerStatement(_key), Me))))
            bar.Controls.Add(_header)

            Dim tabs As New TabControl With {.Dock = DockStyle.Fill, .Font = Theme.BaseFont}
            For Each pair In {New KeyValuePair(Of String, Control)("Sales", _sales), New KeyValuePair(Of String, Control)("Payments received", _payments), New KeyValuePair(Of String, Control)("Statement", _statement)}
                Dim t As New TabPage(pair.Key) With {.BackColor = Theme.Card}
                t.Controls.Add(pair.Value)
                tabs.TabPages.Add(t)
            Next
            Controls.Add(tabs)
            Controls.Add(bar)
            Ui.OnFormat(_sales, Sub(r, col, st)
                                    If col = "balance" AndAlso GetDec(r, "balance") > 0 Then st.ForeColor = Theme.Danger
                                End Sub)
            AddHandler _sales.CellDoubleClick, Sub(s, e)
                                                   Dim id = Ui.SelectedRowId(_sales)
                                                   If e.RowIndex >= 0 AndAlso id > 0 Then Ui.Attempt(Sub() DocPrinter.Preview(Documents.SaleInvoice(id), Me))
                                               End Sub
            LoadData()
        End Sub

        Private Function CurrentBalance() As Decimal
            Dim t = TryCast(_sales.DataSource, DataTable)
            Return If(t Is Nothing, 0D, t.AsEnumerable().Sum(Function(r) GetDec(r, "balance")))
        End Function

        Private Sub LoadData()
            Ui.ShowTable(_sales, CustomerService.CustomerSales(_key), "id=Invoice #", "sale_date=Date", "total_amount=Total", "paid_amount=Paid", "balance=Balance")
            Ui.ShowTable(_payments, CustomerService.CustomerPayments(_key), "payment_date=Date", "amount=Amount", "sale_id=Against Invoice #", "notes=Notes")
            Ui.ShowTable(_statement, CustomerService.Statement(_key), "date=Date", "description=Description", "debit=Sale", "credit=Paid", "balance=Balance")
            Dim due = CurrentBalance()
            _header.Text = "   " & If(due > 0, "Balance due: " & Fmt.Money(due), "Fully paid")
            _header.ForeColor = If(due > 0, Theme.Danger, Theme.Success)
        End Sub

        Private Sub DeletePayment()
            Dim id = Ui.SelectedRowId(_payments)
            If id = 0 Then
                Ui.Warn("Select a payment in the 'Payments received' tab first.")
                Return
            End If
            If Not Ui.Confirm("Delete this payment? Its amount goes back on the customer's balance.") Then Return
            If Ui.Attempt(Sub() CustomerService.DeletePayment(id)) Then LoadData()
        End Sub

        ''' <summary>Asks for an amount and records it. Returns True when a payment was saved.</summary>
        Public Shared Function AskPayment(ckey As String, name As String, balance As Decimal, owner As IWin32Window) As Boolean
            If balance <= 0 Then
                Ui.Info(name & " has nothing outstanding.")
                Return False
            End If
            Using d As New PaymentDialog("Receive payment from " & name & " (due " & Fmt.Money(balance) & ")", balance)
                If d.ShowDialog(owner) <> DialogResult.OK Then Return False
                Dim applied As Decimal
                If Not Ui.Attempt(Sub() applied = CustomerService.ReceivePayment(ckey, d.Amount, d.PayDate, d.Notes)) Then Return False
                If applied < d.Amount Then
                    Ui.Info("Only " & Fmt.Money(applied) & " was due, so that much was recorded.")
                Else
                    Ui.Info("Payment of " & Fmt.Money(applied) & " recorded.")
                End If
                Return True
            End Using
        End Function

    End Class

End Namespace
