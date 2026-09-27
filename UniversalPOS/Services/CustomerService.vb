Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    ''' <summary>
    ''' Customer credit (udhaar). A customer is identified by the name + phone written on their
    ''' sales; each sale keeps its own paid amount, and payments are applied to the oldest unpaid
    ''' sales first.
    ''' </summary>
    Public Module CustomerService

        ' Same customer = same phone, or (when no phone) same name ignoring case/spaces.
        Private Const KeyExpr As String = "CASE WHEN IFNULL(TRIM(customer_phone),'') <> '' THEN 'p:' || TRIM(customer_phone) ELSE 'n:' || LOWER(TRIM(IFNULL(customer_name,''))) END"

        Public Function CustomerKey(name As String, phone As String) As String
            If Not String.IsNullOrWhiteSpace(phone) Then Return "p:" & phone.Trim()
            Return "n:" & If(name, "").Trim().ToLowerInvariant()
        End Function

        ''' <summary>Customers with their totals. Balance = what they still owe.</summary>
        Public Function Customers(Optional search As String = "", Optional withBalanceOnly As Boolean = True) As DataTable
            Return Db.Query("SELECT " & KeyExpr & " AS ckey, MAX(customer_name) AS name, MAX(customer_phone) AS phone, COUNT(*) AS sales, " &
                            "COALESCE(SUM(total_amount),0) AS total, COALESCE(SUM(paid_amount),0) AS paid, " &
                            "COALESCE(SUM(total_amount - paid_amount),0) AS balance, MAX(sale_date) AS last_sale_date " &
                            "FROM sales WHERE (IFNULL(TRIM(customer_name),'') <> '' OR IFNULL(TRIM(customer_phone),'') <> '') " &
                            "AND (@p0 = '' OR IFNULL(customer_name,'') LIKE '%' || @p0 || '%' OR IFNULL(customer_phone,'') LIKE '%' || @p0 || '%') " &
                            "GROUP BY ckey HAVING (@p1 = 0 OR balance > 0.004) ORDER BY balance DESC, name COLLATE NOCASE",
                            If(search, "").Trim(), withBalanceOnly)
        End Function

        Public Function TotalOutstanding() As Decimal
            Return Db.ScalarDec("SELECT COALESCE(SUM(total_amount - paid_amount),0) FROM sales WHERE total_amount - paid_amount > 0.004")
        End Function

        Public Function CustomerSales(ckey As String) As DataTable
            Return Db.Query("SELECT id, sale_date, total_amount, paid_amount, (total_amount - paid_amount) AS balance FROM sales WHERE " & KeyExpr & " = @p0 ORDER BY sale_date DESC, id DESC", ckey)
        End Function

        Public Function CustomerPayments(ckey As String) As DataTable
            Return Db.Query("SELECT p.id, p.payment_date, p.amount, p.sale_id, p.notes FROM sale_payments p JOIN sales s ON s.id = p.sale_id " &
                            "WHERE " & KeyExpr.Replace("customer_", "s.customer_") & " = @p0 ORDER BY p.payment_date DESC, p.id DESC", ckey)
        End Function

        ''' <summary>Records a payment from a customer against their oldest unpaid sales. Returns the amount applied.</summary>
        Public Function ReceivePayment(ckey As String, amount As Decimal, paymentDate As Date, notes As String) As Decimal
            If amount <= 0 Then Throw New BusinessException("Please enter a valid amount.")
            Return Db.Tx(Function(s)
                             Dim remaining = Fmt.Round2(amount)
                             For Each r As DataRow In s.Query("SELECT id, total_amount - paid_amount AS due FROM sales WHERE " & KeyExpr & " = @p0 AND total_amount - paid_amount > 0.004 ORDER BY sale_date, id", ckey).Rows
                                 If remaining <= 0 Then Exit For
                                 Dim pay = Math.Min(Fmt.Round2(GetDec(r, "due")), remaining)
                                 s.Exec("UPDATE sales SET paid_amount = paid_amount + @p0 WHERE id = @p1", pay, GetLng(r, "id"))
                                 s.Exec("INSERT INTO sale_payments (sale_id, amount, payment_date, notes) VALUES (@p0,@p1,@p2,@p3)", GetLng(r, "id"), pay, paymentDate, NullIfEmpty(notes))
                                 remaining -= pay
                             Next
                             If remaining = Fmt.Round2(amount) Then Throw New BusinessException("This customer has nothing outstanding.")
                             Return Fmt.Round2(amount) - remaining
                         End Function)
        End Function

        ''' <summary>Removes a payment and puts its amount back on the sale's balance.</summary>
        Public Sub DeletePayment(paymentId As Long)
            Db.Tx(Sub(s)
                      Dim p = s.QueryRow("SELECT sale_id, amount FROM sale_payments WHERE id = @p0", paymentId)
                      If p Is Nothing Then Return
                      s.Exec("UPDATE sales SET paid_amount = MAX(0, paid_amount - @p0) WHERE id = @p1", GetDec(p, "amount"), GetLng(p, "sale_id"))
                      s.Exec("DELETE FROM sale_payments WHERE id = @p0", paymentId)
                  End Sub)
        End Sub

        ''' <summary>Chronological statement: sales as debit, payments (upfront + later) as credit.</summary>
        Public Function Statement(ckey As String) As DataTable
            Dim dt As New DataTable()
            dt.Columns.Add("date", GetType(String))
            dt.Columns.Add("description", GetType(String))
            dt.Columns.Add("debit", GetType(Decimal))
            dt.Columns.Add("credit", GetType(Decimal))
            dt.Columns.Add("balance", GetType(Decimal))
            Dim later = "(SELECT COALESCE(SUM(amount),0) FROM sale_payments sp WHERE sp.sale_id = s.id)"
            Dim rows = Db.Query(
                "SELECT s.sale_date AS d, 0 AS k, s.id, 'Invoice #' || printf('%06d', s.id) AS descr, s.total_amount AS debit, s.paid_amount - " & later & " AS credit FROM sales s WHERE " & KeyExpr.Replace("customer_", "s.customer_") & " = @p0 " &
                "UNION ALL SELECT p.payment_date, 1, p.id, 'Payment received' || IFNULL(' - ' || p.notes, ''), 0, p.amount FROM sale_payments p JOIN sales s ON s.id = p.sale_id WHERE " & KeyExpr.Replace("customer_", "s.customer_") & " = @p0 " &
                "ORDER BY d, k, id", ckey)
            Dim bal = 0D
            For Each r As DataRow In rows.Rows
                bal += GetDec(r, "debit") - GetDec(r, "credit")
                dt.Rows.Add(GetStr(r, "d"), GetStr(r, "descr"), GetDec(r, "debit"), GetDec(r, "credit"), bal)
            Next
            Return dt
        End Function

    End Module

End Namespace
