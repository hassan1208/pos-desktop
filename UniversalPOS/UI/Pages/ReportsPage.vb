Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Printing
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class ReportsPage
        Inherits PageBase

        Private ReadOnly _range As New RangeBar()
        Private ReadOnly _pl As DataGridView = Ui.Grid()
        Private ReadOnly _byCategory As DataGridView = Ui.Grid()
        Private ReadOnly _top As DataGridView = Ui.Grid()
        Private ReadOnly _daily As DataGridView = Ui.Grid()
        Private ReadOnly _expenses As DataGridView = Ui.Grid()
        Private ReadOnly _monthly As DataGridView = Ui.Grid()
        Private ReadOnly _yearly As DataGridView = Ui.Grid()
        Private ReadOnly _tabs As New TabControl With {.Dock = DockStyle.Fill}
        Private _report As RangeReport

        Public Sub New()
            MyBase.New("Reports", "Profit & loss and sales analysis")
            Toolbar.Controls.Add(_range)
            Toolbar.Controls.Add(Ui.Btn("Print Report", BtnKind.Primary, AddressOf PrintReport))
            Toolbar.Controls.Add(Ui.Btn("Print Current Tab", BtnKind.Secondary, AddressOf PrintTab))
            Toolbar.Controls.Add(Ui.Btn("Export Current Tab", BtnKind.Secondary, Sub() Ui.ExportCsv(CurrentGrid(), "report")))

            Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2}
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 66))
            Dim plCard = CardPanel("Profit & Loss")
            plCard.Controls.Add(_pl)
            _pl.BringToFront()
            _pl.ColumnHeadersVisible = False
            plCard.Margin = New Padding(0, 0, 8, 0)
            AddTab("Sales by Category", _byCategory)
            AddTab("Top Products", _top)
            AddTab("Daily Sales", _daily)
            AddTab("Expenses", _expenses)
            AddTab("Monthly (all time)", _monthly)
            AddTab("Yearly (all time)", _yearly)
            _tabs.Margin = New Padding(8, 0, 0, 0)
            layout.Controls.Add(plCard, 0, 0)
            layout.Controls.Add(_tabs, 1, 0)
            Body.Controls.Add(layout)
            AddHandler _range.RangeChanged, Sub() RefreshData()
            Dim boldFont = Theme.Fnt(9.75F, FontStyle.Bold)
            Dim bigFont = Theme.Fnt(11, FontStyle.Bold)
            Ui.OnFormat(_pl, Sub(r, col, st)
                                 Dim item = GetStr(r, "item")
                                 If item = "NET PROFIT" Then
                                     st.Font = bigFont
                                     st.ForeColor = If(_report IsNot Nothing AndAlso _report.NetProfit < 0, Theme.Danger, Theme.Success)
                                 ElseIf item.StartsWith("Gross profit") Then
                                     st.Font = boldFont
                                 ElseIf item.StartsWith("  ") Then
                                     st.ForeColor = Theme.Muted
                                 End If
                             End Sub)
            ' Highlight the best month by profit.
            Ui.OnFormat(_monthly, Sub(r, col, st)
                                      Dim t = TryCast(_monthly.DataSource, DataTable)
                                      If t Is Nothing OrElse t.Rows.Count = 0 Then Return
                                      Dim best = t.AsEnumerable().Max(Function(x) GetDec(x, "profit"))
                                      If GetDec(r, "profit") = best Then st.BackColor = Color.FromArgb(220, 252, 231)
                                  End Sub)
        End Sub

        Private Sub AddTab(title As String, g As DataGridView)
            Dim t As New TabPage(title) With {.BackColor = Theme.Card}
            t.Controls.Add(g)
            _tabs.TabPages.Add(t)
        End Sub

        Private Function CurrentGrid() As DataGridView
            Return DirectCast(_tabs.SelectedTab.Controls(0), DataGridView)
        End Function

        Public Overrides Sub RefreshData()
            _report = ReportService.Range(_range.FromDate, _range.ToDate)
            Dim r = _report
            Dim pl As New DataTable()
            pl.Columns.Add("item", GetType(String))
            pl.Columns.Add("amount", GetType(String))
            pl.Rows.Add("Sales (" & r.SalesCount & ")", Fmt.Money(r.Revenue))
            pl.Rows.Add("  Cost of goods sold", "- " & Fmt.Money(r.Cogs))
            pl.Rows.Add("  (Discounts given)", Fmt.Money(r.Discounts))
            pl.Rows.Add("Gross profit on sales", Fmt.Money(r.GrossProfit))
            pl.Rows.Add("Repair jobs (" & r.MaintenanceCount & ") profit", Fmt.Money(r.MaintenanceProfit))
            pl.Rows.Add("  charged " & Fmt.Money(r.MaintenanceCharge) & ", parts " & Fmt.Money(r.MaintenanceCost), "")
            pl.Rows.Add("Expenses (" & r.ExpenseCount & ")", "- " & Fmt.Money(r.ExpenseTotal))
            pl.Rows.Add("NET PROFIT", Fmt.Money(r.NetProfit))
            pl.Rows.Add("", "")
            pl.Rows.Add("Stock purchased (" & r.PurchaseCount & " bills)", Fmt.Money(r.PurchaseTotal))
            Ui.ShowTable(_pl, pl, "item=Item", "amount=Amount")
            If _pl.Columns.Contains("amount") Then _pl.Columns("amount").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight

            Ui.ShowTable(_byCategory, r.CategoryBreakdown, "category=Category", "units=Units", "revenue=Revenue", "profit=Profit", "margin_pct=Margin %")
            Ui.ShowTable(_top, r.TopProducts, "product=Product", "units=Units", "revenue=Revenue", "profit=Profit")
            Ui.ShowTable(_daily, r.DailyTrend, "day=Date", "sales=Sales", "revenue=Revenue", "profit=Profit")
            Ui.ShowTable(_expenses, r.ExpenseBreakdown, "category=Category", "entries=Entries", "total=Total")
            Ui.ShowTable(_monthly, ReportService.Monthly(), "month=Month", "sales=Sales", "revenue=Revenue", "profit=Profit")
            Ui.ShowTable(_yearly, ReportService.Yearly(), "year=Year", "sales=Sales", "revenue=Revenue", "profit=Profit")
        End Sub

        Private Sub PrintReport()
            If _report Is Nothing Then Return
            Dim doc As New PrintDoc With {.Title = "PROFIT & LOSS REPORT", .DocDate = Date.Today.ToString("dd MMM yyyy", Fmt.Inv), .Format = "a4", .Footer = " "}
            doc.PartyLines.Add("Period: " & _range.Description)
            doc.Columns.Add(New PrintColumn("Item", 5))
            doc.Columns.Add(New PrintColumn("Amount", 2, StringAlignment.Far))
            For Each row As DataGridViewRow In _pl.Rows
                doc.Rows.Add({CStr(row.Cells("item").Value), CStr(row.Cells("amount").Value)})
            Next
            doc.Rows.Add({"", ""})
            doc.Rows.Add({"SALES BY CATEGORY", ""})
            For Each r As DataRow In _report.CategoryBreakdown.Rows
                doc.Rows.Add({"  " & GetStr(r, "category") & "  (" & GetLng(r, "units") & " units, profit " & Fmt.Num(GetDec(r, "profit")) & ")", Fmt.Money(GetDec(r, "revenue"))})
            Next
            doc.Rows.Add({"", ""})
            doc.Rows.Add({"EXPENSES BY CATEGORY", ""})
            For Each r As DataRow In _report.ExpenseBreakdown.Rows
                doc.Rows.Add({"  " & GetStr(r, "category"), Fmt.Money(GetDec(r, "total"))})
            Next
            doc.AddTotal("Net Profit:", Fmt.Money(_report.NetProfit))
            DocPrinter.Preview(doc, FindForm())
        End Sub

        Private Sub PrintTab()
            DocPrinter.Preview(Documents.FromGrid(_tabs.SelectedTab.Text.ToUpperInvariant(), _range.Description, CurrentGrid()), FindForm())
        End Sub

    End Class

End Namespace
