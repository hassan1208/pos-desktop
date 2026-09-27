Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    ''' <summary>Input for a shopkeeper (nearby trader) ledger entry.</summary>
    Public Class ShopkeeperEntry
        Public Property ContactId As Long
        ''' <summary>"given" (we gave them product/cash — they owe us more) or "received".</summary>
        Public Property Direction As String = "given"
        ''' <summary>"product" or "cash".</summary>
        Public Property EntryType As String = "product"
        Public Property TransactionDate As Date = Date.Today
        Public Property Notes As String = ""
        ' cash
        Public Property Description As String = ""
        Public Property CashAmount As Decimal
        ' product
        Public Property CategoryId As Long
        Public Property ProductId As Long
        Public Property NewProductName As String = ""
        Public Property Quantity As Integer = 1
        Public Property UnitPrice As Decimal
        ' settlement: credit | partial | full
        Public Property SettlementType As String = "credit"
        Public Property SettledAmount As Decimal
    End Class

    Public Module ShopkeeperService

        ''' <summary>Contacts with net balance. Positive balance = they owe us; negative = we owe them.</summary>
        Public Function Contacts(Optional search As String = "") As DataTable
            Return Db.Query("SELECT c.id, c.name, c.phone, c.address, " &
                            "COALESCE(SUM(CASE WHEN t.direction='given' THEN t.amount ELSE 0 END),0) AS total_given, " &
                            "COALESCE(SUM(CASE WHEN t.direction='received' THEN t.amount ELSE 0 END),0) AS total_received, " &
                            "COALESCE(SUM(CASE WHEN t.direction='given' THEN t.amount ELSE -t.amount END),0) AS balance " &
                            "FROM shopkeeper_contacts c LEFT JOIN shopkeeper_transactions t ON t.contact_id = c.id " &
                            "WHERE (@p0 = '' OR c.name LIKE '%' || @p0 || '%' OR IFNULL(c.phone,'') LIKE '%' || @p0 || '%') " &
                            "GROUP BY c.id ORDER BY c.name COLLATE NOCASE", If(search, "").Trim())
        End Function

        Public Function GetContact(id As Long) As DataRow
            Return Db.QueryRow("SELECT * FROM shopkeeper_contacts WHERE id = @p0", id)
        End Function

        Public Function SaveContact(id As Long, name As String, phone As String, address As String) As Long
            name = If(name, "").Trim()
            If name = "" Then Throw New BusinessException("Name is required.")
            If id > 0 Then
                Db.Exec("UPDATE shopkeeper_contacts SET name=@p0, phone=@p1, address=@p2 WHERE id=@p3", name, NullIfEmpty(phone), NullIfEmpty(address), id)
                Return id
            End If
            Return Db.Insert("INSERT INTO shopkeeper_contacts (name, phone, address) VALUES (@p0,@p1,@p2)", name, NullIfEmpty(phone), NullIfEmpty(address))
        End Function

        Public Sub DeleteContact(id As Long)
            Db.Exec("DELETE FROM shopkeeper_contacts WHERE id = @p0", id)
        End Sub

        ''' <summary>Ledger rows oldest first with running balance (positive = they owe us).</summary>
        Public Function Ledger(contactId As Long) As DataTable
            Dim dt = Db.Query("SELECT id, transaction_date, direction, entry_type, description, quantity, amount, notes, returned, product_id, sale_id FROM shopkeeper_transactions WHERE contact_id = @p0 ORDER BY transaction_date, id", contactId)
            dt.Columns.Add("given", GetType(Decimal))
            dt.Columns.Add("received", GetType(Decimal))
            dt.Columns.Add("balance", GetType(Decimal))
            Dim bal = 0D
            For Each r As DataRow In dt.Rows
                Dim amt = GetDec(r, "amount")
                If GetStr(r, "direction") = "given" Then
                    r("given") = amt : r("received") = 0D : bal += amt
                Else
                    r("given") = 0D : r("received") = amt : bal -= amt
                End If
                r("balance") = bal
            Next
            Return dt
        End Function

        Public Function Balance(contactId As Long) As Decimal
            Return Db.ScalarDec("SELECT COALESCE(SUM(CASE WHEN direction='given' THEN amount ELSE -amount END),0) FROM shopkeeper_transactions WHERE contact_id = @p0", contactId)
        End Function

        ''' <summary>
        ''' Records an entry. Giving a product deducts stock and creates a Sale (so it shows in sales
        ''' history and profit); receiving a product adds stock (or creates the product).
        ''' An optional settlement records the opposite cash entry at the same time.
        ''' </summary>
        Public Function AddEntry(e As ShopkeeperEntry) As Long
            If e.Direction <> "given" AndAlso e.Direction <> "received" Then Throw New BusinessException("Invalid direction.")
            If e.EntryType <> "product" AndAlso e.EntryType <> "cash" Then Throw New BusinessException("Invalid entry type.")

            Return Db.Tx(Function(s)
                             Dim contact = s.QueryRow("SELECT id, name, phone FROM shopkeeper_contacts WHERE id = @p0", e.ContactId)
                             If contact Is Nothing Then Throw New BusinessException("Please select a valid shopkeeper.")

                             Dim description As String
                             Dim amount As Decimal
                             Dim productId As Object = Nothing
                             Dim quantity As Object = Nothing
                             Dim saleId As Object = Nothing

                             If e.EntryType = "cash" Then
                                 description = If(e.Description, "").Trim()
                                 amount = Fmt.Round2(e.CashAmount)
                                 If description = "" OrElse amount <= 0 Then Throw New BusinessException("Please enter a description and amount.")
                             Else
                                 Dim qty = Math.Max(1, e.Quantity)
                                 Dim price = Math.Max(0D, e.UnitPrice)
                                 If s.QueryRow("SELECT id FROM categories WHERE id = @p0", e.CategoryId) Is Nothing Then Throw New BusinessException("Please select a valid category.")
                                 amount = Fmt.Round2(qty * price)
                                 If e.Direction = "given" Then
                                     If e.ProductId <= 0 Then Throw New BusinessException("Please select a product to give.")
                                     Dim p = s.QueryRow("SELECT id, product_name, purchase_price, stock_quantity, category_id FROM products WHERE id = @p0", e.ProductId)
                                     If p Is Nothing Then Throw New BusinessException("Product not found.")
                                     If qty > GetInt(p, "stock_quantity") Then Throw New BusinessException(GetStr(p, "product_name") & " has only " & GetInt(p, "stock_quantity") & " units in stock.")
                                     Dim pp = GetDec(p, "purchase_price")
                                     Dim profit = Fmt.Round2((price - pp) * qty)
                                     s.Exec("UPDATE products SET stock_quantity = stock_quantity - @p0 WHERE id = @p1", qty, e.ProductId)
                                     Dim sid = s.Insert("INSERT INTO sales (sale_date, subtotal, discount, total_amount, total_profit, customer_name, customer_phone, user_id, paid_amount) VALUES (@p0,@p1,0,@p1,@p2,@p3,@p4,@p5,@p1)",
                                                        e.TransactionDate, amount, profit, GetStr(contact, "name"), NullIfEmpty(GetStr(contact, "phone")), NullIfZero(Session.UserId))
                                     s.Exec("INSERT INTO sale_items (sale_id, product_id, category_id, product_name, quantity, purchase_price, sale_price, profit, line_total) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                            sid, e.ProductId, GetLng(p, "category_id"), GetStr(p, "product_name"), qty, pp, price, profit, amount)
                                     description = GetStr(p, "product_name")
                                     saleId = sid
                                     productId = e.ProductId
                                 Else
                                     If e.ProductId > 0 Then
                                         Dim p = s.QueryRow("SELECT id, product_name FROM products WHERE id = @p0", e.ProductId)
                                         If p Is Nothing Then Throw New BusinessException("Product not found.")
                                         s.Exec("UPDATE products SET stock_quantity = stock_quantity + @p0 WHERE id = @p1", qty, e.ProductId)
                                         description = GetStr(p, "product_name")
                                         productId = e.ProductId
                                     ElseIf Not String.IsNullOrWhiteSpace(e.NewProductName) Then
                                         description = e.NewProductName.Trim()
                                         productId = s.Insert("INSERT INTO products (category_id, product_name, purchase_price, sale_price, stock_quantity) VALUES (@p0,@p1,@p2,@p2,@p3)",
                                                              e.CategoryId, description, price, qty)
                                     Else
                                         Throw New BusinessException("Select an existing product or type a new product name.")
                                     End If
                                 End If
                                 quantity = qty
                             End If

                             Dim id = s.Insert("INSERT INTO shopkeeper_transactions (contact_id, direction, entry_type, product_id, description, amount, quantity, sale_id, transaction_date, notes) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                                               e.ContactId, e.Direction, e.EntryType, productId, description, amount, quantity, saleId, e.TransactionDate, NullIfEmpty(e.Notes))

                             Dim settled As Decimal
                             Select Case e.SettlementType
                                 Case "full" : settled = amount
                                 Case "partial" : settled = Math.Min(Math.Max(Fmt.Round2(e.SettledAmount), 0D), amount)
                                 Case Else : settled = 0D
                             End Select
                             If settled > 0 Then
                                 s.Exec("INSERT INTO shopkeeper_transactions (contact_id, direction, entry_type, description, amount, transaction_date, notes) VALUES (@p0,@p1,'cash',@p2,@p3,@p4,@p5)",
                                        e.ContactId, If(e.Direction = "given", "received", "given"), "Settlement for: " & description, settled, e.TransactionDate, NullIfEmpty(e.Notes))
                             End If
                             Return id
                         End Function)
        End Function

        ''' <summary>Reverses a product entry (stock goes back, the linked sale is removed).</summary>
        Public Sub ReturnEntry(txnId As Long)
            Db.Tx(Sub(s)
                      Dim t = s.QueryRow("SELECT * FROM shopkeeper_transactions WHERE id = @p0", txnId)
                      If t Is Nothing OrElse GetStr(t, "entry_type") <> "product" OrElse GetLng(t, "returned") = 1 OrElse GetLng(t, "product_id") = 0 Then
                          Throw New BusinessException("This transaction cannot be returned.")
                      End If
                      Dim qty = GetInt(t, "quantity")
                      Dim pid = GetLng(t, "product_id")
                      Dim newDirection As String
                      If GetStr(t, "direction") = "given" Then
                          s.Exec("UPDATE products SET stock_quantity = stock_quantity + @p0 WHERE id = @p1", qty, pid)
                          If GetLng(t, "sale_id") > 0 Then s.Exec("DELETE FROM sales WHERE id = @p0", GetLng(t, "sale_id"))
                          newDirection = "received"
                      Else
                          Dim stock = s.ScalarLong("SELECT stock_quantity FROM products WHERE id = @p0", pid)
                          If stock < qty Then Throw New BusinessException("Cannot return - not enough of this product left in stock (some may already be sold).")
                          s.Exec("UPDATE products SET stock_quantity = stock_quantity - @p0 WHERE id = @p1", qty, pid)
                          newDirection = "given"
                      End If
                      s.Exec("INSERT INTO shopkeeper_transactions (contact_id, direction, entry_type, product_id, description, amount, quantity, transaction_date) VALUES (@p0,@p1,'product',@p2,@p3,@p4,@p5,@p6)",
                             GetLng(t, "contact_id"), newDirection, pid, "Return: " & GetStr(t, "description"), GetDec(t, "amount"), qty, Date.Today)
                      s.Exec("UPDATE shopkeeper_transactions SET returned = 1 WHERE id = @p0", txnId)
                  End Sub)
        End Sub

        ''' <summary>Deletes a ledger line only (stock and sales are not touched, same as the web version).</summary>
        Public Sub DeleteEntry(txnId As Long)
            Db.Exec("DELETE FROM shopkeeper_transactions WHERE id = @p0", txnId)
        End Sub

    End Module

End Namespace
