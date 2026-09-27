Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    Public Class CategoryFieldDef
        Public Property Id As Long
        Public Property Name As String = ""
        Public Property IsRequired As Boolean
    End Class

    ''' <summary>Optional purchase bill recorded when a new product is added with a vendor.</summary>
    Public Class InitialPurchase
        Public Property BillNumber As String = ""
        Public Property PaymentType As String = "full"
        Public Property PaidAmount As Decimal
        Public Property BillImageFile As String = ""
    End Class

    Public Module InventoryService

        ' ---------------- Categories ----------------

        Public Function Categories() As DataTable
            Return Db.Query("SELECT c.id, c.name, (SELECT COUNT(*) FROM products p WHERE p.category_id = c.id) AS products, " &
                            "(SELECT COALESCE(SUM(stock_quantity),0) FROM products p WHERE p.category_id = c.id) AS stock, " &
                            "(SELECT COUNT(*) FROM category_fields f WHERE f.category_id = c.id) AS fields " &
                            "FROM categories c ORDER BY c.name COLLATE NOCASE")
        End Function

        Public Function CategoryFields(categoryId As Long) As List(Of CategoryFieldDef)
            Dim list As New List(Of CategoryFieldDef)
            For Each r As DataRow In Db.Query("SELECT id, field_name, is_required FROM category_fields WHERE category_id = @p0 ORDER BY field_order, id", categoryId).Rows
                list.Add(New CategoryFieldDef With {.Id = GetLng(r, "id"), .Name = GetStr(r, "field_name"), .IsRequired = GetLng(r, "is_required") = 1})
            Next
            Return list
        End Function

        ''' <summary>Saves a category and its custom fields. Existing fields keep their id (and product values).</summary>
        Public Function SaveCategory(id As Long, name As String, fields As List(Of CategoryFieldDef)) As Long
            name = If(name, "").Trim()
            If name = "" Then Throw New BusinessException("Category name is required.")
            Return Db.Tx(Function(s)
                             If s.ScalarLong("SELECT COUNT(*) FROM categories WHERE name = @p0 COLLATE NOCASE AND id <> @p1", name, id) > 0 Then
                                 Throw New BusinessException("A category with this name already exists.")
                             End If
                             If id > 0 Then
                                 s.Exec("UPDATE categories SET name = @p0 WHERE id = @p1", name, id)
                             Else
                                 id = s.Insert("INSERT INTO categories (name) VALUES (@p0)", name)
                             End If
                             Dim keep As New List(Of Long)
                             Dim order = 0
                             For Each f In fields
                                 Dim fname = If(f.Name, "").Trim()
                                 If fname = "" Then Continue For
                                 order += 1
                                 If f.Id > 0 Then
                                     s.Exec("UPDATE category_fields SET field_name=@p0, is_required=@p1, field_order=@p2 WHERE id=@p3 AND category_id=@p4", fname, f.IsRequired, order, f.Id, id)
                                     keep.Add(f.Id)
                                 Else
                                     keep.Add(s.Insert("INSERT INTO category_fields (category_id, field_name, is_required, field_order) VALUES (@p0,@p1,@p2,@p3)", id, fname, f.IsRequired, order))
                                 End If
                             Next
                             For Each r As DataRow In s.Query("SELECT id FROM category_fields WHERE category_id = @p0", id).Rows
                                 If Not keep.Contains(GetLng(r, "id")) Then s.Exec("DELETE FROM category_fields WHERE id = @p0", GetLng(r, "id"))
                             Next
                             Return id
                         End Function)
        End Function

        Public Sub DeleteCategory(id As Long)
            If Db.ScalarLong("SELECT COUNT(*) FROM products WHERE category_id = @p0", id) > 0 Then
                Throw New BusinessException("This category still has products. Move or delete them first.")
            End If
            Db.Exec("DELETE FROM categories WHERE id = @p0", id)
        End Sub

        ' ---------------- Products ----------------

        Public Function Products(Optional categoryId As Long = 0, Optional search As String = "", Optional lowStockOnly As Boolean = False) As DataTable
            Dim sql = "SELECT p.id, p.product_name, p.barcode, c.name AS category, v.name AS vendor, p.purchase_price, p.sale_price, " &
                      "p.profit_percent, p.stock_quantity, (p.stock_quantity * p.purchase_price) AS stock_value, p.category_id " &
                      "FROM products p JOIN categories c ON c.id = p.category_id LEFT JOIN vendors v ON v.id = p.vendor_id WHERE 1=1"
            If categoryId > 0 Then sql &= " AND p.category_id = @p0"
            If Not String.IsNullOrWhiteSpace(search) Then sql &= " AND (p.product_name LIKE @p1 OR IFNULL(p.barcode,'') LIKE @p1 OR c.name LIKE @p1)"
            If lowStockOnly Then sql &= " AND p.stock_quantity <= @p2"
            sql &= " ORDER BY p.product_name COLLATE NOCASE"
            Return Db.Query(sql, categoryId, "%" & If(search, "").Trim() & "%", AppSettings.LowStockThreshold)
        End Function

        ''' <summary>Products that can be sold (for pickers): id, display text, price, stock.</summary>
        Public Function ProductPicker(Optional categoryId As Long = 0, Optional inStockOnly As Boolean = False) As DataTable
            Dim sql = "SELECT p.id, p.product_name || '  [' || c.name || ']' AS display, p.product_name, p.sale_price, p.purchase_price, p.stock_quantity, p.category_id, p.barcode " &
                      "FROM products p JOIN categories c ON c.id = p.category_id WHERE (@p0 = 0 OR p.category_id = @p0)"
            If inStockOnly Then sql &= " AND p.stock_quantity > 0"
            sql &= " ORDER BY p.product_name COLLATE NOCASE"
            Return Db.Query(sql, categoryId)
        End Function

        Public Function FindByBarcode(code As String) As DataRow
            If String.IsNullOrWhiteSpace(code) Then Return Nothing
            Return Db.QueryRow("SELECT id, product_name, sale_price, stock_quantity FROM products WHERE barcode = @p0 LIMIT 1", code.Trim())
        End Function

        Public Function GetProduct(id As Long) As DataRow
            Return Db.QueryRow("SELECT * FROM products WHERE id = @p0", id)
        End Function

        Public Function ProductFieldValues(productId As Long) As Dictionary(Of Long, String)
            Dim d As New Dictionary(Of Long, String)
            For Each r As DataRow In Db.Query("SELECT category_field_id, field_value FROM product_field_values WHERE product_id = @p0", productId).Rows
                d(GetLng(r, "category_field_id")) = GetStr(r, "field_value")
            Next
            Return d
        End Function

        ''' <summary>Text like "Size: L, Color: Red" for a product's custom fields.</summary>
        Public Function ProductFieldSummary(productId As Long) As String
            Dim parts As New List(Of String)
            For Each r As DataRow In Db.Query("SELECT f.field_name, v.field_value FROM product_field_values v JOIN category_fields f ON f.id = v.category_field_id WHERE v.product_id = @p0 ORDER BY f.field_order", productId).Rows
                parts.Add(GetStr(r, "field_name") & ": " & GetStr(r, "field_value"))
            Next
            Return String.Join(", ", parts)
        End Function

        Public Function ProfitPercent(purchasePrice As Decimal, salePrice As Decimal) As Decimal
            If purchasePrice <= 0 Then Return 0D
            Return Fmt.Round2((salePrice - purchasePrice) / purchasePrice * 100D)
        End Function

        ''' <summary>
        ''' Adds or updates a product. When a new product is added with a vendor, a purchase bill is
        ''' also recorded so it shows up in that vendor's ledger (as in the web version).
        ''' </summary>
        Public Function SaveProduct(id As Long, categoryId As Long, vendorId As Long, name As String, barcode As String,
                                    purchasePrice As Decimal, salePrice As Decimal, stock As Integer,
                                    customValues As Dictionary(Of Long, String), purchase As InitialPurchase) As Long
            name = If(name, "").Trim()
            If name = "" OrElse categoryId <= 0 Then Throw New BusinessException("Product name and category are required.")
            If purchasePrice < 0 OrElse salePrice < 0 Then Throw New BusinessException("Prices cannot be negative.")
            stock = Math.Max(0, stock)
            barcode = If(barcode, "").Trim()

            Dim fields = CategoryFields(categoryId)
            For Each f In fields
                Dim v As String = Nothing
                If f.IsRequired AndAlso (customValues Is Nothing OrElse Not customValues.TryGetValue(f.Id, v) OrElse String.IsNullOrWhiteSpace(v)) Then
                    Throw New BusinessException(f.Name & " is required.")
                End If
            Next

            Dim billImage As String = Nothing
            If id = 0 AndAlso vendorId > 0 AndAlso purchase IsNot Nothing AndAlso purchase.BillImageFile <> "" Then
                billImage = AppPaths.StoreFile(purchase.BillImageFile, "bills", "bill")
            End If

            Return Db.Tx(Function(s)
                             If barcode <> "" AndAlso s.ScalarLong("SELECT COUNT(*) FROM products WHERE barcode = @p0 AND id <> @p1", barcode, id) > 0 Then
                                 Throw New BusinessException("Another product already uses barcode " & barcode & ".")
                             End If
                             Dim profitPct = ProfitPercent(purchasePrice, salePrice)
                             If id > 0 Then
                                 s.Exec("UPDATE products SET category_id=@p0, vendor_id=@p1, product_name=@p2, barcode=@p3, purchase_price=@p4, sale_price=@p5, profit_percent=@p6, stock_quantity=@p7, updated_at=datetime('now','localtime') WHERE id=@p8",
                                        categoryId, NullIfZero(vendorId), name, NullIfEmpty(barcode), purchasePrice, salePrice, profitPct, stock, id)
                                 s.Exec("DELETE FROM product_field_values WHERE product_id = @p0", id)
                             Else
                                 id = s.Insert("INSERT INTO products (category_id, vendor_id, product_name, barcode, purchase_price, sale_price, profit_percent, stock_quantity) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                                               categoryId, NullIfZero(vendorId), name, NullIfEmpty(barcode), purchasePrice, salePrice, profitPct, stock)
                                 If vendorId > 0 AndAlso stock > 0 Then
                                     Dim p = If(purchase, New InitialPurchase())
                                     Dim total = Fmt.Round2(purchasePrice * stock)
                                     Dim paid = PurchaseService.ResolvePaid(p.PaymentType, p.PaidAmount, total)
                                     Dim pid = s.Insert("INSERT INTO purchases (vendor_id, bill_number, purchase_date, total_amount, paid_amount, balance_due, payment_type, bill_image) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                                                        vendorId, NullIfEmpty(p.BillNumber), Date.Today, total, paid, total - paid, PurchaseService.NormalizeType(p.PaymentType, paid, total), billImage)
                                     s.Exec("INSERT INTO purchase_items (purchase_id, category_id, product_id, product_name, quantity, purchase_price, sale_price, profit_percent, line_total) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                            pid, categoryId, id, name, stock, purchasePrice, salePrice, profitPct, total)
                                     If paid > 0 Then
                                         s.Exec("INSERT INTO vendor_payments (vendor_id, purchase_id, amount, payment_date, notes) VALUES (@p0,@p1,@p2,@p3,@p4)",
                                                vendorId, pid, paid, Date.Today, "Paid at purchase")
                                     End If
                                 End If
                             End If
                             For Each f In fields
                                 Dim v As String = Nothing
                                 If customValues IsNot Nothing AndAlso customValues.TryGetValue(f.Id, v) AndAlso Not String.IsNullOrWhiteSpace(v) Then
                                     s.Exec("INSERT INTO product_field_values (product_id, category_field_id, field_value) VALUES (@p0,@p1,@p2)", id, f.Id, v.Trim())
                                 End If
                             Next
                             Return id
                         End Function)
        End Function

        Public Sub DeleteProduct(id As Long)
            ' Sales / purchase history keeps the product name snapshot; links are set to NULL by the FKs.
            Db.Exec("DELETE FROM products WHERE id = @p0", id)
        End Sub

        Public Sub AdjustStock(id As Long, delta As Integer)
            Db.Tx(Sub(s)
                      Dim current = s.ScalarLong("SELECT stock_quantity FROM products WHERE id = @p0", id)
                      If current + delta < 0 Then Throw New BusinessException("Stock cannot go below zero.")
                      s.Exec("UPDATE products SET stock_quantity = stock_quantity + @p0, updated_at = datetime('now','localtime') WHERE id = @p1", delta, id)
                  End Sub)
        End Sub

    End Module

End Namespace
