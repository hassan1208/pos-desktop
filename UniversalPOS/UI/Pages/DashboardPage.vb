Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class DashboardPage
        Inherits PageBase

        Private ReadOnly _cards As TableLayoutPanel
        Private ReadOnly _chart As BarChart
        Private ReadOnly _recent As DataGridView
        Private ReadOnly _low As DataGridView
        Private ReadOnly _navigate As Action(Of String)

        Public Sub New(navigate As Action(Of String))
            MyBase.New("Dashboard", "Today at a glance")
            _navigate = navigate

            Toolbar.Controls.Add(Ui.Btn("+ New Sale", BtnKind.Success, Sub() _navigate("sale")))
            Toolbar.Controls.Add(Ui.Btn("Add Product", BtnKind.Secondary, Sub() _navigate("products")))
            Toolbar.Controls.Add(Ui.Btn("Sales History", BtnKind.Secondary, Sub() _navigate("sales")))
            Toolbar.Controls.Add(Ui.Btn("Refresh", BtnKind.Secondary, Sub() RefreshData()))

            _cards = New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 4, .RowCount = 2, .Height = CInt(200 * Theme.Fnt(10).Size / 10), .Padding = New Padding(0)}
            For i = 0 To 3
                _cards.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
            Next
            _cards.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
            _cards.RowStyles.Add(New RowStyle(SizeType.Percent, 50))

            Dim lower As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Padding = New Padding(0, 10, 0, 0)}
            lower.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
            lower.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 35))
            lower.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))

            Dim chartCard = CardPanel("Sales - last 7 days")
            _chart = New BarChart With {.Dock = DockStyle.Fill}
            chartCard.Controls.Add(_chart)
            _chart.BringToFront()
            chartCard.Margin = New Padding(0, 0, 8, 0)

            Dim recentCard = CardPanel("Recent sales")
            _recent = Ui.Grid()
            recentCard.Controls.Add(_recent)
            _recent.BringToFront()
            recentCard.Margin = New Padding(4, 0, 4, 0)
            AddHandler _recent.CellDoubleClick, Sub()
                                                    Dim id = Ui.SelectedRowId(_recent)
                                                    If id > 0 Then Ui.Attempt(Sub() Printing.DocPrinter.Preview(Printing.Documents.SaleInvoice(id), FindForm()))
                                                End Sub

            Dim lowCard = CardPanel("Low stock")
            _low = Ui.Grid()
            lowCard.Controls.Add(_low)
            _low.BringToFront()
            lowCard.Margin = New Padding(8, 0, 0, 0)

            lower.Controls.Add(chartCard, 0, 0)
            lower.Controls.Add(recentCard, 1, 0)
            lower.Controls.Add(lowCard, 2, 0)

            Body.Controls.Add(lower)
            Body.Controls.Add(_cards)
        End Sub

        Public Overrides Sub RefreshData()
            Dim st = ReportService.Dashboard()
            _cards.SuspendLayout()
            _cards.Controls.Clear()
            AddCard(0, 0, "Today's Sales", Fmt.Money(st.TodaySales), st.TodaySalesCount & " sale(s)", Theme.Primary)
            If Session.IsAdmin Then
                AddCard(1, 0, "Today's Profit", Fmt.Money(st.TodayProfit), "Expenses today: " & Fmt.Money(st.TodayExpenses), Theme.Success)
                AddCard(2, 0, "This Month Sales", Fmt.Money(st.MonthSales), "Profit: " & Fmt.Money(st.MonthProfit), Theme.Purple)
                AddCard(3, 0, "This Month Net", Fmt.Money(st.MonthProfit + st.MonthMaintenanceProfit - st.MonthExpenses), "Expenses: " & Fmt.Money(st.MonthExpenses), Theme.InfoColor)
                AddCard(0, 1, "Products", st.ProductCount.ToString("N0"), "Stock value: " & Fmt.Money(st.StockValue), Theme.Text)
                AddCard(1, 1, "Low Stock", st.LowStockCount.ToString("N0"), "at or below " & AppSettings.LowStockThreshold & " units", If(st.LowStockCount > 0, Theme.Danger, Theme.Success))
                AddCard(2, 1, "Payable to Vendors", Fmt.Money(st.VendorPayable), "Unpaid purchase bills", Theme.Warning)
                AddCard(3, 1, "To Receive", Fmt.Money(st.CustomerCredit + st.ShopkeeperReceivable), "Udhaar " & Fmt.Money(st.CustomerCredit) & "  |  Shops " & Fmt.Money(st.ShopkeeperReceivable), Theme.InfoColor)
            Else
                AddCard(1, 0, "Products", st.ProductCount.ToString("N0"), "in catalog", Theme.Text)
                AddCard(2, 0, "Low Stock", st.LowStockCount.ToString("N0"), "at or below " & AppSettings.LowStockThreshold & " units", If(st.LowStockCount > 0, Theme.Danger, Theme.Success))
                AddCard(3, 0, "Signed in", Session.UserName, Date.Today.ToString("dd MMM yyyy"), Theme.Purple)
                _cards.RowStyles(1).Height = 0
            End If
            _cards.ResumeLayout()

            _chart.SetData(ReportService.Last7Days())
            Ui.ShowTable(_recent, ReportService.RecentSales(12), "id=#", "sale_date=Date", "customer=Customer", "total_amount=Amount")
            If _recent.Columns.Count > 0 Then _recent.Columns("id").FillWeight = 30
            Ui.ShowTable(_low, ReportService.LowStock(20), "product_name=Product", "stock_quantity=Stock")
        End Sub

        Private Sub AddCard(col As Integer, row As Integer, title As String, value As String, note As String, accent As Color)
            Dim p As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.Card, .Margin = New Padding(If(col = 0, 0, 5), 0, If(col = 3, 0, 5), 10)}
            AddHandler p.Paint, Sub(s, e)
                                    Using b As New SolidBrush(accent)
                                        e.Graphics.FillRectangle(b, 0, 0, 4, p.Height)
                                    End Using
                                    Using pen As New Pen(Theme.Border)
                                        e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1)
                                    End Using
                                End Sub
            AddHandler p.Resize, Sub() p.Invalidate()
            Dim t = Ui.Lbl(title, 9, FontStyle.Regular, Theme.Muted)
            t.Location = New Point(16, 10)
            Dim v = Ui.Lbl(value, 15, FontStyle.Bold, accent)
            v.Location = New Point(14, 10 + t.PreferredHeight + 2)
            Dim n = Ui.Lbl(note, 8.5F, FontStyle.Regular, Theme.Muted)
            n.Location = New Point(16, v.Top + v.PreferredHeight + 2)
            p.Controls.AddRange({t, v, n})
            _cards.Controls.Add(p, col, row)
        End Sub

    End Class

    ''' <summary>Tiny custom-drawn bar chart (no chart library needed).</summary>
    Public Class BarChart
        Inherits Control

        Private _labels As New List(Of String)
        Private _values As New List(Of Decimal)

        Public Sub New()
            DoubleBuffered = True
            BackColor = Theme.Card
            ResizeRedraw = True
        End Sub

        Public Sub SetData(dt As DataTable)
            _labels = New List(Of String)
            _values = New List(Of Decimal)
            For Each r As DataRow In dt.Rows
                _labels.Add(CDate(r("day")).ToString("ddd dd", Fmt.Inv))
                _values.Add(CDec(r("revenue")))
            Next
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            If _values.Count = 0 Then Return
            Dim g = e.Graphics
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim padL = 12, padR = 12, padT = 24, padB = 28
            Dim w = Width - padL - padR
            Dim h = Height - padT - padB
            If w <= 10 OrElse h <= 10 Then Return
            Dim max = Math.Max(1D, _values.Max())
            Dim slot = w / _values.Count
            Dim barW = CSng(Math.Min(46, slot * 0.6))
            Using fSmall As New Font("Segoe UI", 8), bar As New SolidBrush(Theme.Primary), today As New SolidBrush(Theme.Success), txt As New SolidBrush(Theme.Muted), grid As New Pen(Theme.Border)
                g.DrawLine(grid, padL, padT + h, padL + w, padT + h)
                For i = 0 To _values.Count - 1
                    Dim bh = CSng(h * _values(i) / max)
                    Dim x = CSng(padL + slot * i + (slot - barW) / 2)
                    Dim y = padT + h - bh
                    g.FillRectangle(If(i = _values.Count - 1, today, bar), x, y, barW, Math.Max(1, bh))
                    Using sf As New StringFormat With {.Alignment = StringAlignment.Center}
                        g.DrawString(_labels(i), fSmall, txt, New RectangleF(CSng(padL + slot * i), padT + h + 6, CSng(slot), 16), sf)
                        If _values(i) > 0 Then
                            g.DrawString(ShortNum(_values(i)), fSmall, txt, New RectangleF(CSng(padL + slot * i), y - 16, CSng(slot), 16), sf)
                        End If
                    End Using
                Next
            End Using
        End Sub

        Private Shared Function ShortNum(v As Decimal) As String
            If v >= 1000000D Then Return (v / 1000000D).ToString("0.#", Fmt.Inv) & "M"
            If v >= 1000D Then Return (v / 1000D).ToString("0.#", Fmt.Inv) & "k"
            Return v.ToString("0", Fmt.Inv)
        End Function

    End Class

End Namespace
