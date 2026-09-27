Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    Public Class DashboardStats
        Public Property TodaySales As Decimal
        Public Property TodaySalesCount As Long
        Public Property TodayProfit As Decimal
        Public Property TodayExpenses As Decimal
        Public Property MonthSales As Decimal
        Public Property MonthProfit As Decimal
        Public Property MonthExpenses As Decimal
        Public Property MonthMaintenanceProfit As Decimal
        Public Property ProductCount As Long
        Public Property LowStockCount As Long
        Public Property StockValue As Decimal
        Public Property VendorPayable As Decimal
        Public Property ShopkeeperReceivable As Decimal
        Public Property CustomerCredit As Decimal
        Public Property ShopkeeperPayable As Decimal
    End Class

    Public Class RangeReport
        Public Property FromDate As Date
        Public Property ToDate As Date
        Public Property SalesCount As Long
        Public Property Revenue As Decimal
        Public Property GrossProfit As Decimal
        Public Property Discounts As Decimal
        Public Property PurchaseCount As Long
        Public Property PurchaseTotal As Decimal
        Public Property ExpenseCount As Long
        Public Property ExpenseTotal As Decimal
        Public Property MaintenanceCount As Long
        Public Property MaintenanceCost As Decimal
        Public Property MaintenanceCharge As Decimal
        Public ReadOnly Property Cogs As Decimal
            Get
                Return Revenue - GrossProfit
            End Get
        End Property
        Public ReadOnly Property MaintenanceProfit As Decimal
            Get
                Return MaintenanceCharge - MaintenanceCost
            End Get
        End Property
        ''' <summary>Sales profit + repair profit - expenses.</summary>
        Public ReadOnly Property NetProfit As Decimal
            Get
                Return GrossProfit + MaintenanceProfit - ExpenseTotal
            End Get
        End Property
        Public Property ExpenseBreakdown As DataTable
        Public Property CategoryBreakdown As DataTable
        Public Property TopProducts As DataTable
        Public Property DailyTrend As DataTable
    End Class

    Public Module ReportService

        Public Function Dashboard() As DashboardStats
            Dim today = Date.Today
            Dim monthStart = New Date(today.Year, today.Month, 1)
            Using s = Db.Open()
                Dim st As New DashboardStats()
                Dim r = s.QueryRow("SELECT COUNT(*) AS c, COALESCE(SUM(total_amount),0) AS t, COALESCE(SUM(total_profit),0) AS p FROM sales WHERE sale_date = @p0", today)
                st.TodaySalesCount = GetLng(r, "c") : st.TodaySales = GetDec(r, "t") : st.TodayProfit = GetDec(r, "p")
                r = s.QueryRow("SELECT COALESCE(SUM(total_amount),0) AS t, COALESCE(SUM(total_profit),0) AS p FROM sales WHERE sale_date BETWEEN @p0 AND @p1", monthStart, today)
                st.MonthSales = GetDec(r, "t") : st.MonthProfit = GetDec(r, "p")
                st.TodayExpenses = s.ScalarDec("SELECT COALESCE(SUM(amount),0) FROM expenses WHERE expense_date = @p0", today)
                st.MonthExpenses = s.ScalarDec("SELECT COALESCE(SUM(amount),0) FROM expenses WHERE expense_date BETWEEN @p0 AND @p1", monthStart, today)
                st.MonthMaintenanceProfit = s.ScalarDec("SELECT COALESCE(SUM(customer_charge - total_amount),0) FROM maintenance_jobs WHERE maintenance_date BETWEEN @p0 AND @p1", monthStart, today)
                st.ProductCount = s.ScalarLong("SELECT COUNT(*) FROM products")
                st.LowStockCount = s.ScalarLong("SELECT COUNT(*) FROM products WHERE stock_quantity <= @p0", AppSettings.LowStockThreshold)
                st.StockValue = s.ScalarDec("SELECT COALESCE(SUM(stock_quantity * purchase_price),0) FROM products WHERE stock_quantity > 0")
                st.CustomerCredit = s.ScalarDec("SELECT COALESCE(SUM(total_amount - paid_amount),0) FROM sales WHERE total_amount - paid_amount > 0.004")
                st.VendorPayable = s.ScalarDec("SELECT COALESCE(SUM(balance_due),0) FROM purchases")
                Dim b = s.Query("SELECT COALESCE(SUM(CASE WHEN direction='given' THEN amount ELSE -amount END),0) AS bal FROM shopkeeper_transactions GROUP BY contact_id")
                For Each row As DataRow In b.Rows
                    Dim v = GetDec(row, "bal")
                    If v > 0 Then st.ShopkeeperReceivable += v Else st.ShopkeeperPayable += -v
                Next
                Return st
            End Using
        End Function

        Public Function RecentSales(Optional limit As Integer = 8) As DataTable
            Return Db.Query("SELECT id, sale_date, IFNULL(customer_name,'Walk-in') AS customer, total_amount FROM sales ORDER BY id DESC LIMIT @p0", limit)
        End Function

        Public Function LowStock(Optional limit As Integer = 8) As DataTable
            Return Db.Query("SELECT product_name, stock_quantity FROM products WHERE stock_quantity <= @p0 ORDER BY stock_quantity, product_name LIMIT @p1", AppSettings.LowStockThreshold, limit)
        End Function

        Public Function Last7Days() As DataTable
            Dim dt As New DataTable()
            dt.Columns.Add("day", GetType(Date))
            dt.Columns.Add("revenue", GetType(Decimal))
            Dim data = Db.Query("SELECT sale_date, COALESCE(SUM(total_amount),0) AS revenue FROM sales WHERE sale_date >= @p0 GROUP BY sale_date", Date.Today.AddDays(-6))
            For i = 6 To 0 Step -1
                Dim d = Date.Today.AddDays(-i)
                Dim key = Fmt.DbDate(d)
                Dim match = data.Select("sale_date = '" & key & "'")
                dt.Rows.Add(d, If(match.Length > 0, GetDec(match(0), "revenue"), 0D))
            Next
            Return dt
        End Function

        Public Function Range(fromDate As Date, toDate As Date) As RangeReport
            Dim rep As New RangeReport With {.FromDate = fromDate, .ToDate = toDate}
            Using s = Db.Open()
                Dim r = s.QueryRow("SELECT COUNT(*) AS c, COALESCE(SUM(total_amount),0) AS t, COALESCE(SUM(total_profit),0) AS p, COALESCE(SUM(discount),0) AS d FROM sales WHERE sale_date BETWEEN @p0 AND @p1", fromDate, toDate)
                rep.SalesCount = GetLng(r, "c") : rep.Revenue = GetDec(r, "t") : rep.GrossProfit = GetDec(r, "p") : rep.Discounts = GetDec(r, "d")
                r = s.QueryRow("SELECT COUNT(*) AS c, COALESCE(SUM(total_amount),0) AS t FROM purchases WHERE purchase_date BETWEEN @p0 AND @p1", fromDate, toDate)
                rep.PurchaseCount = GetLng(r, "c") : rep.PurchaseTotal = GetDec(r, "t")
                r = s.QueryRow("SELECT COUNT(*) AS c, COALESCE(SUM(amount),0) AS t FROM expenses WHERE expense_date BETWEEN @p0 AND @p1", fromDate, toDate)
                rep.ExpenseCount = GetLng(r, "c") : rep.ExpenseTotal = GetDec(r, "t")
                r = s.QueryRow("SELECT COUNT(*) AS c, COALESCE(SUM(total_amount),0) AS cost, COALESCE(SUM(customer_charge),0) AS charge FROM maintenance_jobs WHERE maintenance_date BETWEEN @p0 AND @p1", fromDate, toDate)
                rep.MaintenanceCount = GetLng(r, "c") : rep.MaintenanceCost = GetDec(r, "cost") : rep.MaintenanceCharge = GetDec(r, "charge")

                rep.ExpenseBreakdown = s.Query("SELECT ec.name AS category, COUNT(*) AS entries, COALESCE(SUM(e.amount),0) AS total FROM expenses e JOIN expense_categories ec ON ec.id = e.category_id " &
                                               "WHERE e.expense_date BETWEEN @p0 AND @p1 GROUP BY ec.id ORDER BY total DESC", fromDate, toDate)
                rep.CategoryBreakdown = s.Query("SELECT IFNULL(c.name,'(deleted)') AS category, SUM(si.quantity) AS units, COALESCE(SUM(si.line_total),0) AS revenue, COALESCE(SUM(si.profit),0) AS profit, " &
                                                "CASE WHEN SUM(si.line_total) > 0 THEN ROUND(SUM(si.profit) * 100.0 / SUM(si.line_total), 1) ELSE 0 END AS margin_pct " &
                                                "FROM sale_items si JOIN sales s ON s.id = si.sale_id LEFT JOIN categories c ON c.id = si.category_id " &
                                                "WHERE s.sale_date BETWEEN @p0 AND @p1 GROUP BY si.category_id ORDER BY revenue DESC", fromDate, toDate)
                rep.TopProducts = s.Query("SELECT si.product_name AS product, SUM(si.quantity) AS units, COALESCE(SUM(si.line_total),0) AS revenue, COALESCE(SUM(si.profit),0) AS profit " &
                                          "FROM sale_items si JOIN sales s ON s.id = si.sale_id WHERE s.sale_date BETWEEN @p0 AND @p1 " &
                                          "GROUP BY si.product_name ORDER BY revenue DESC LIMIT 15", fromDate, toDate)
                rep.DailyTrend = s.Query("SELECT sale_date AS day, COUNT(*) AS sales, COALESCE(SUM(total_amount),0) AS revenue, COALESCE(SUM(total_profit),0) AS profit FROM sales " &
                                         "WHERE sale_date BETWEEN @p0 AND @p1 GROUP BY sale_date ORDER BY sale_date", fromDate, toDate)
            End Using
            Return rep
        End Function

        Public Function Monthly() As DataTable
            Return Db.Query("SELECT substr(sale_date,1,7) AS month, COUNT(*) AS sales, COALESCE(SUM(total_amount),0) AS revenue, COALESCE(SUM(total_profit),0) AS profit FROM sales GROUP BY substr(sale_date,1,7) ORDER BY month DESC")
        End Function

        Public Function Yearly() As DataTable
            Return Db.Query("SELECT substr(sale_date,1,4) AS year, COUNT(*) AS sales, COALESCE(SUM(total_amount),0) AS revenue, COALESCE(SUM(total_profit),0) AS profit FROM sales GROUP BY substr(sale_date,1,4) ORDER BY year DESC")
        End Function

        Public Function SalesList(fromDate As Date, toDate As Date, Optional search As String = "") As DataTable
            Return Db.Query("SELECT s.id, s.sale_date, IFNULL(s.customer_name,'Walk-in') AS customer, s.customer_phone, " &
                            "(SELECT SUM(quantity) FROM sale_items i WHERE i.sale_id = s.id) AS items, s.discount, s.total_amount, (s.total_amount - s.paid_amount) AS due, s.total_profit, u.username AS user " &
                            "FROM sales s LEFT JOIN users u ON u.id = s.user_id WHERE s.sale_date BETWEEN @p0 AND @p1 " &
                            "AND (@p2 = '' OR IFNULL(s.customer_name,'') LIKE '%' || @p2 || '%' OR IFNULL(s.customer_phone,'') LIKE '%' || @p2 || '%' OR CAST(s.id AS TEXT) = @p2 " &
                            "OR EXISTS (SELECT 1 FROM sale_items i WHERE i.sale_id = s.id AND i.product_name LIKE '%' || @p2 || '%')) " &
                            "ORDER BY s.sale_date DESC, s.id DESC", fromDate, toDate, If(search, "").Trim())
        End Function

    End Module

End Namespace
