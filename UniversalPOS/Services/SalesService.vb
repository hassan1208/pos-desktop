Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    Public Class SaleLine
        Public Property ProductId As Long
        Public Property ProductName As String = ""
        Public Property Quantity As Integer
        Public Property SalePrice As Decimal
        Public ReadOnly Property LineTotal As Decimal
            Get
                Return Fmt.Round2(Quantity * SalePrice)
            End Get
        End Property
    End Class

    Public Module SalesService

        ''' <summary>
        ''' Creates (saleId = 0) or updates a sale. Stock is checked and deducted atomically;
        ''' when editing, the old items' stock is restored first so the sale can be re-validated.
        ''' </summary>
        Public Function SaveSale(saleId As Long, saleDate As Date, customerName As String, customerPhone As String,
                                 discount As Decimal, lines As IEnumerable(Of SaleLine)) As Long
            Return Db.Tx(Function(s)
                             If saleId > 0 Then
                                 If s.QueryRow("SELECT id FROM sales WHERE id = @p0", saleId) Is Nothing Then
                                     Throw New BusinessException("Sale not found.")
                                 End If
                                 RestoreStock(s, saleId)
                                 s.Exec("DELETE FROM sale_items WHERE sale_id = @p0", saleId)
                             End If

                             ' Merge duplicate product lines so the stock check sees the combined quantity.
                             Dim merged As New List(Of SaleLine)
                             For Each ln In lines
                                 If ln.ProductId <= 0 Then Continue For
                                 Dim qty = Math.Max(1, ln.Quantity)
                                 Dim existing = merged.FirstOrDefault(Function(m) m.ProductId = ln.ProductId AndAlso m.SalePrice = ln.SalePrice)
                                 If existing IsNot Nothing Then
                                     existing.Quantity += qty
                                 Else
                                     merged.Add(New SaleLine With {.ProductId = ln.ProductId, .Quantity = qty, .SalePrice = Math.Max(0D, ln.SalePrice)})
                                 End If
                             Next
                             If merged.Count = 0 Then Throw New BusinessException("Please add at least one product.")

                             Dim needed As New Dictionary(Of Long, Integer)
                             For Each ln In merged
                                 needed(ln.ProductId) = If(needed.ContainsKey(ln.ProductId), needed(ln.ProductId), 0) + ln.Quantity
                             Next

                             Dim products As New Dictionary(Of Long, DataRow)
                             For Each pid In needed.Keys
                                 Dim p = s.QueryRow("SELECT id, category_id, product_name, purchase_price, stock_quantity FROM products WHERE id = @p0", pid)
                                 If p Is Nothing Then Throw New BusinessException("A product in this sale no longer exists.")
                                 If needed(pid) > GetInt(p, "stock_quantity") Then
                                     Throw New BusinessException(GetStr(p, "product_name") & " has only " & GetInt(p, "stock_quantity") & " units in stock.")
                                 End If
                                 products(pid) = p
                             Next

                             Dim subtotal = 0D, profit = 0D
                             For Each ln In merged
                                 Dim p = products(ln.ProductId)
                                 subtotal += ln.LineTotal
                                 profit += Fmt.Round2((ln.SalePrice - GetDec(p, "purchase_price")) * ln.Quantity)
                             Next
                             discount = Math.Min(Math.Max(0D, Fmt.Round2(discount)), subtotal)
                             Dim total = subtotal - discount
                             profit -= discount

                             Dim id = saleId
                             If id > 0 Then
                                 s.Exec("UPDATE sales SET sale_date=@p0, subtotal=@p1, discount=@p2, total_amount=@p3, total_profit=@p4, customer_name=@p5, customer_phone=@p6 WHERE id=@p7",
                                        saleDate, subtotal, discount, total, profit, NullIfEmpty(customerName), NullIfEmpty(customerPhone), id)
                             Else
                                 id = s.Insert("INSERT INTO sales (sale_date, subtotal, discount, total_amount, total_profit, customer_name, customer_phone, user_id) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                                               saleDate, subtotal, discount, total, profit, NullIfEmpty(customerName), NullIfEmpty(customerPhone), NullIfZero(Session.UserId))
                             End If

                             For Each ln In merged
                                 Dim p = products(ln.ProductId)
                                 Dim pp = GetDec(p, "purchase_price")
                                 s.Exec("INSERT INTO sale_items (sale_id, product_id, category_id, product_name, quantity, purchase_price, sale_price, profit, line_total) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                        id, ln.ProductId, GetLng(p, "category_id"), GetStr(p, "product_name"), ln.Quantity, pp, ln.SalePrice,
                                        Fmt.Round2((ln.SalePrice - pp) * ln.Quantity), ln.LineTotal)
                                 s.Exec("UPDATE products SET stock_quantity = stock_quantity - @p0, updated_at = datetime('now','localtime') WHERE id = @p1", ln.Quantity, ln.ProductId)
                             Next
                             Return id
                         End Function)
        End Function

        Private Sub RestoreStock(s As DbSession, saleId As Long)
            For Each r As DataRow In s.Query("SELECT product_id, quantity FROM sale_items WHERE sale_id = @p0 AND product_id IS NOT NULL", saleId).Rows
                s.Exec("UPDATE products SET stock_quantity = stock_quantity + @p0 WHERE id = @p1", GetInt(r, "quantity"), GetLng(r, "product_id"))
            Next
        End Sub

        ''' <summary>Deletes a sale and puts its items back into stock.</summary>
        Public Sub DeleteSale(saleId As Long)
            Db.Tx(Sub(s)
                      RestoreStock(s, saleId)
                      ' A shopkeeper "given" entry that created this sale no longer has a sale behind it.
                      s.Exec("UPDATE shopkeeper_transactions SET sale_id = NULL WHERE sale_id = @p0", saleId)
                      s.Exec("DELETE FROM sales WHERE id = @p0", saleId)
                  End Sub)
        End Sub

        Public Function GetSale(saleId As Long) As DataRow
            Return Db.QueryRow("SELECT * FROM sales WHERE id = @p0", saleId)
        End Function

        Public Function GetSaleItems(saleId As Long) As DataTable
            Return Db.Query("SELECT id, product_id, category_id, product_name, quantity, purchase_price, sale_price, profit, line_total FROM sale_items WHERE sale_id = @p0 ORDER BY id", saleId)
        End Function

    End Module

End Namespace
