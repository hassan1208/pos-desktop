Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    Public Class PurchaseLine
        Public Property CategoryId As Long
        Public Property ProductName As String = ""
        Public Property Quantity As Integer
        Public Property PurchasePrice As Decimal
        Public Property SalePrice As Decimal
    End Class

    Public Module PurchaseService

        ' ---------------- Vendors ----------------

        Public Function Vendors(Optional search As String = "") As DataTable
            Return Db.Query("SELECT v.id, v.name, v.phone, v.address, " &
                            "(SELECT COUNT(*) FROM purchases p WHERE p.vendor_id = v.id) AS bills, " &
                            "(SELECT COALESCE(SUM(total_amount),0) FROM purchases p WHERE p.vendor_id = v.id) AS total_purchased, " &
                            "(SELECT COALESCE(SUM(paid_amount),0) FROM purchases p WHERE p.vendor_id = v.id) AS total_paid, " &
                            "(SELECT COALESCE(SUM(balance_due),0) FROM purchases p WHERE p.vendor_id = v.id) AS balance_due " &
                            "FROM vendors v WHERE (@p0 = '' OR v.name LIKE '%' || @p0 || '%' OR IFNULL(v.phone,'') LIKE '%' || @p0 || '%') " &
                            "ORDER BY v.name COLLATE NOCASE", If(search, "").Trim())
        End Function

        Public Function VendorList() As DataTable
            Return Db.Query("SELECT id, name FROM vendors ORDER BY name COLLATE NOCASE")
        End Function

        Public Function SaveVendor(id As Long, name As String, phone As String, address As String) As Long
            name = If(name, "").Trim()
            If name = "" Then Throw New BusinessException("Vendor name is required.")
            If id > 0 Then
                Db.Exec("UPDATE vendors SET name=@p0, phone=@p1, address=@p2 WHERE id=@p3", name, NullIfEmpty(phone), NullIfEmpty(address), id)
                Return id
            End If
            Return Db.Insert("INSERT INTO vendors (name, phone, address) VALUES (@p0,@p1,@p2)", name, NullIfEmpty(phone), NullIfEmpty(address))
        End Function

        Public Sub DeleteVendor(id As Long)
            If Db.ScalarLong("SELECT COUNT(*) FROM purchases WHERE vendor_id = @p0", id) > 0 Then
                Throw New BusinessException("This vendor has purchase bills and cannot be deleted.")
            End If
            Db.Exec("DELETE FROM vendors WHERE id = @p0", id)
        End Sub

        ' ---------------- Purchases ----------------

        Public Function ResolvePaid(paymentType As String, paidInput As Decimal, total As Decimal) As Decimal
            Select Case paymentType
                Case "full" : Return total
                Case "credit" : Return 0D
                Case Else : Return Math.Min(Math.Max(Fmt.Round2(paidInput), 0D), total)
            End Select
        End Function

        Public Function NormalizeType(paymentType As String, paid As Decimal, total As Decimal) As String
            If paymentType = "partial" Then
                If paid >= total Then Return "full"
                If paid <= 0 Then Return "credit"
            End If
            If paymentType = "full" OrElse paymentType = "credit" OrElse paymentType = "partial" Then Return paymentType
            Return "credit"
        End Function

        ''' <summary>
        ''' Saves a purchase bill. Each line restocks an existing product with the same name in the same
        ''' category, or creates a new product.
        ''' </summary>
        Public Function SavePurchase(vendorId As Long, billNumber As String, purchaseDate As Date, paymentType As String,
                                     paidInput As Decimal, notes As String, billImageFile As String,
                                     lines As IEnumerable(Of PurchaseLine)) As Long
            Dim valid = lines.Where(Function(l) l.CategoryId > 0 AndAlso Not String.IsNullOrWhiteSpace(l.ProductName)).ToList()
            If vendorId <= 0 Then Throw New BusinessException("Please select a vendor.")
            If valid.Count = 0 Then Throw New BusinessException("Please add at least one product.")

            Dim billImage As String = Nothing
            If Not String.IsNullOrEmpty(billImageFile) Then billImage = AppPaths.StoreFile(billImageFile, "bills", "bill")

            Return Db.Tx(Function(s)
                             If s.QueryRow("SELECT id FROM vendors WHERE id = @p0", vendorId) Is Nothing Then Throw New BusinessException("Invalid vendor.")
                             Dim total = 0D
                             For Each l In valid
                                 l.Quantity = Math.Max(1, l.Quantity)
                                 total += Fmt.Round2(l.Quantity * l.PurchasePrice)
                             Next
                             Dim paid = ResolvePaid(paymentType, paidInput, total)
                             Dim purchaseId = s.Insert("INSERT INTO purchases (vendor_id, bill_number, purchase_date, total_amount, paid_amount, balance_due, payment_type, notes, bill_image) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                                       vendorId, NullIfEmpty(billNumber), purchaseDate, total, paid, total - paid, NormalizeType(paymentType, paid, total), NullIfEmpty(notes), billImage)
                             For Each l In valid
                                 Dim name = l.ProductName.Trim()
                                 Dim pct = InventoryService.ProfitPercent(l.PurchasePrice, l.SalePrice)
                                 Dim lineTotal = Fmt.Round2(l.Quantity * l.PurchasePrice)
                                 Dim existing = s.QueryRow("SELECT id FROM products WHERE category_id = @p0 AND LOWER(product_name) = LOWER(@p1) LIMIT 1", l.CategoryId, name)
                                 Dim productId As Long
                                 If existing IsNot Nothing Then
                                     productId = GetLng(existing, "id")
                                     s.Exec("UPDATE products SET vendor_id=@p0, purchase_price=@p1, sale_price=@p2, profit_percent=@p3, stock_quantity = stock_quantity + @p4, updated_at=datetime('now','localtime') WHERE id=@p5",
                                            vendorId, l.PurchasePrice, l.SalePrice, pct, l.Quantity, productId)
                                 Else
                                     productId = s.Insert("INSERT INTO products (category_id, vendor_id, product_name, purchase_price, sale_price, profit_percent, stock_quantity) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                                                          l.CategoryId, vendorId, name, l.PurchasePrice, l.SalePrice, pct, l.Quantity)
                                 End If
                                 s.Exec("INSERT INTO purchase_items (purchase_id, category_id, product_id, product_name, quantity, purchase_price, sale_price, profit_percent, line_total) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                        purchaseId, l.CategoryId, productId, name, l.Quantity, l.PurchasePrice, l.SalePrice, pct, lineTotal)
                             Next
                             If paid > 0 Then
                                 ' Keep the payment trail complete: an upfront payment also appears in the payments list.
                                 s.Exec("INSERT INTO vendor_payments (vendor_id, purchase_id, amount, payment_date, notes) VALUES (@p0,@p1,@p2,@p3,@p4)",
                                        vendorId, purchaseId, paid, purchaseDate, "Paid at purchase")
                             End If
                             Return purchaseId
                         End Function)
        End Function

        ''' <summary>Records a payment against a purchase bill (capped at its balance).</summary>
        Public Function AddPayment(purchaseId As Long, amount As Decimal, paymentDate As Date, notes As String) As Decimal
            Return Db.Tx(Function(s)
                             Dim p = s.QueryRow("SELECT id, vendor_id, paid_amount, balance_due FROM purchases WHERE id = @p0", purchaseId)
                             If p Is Nothing OrElse amount <= 0 Then Throw New BusinessException("Please enter a valid amount.")
                             Dim balance = GetDec(p, "balance_due")
                             If balance <= 0 Then Throw New BusinessException("This bill is already fully paid.")
                             amount = Math.Min(Fmt.Round2(amount), balance)
                             Dim newBalance = balance - amount
                             s.Exec("UPDATE purchases SET paid_amount = paid_amount + @p0, balance_due = @p1, payment_type = @p2 WHERE id = @p3",
                                    amount, newBalance, If(newBalance <= 0, "full", "partial"), purchaseId)
                             s.Exec("INSERT INTO vendor_payments (vendor_id, purchase_id, amount, payment_date, notes) VALUES (@p0,@p1,@p2,@p3,@p4)",
                                    GetLng(p, "vendor_id"), purchaseId, amount, paymentDate, NullIfEmpty(notes))
                             Return amount
                         End Function)
        End Function

        ''' <summary>
        ''' Spreads one payment across a vendor's unpaid bills, oldest first.
        ''' Returns the amount actually applied.
        ''' </summary>
        Public Function PayVendor(vendorId As Long, amount As Decimal, paymentDate As Date, notes As String) As Decimal
            If amount <= 0 Then Throw New BusinessException("Please enter a valid amount.")
            Return Db.Tx(Function(s)
                             Dim remaining = Fmt.Round2(amount)
                             For Each p As DataRow In s.Query("SELECT id, balance_due FROM purchases WHERE vendor_id = @p0 AND balance_due > 0 ORDER BY purchase_date, id", vendorId).Rows
                                 If remaining <= 0 Then Exit For
                                 Dim bal = GetDec(p, "balance_due")
                                 Dim pay = Math.Min(bal, remaining)
                                 Dim newBal = bal - pay
                                 s.Exec("UPDATE purchases SET paid_amount = paid_amount + @p0, balance_due = @p1, payment_type = @p2 WHERE id = @p3",
                                        pay, newBal, If(newBal <= 0, "full", "partial"), GetLng(p, "id"))
                                 s.Exec("INSERT INTO vendor_payments (vendor_id, purchase_id, amount, payment_date, notes) VALUES (@p0,@p1,@p2,@p3,@p4)",
                                        vendorId, GetLng(p, "id"), pay, paymentDate, NullIfEmpty(notes))
                                 remaining -= pay
                             Next
                             If remaining = Fmt.Round2(amount) Then Throw New BusinessException("This vendor has no unpaid bills.")
                             Return Fmt.Round2(amount) - remaining
                         End Function)
        End Function

        Public Function VendorPurchases(vendorId As Long) As DataTable
            Return Db.Query("SELECT id, bill_number, purchase_date, total_amount, paid_amount, balance_due, payment_type, notes FROM purchases WHERE vendor_id = @p0 ORDER BY purchase_date DESC, id DESC", vendorId)
        End Function

        Public Function VendorPayments(vendorId As Long) As DataTable
            Return Db.Query("SELECT vp.id, vp.payment_date, vp.amount, p.bill_number, vp.purchase_id, vp.notes FROM vendor_payments vp LEFT JOIN purchases p ON p.id = vp.purchase_id WHERE vp.vendor_id = @p0 ORDER BY vp.payment_date DESC, vp.id DESC", vendorId)
        End Function

        Public Function GetPurchase(id As Long) As DataRow
            Return Db.QueryRow("SELECT p.*, v.name AS vendor_name, v.phone AS vendor_phone, v.address AS vendor_address FROM purchases p JOIN vendors v ON v.id = p.vendor_id WHERE p.id = @p0", id)
        End Function

        Public Function GetPurchaseItems(id As Long) As DataTable
            Return Db.Query("SELECT pi.product_name, c.name AS category, pi.quantity, pi.purchase_price, pi.sale_price, pi.line_total FROM purchase_items pi LEFT JOIN categories c ON c.id = pi.category_id WHERE pi.purchase_id = @p0 ORDER BY pi.id", id)
        End Function

        Public Sub SetBillImage(purchaseId As Long, imageFile As String)
            Dim rel = AppPaths.StoreFile(imageFile, "bills", "bill")
            Db.Exec("UPDATE purchases SET bill_image = @p0 WHERE id = @p1", rel, purchaseId)
        End Sub

        ''' <summary>
        ''' Chronological statement rows (bills as debit, payments as credit) with running balance.
        ''' Columns: date, description, debit, credit, balance.
        ''' </summary>
        Public Function VendorStatement(vendorId As Long) As DataTable
            Dim dt As New DataTable()
            dt.Columns.Add("date", GetType(String))
            dt.Columns.Add("description", GetType(String))
            dt.Columns.Add("debit", GetType(Decimal))
            dt.Columns.Add("credit", GetType(Decimal))
            dt.Columns.Add("balance", GetType(Decimal))
            Dim entries = Db.Query(
                "SELECT purchase_date AS d, 0 AS k, id, 'Purchase bill ' || IFNULL('#' || bill_number, '(no number)') AS descr, total_amount AS debit, 0 AS credit FROM purchases WHERE vendor_id = @p0 " &
                "UNION ALL SELECT payment_date, 1, id, 'Payment' || IFNULL(' - ' || notes, ''), 0, amount FROM vendor_payments WHERE vendor_id = @p0 " &
                "ORDER BY d, k, id", vendorId)
            Dim bal = 0D
            For Each r As DataRow In entries.Rows
                bal += GetDec(r, "debit") - GetDec(r, "credit")
                dt.Rows.Add(GetStr(r, "d"), GetStr(r, "descr"), GetDec(r, "debit"), GetDec(r, "credit"), bal)
            Next
            Return dt
        End Function

    End Module

End Namespace
