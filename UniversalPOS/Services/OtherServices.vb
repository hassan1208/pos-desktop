Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data

Namespace Services

    Public Class QuoteLine
        ''' <summary>0 for a manual / custom item that isn't in the catalog.</summary>
        Public Property ProductId As Long
        Public Property CategoryId As Long
        Public Property ProductName As String = ""
        Public Property Quantity As Integer
        Public Property SalePrice As Decimal
    End Class

    Public Module QuotationService

        Public ReadOnly Statuses As String() = {"draft", "sent", "accepted", "rejected", "converted"}

        Public Function Quotations(Optional status As String = "", Optional search As String = "") As DataTable
            Return Db.Query("SELECT q.id, q.quote_date, q.customer_name, q.customer_phone, " &
                            "(SELECT COUNT(*) FROM quotation_items i WHERE i.quotation_id = q.id) AS items, q.total_amount, q.status, q.converted_sale_id " &
                            "FROM quotations q WHERE (@p0 = '' OR q.status = @p0) AND (@p1 = '' OR IFNULL(q.customer_name,'') LIKE '%' || @p1 || '%' OR IFNULL(q.customer_phone,'') LIKE '%' || @p1 || '%') " &
                            "ORDER BY q.quote_date DESC, q.id DESC", If(status, ""), If(search, "").Trim())
        End Function

        Public Function GetQuotation(id As Long) As DataRow
            Return Db.QueryRow("SELECT * FROM quotations WHERE id = @p0", id)
        End Function

        Public Function GetItems(id As Long) As DataTable
            Return Db.Query("SELECT product_id, category_id, product_name, quantity, sale_price, line_total FROM quotation_items WHERE quotation_id = @p0 ORDER BY id", id)
        End Function

        Public Function SaveQuotation(id As Long, quoteDate As Date, customerName As String, customerPhone As String,
                                      status As String, notes As String, lines As IEnumerable(Of QuoteLine)) As Long
            Dim valid = lines.Where(Function(l) Not String.IsNullOrWhiteSpace(l.ProductName)).ToList()
            If valid.Count = 0 Then Throw New BusinessException("Please add at least one item.")
            If Not Statuses.Contains(status) OrElse status = "converted" Then status = "draft"
            Return Db.Tx(Function(s)
                             If id > 0 Then
                                 Dim q = s.QueryRow("SELECT status FROM quotations WHERE id = @p0", id)
                                 If q Is Nothing Then Throw New BusinessException("Quotation not found.")
                                 If GetStr(q, "status") = "converted" Then Throw New BusinessException("A converted quotation can't be edited.")
                             End If
                             Dim total = valid.Sum(Function(l) Fmt.Round2(Math.Max(1, l.Quantity) * l.SalePrice))
                             If id > 0 Then
                                 s.Exec("UPDATE quotations SET quote_date=@p0, customer_name=@p1, customer_phone=@p2, total_amount=@p3, status=@p4, notes=@p5 WHERE id=@p6",
                                        quoteDate, NullIfEmpty(customerName), NullIfEmpty(customerPhone), total, status, NullIfEmpty(notes), id)
                                 s.Exec("DELETE FROM quotation_items WHERE quotation_id = @p0", id)
                             Else
                                 id = s.Insert("INSERT INTO quotations (quote_date, customer_name, customer_phone, total_amount, status, notes) VALUES (@p0,@p1,@p2,@p3,@p4,@p5)",
                                               quoteDate, NullIfEmpty(customerName), NullIfEmpty(customerPhone), total, status, NullIfEmpty(notes))
                             End If
                             For Each l In valid
                                 Dim qty = Math.Max(1, l.Quantity)
                                 s.Exec("INSERT INTO quotation_items (quotation_id, product_id, category_id, product_name, quantity, sale_price, line_total) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                                        id, NullIfZero(l.ProductId), NullIfZero(l.CategoryId), l.ProductName.Trim(), qty, l.SalePrice, Fmt.Round2(qty * l.SalePrice))
                             Next
                             Return id
                         End Function)
        End Function

        Public Sub SetStatus(id As Long, status As String)
            If Not Statuses.Contains(status) OrElse status = "converted" Then Throw New BusinessException("Invalid status.")
            If Db.Exec("UPDATE quotations SET status = @p0 WHERE id = @p1 AND status <> 'converted'", status, id) = 0 Then
                Throw New BusinessException("A converted quotation can't be changed.")
            End If
        End Sub

        Public Sub DeleteQuotation(id As Long)
            Db.Exec("DELETE FROM quotations WHERE id = @p0", id)
        End Sub

        ''' <summary>Turns a quotation into a real sale (checks stock). Returns the new sale id.</summary>
        Public Function ConvertToSale(id As Long) As Long
            Dim q = GetQuotation(id)
            If q Is Nothing Then Throw New BusinessException("Quotation not found.")
            If GetStr(q, "status") = "converted" Then Throw New BusinessException("This quotation was already converted.")
            Dim items = GetItems(id)
            If items.Rows.Count = 0 Then Throw New BusinessException("This quotation has no items to convert.")
            Dim lines As New List(Of SaleLine)
            For Each r As DataRow In items.Rows
                If GetLng(r, "product_id") = 0 Then
                    Throw New BusinessException("""" & GetStr(r, "product_name") & """ is a manual/custom item (or its product was deleted), so this quotation can't be auto-converted. Remove it or add that sale manually.")
                End If
                lines.Add(New SaleLine With {.ProductId = GetLng(r, "product_id"), .Quantity = GetInt(r, "quantity"), .SalePrice = GetDec(r, "sale_price")})
            Next
            Dim saleId = SalesService.SaveSale(0, Date.Today, GetStr(q, "customer_name"), GetStr(q, "customer_phone"), 0D, lines)
            Db.Exec("UPDATE quotations SET status = 'converted', converted_sale_id = @p0 WHERE id = @p1", saleId, id)
            Return saleId
        End Function

    End Module

    Public Module ExpenseService

        Public Function Categories() As DataTable
            Return Db.Query("SELECT ec.id, ec.name, (SELECT COUNT(*) FROM expenses e WHERE e.category_id = ec.id) AS entries, " &
                            "(SELECT COALESCE(SUM(amount),0) FROM expenses e WHERE e.category_id = ec.id) AS total FROM expense_categories ec ORDER BY ec.name COLLATE NOCASE")
        End Function

        Public Function SaveCategory(id As Long, name As String) As Long
            name = If(name, "").Trim()
            If name = "" Then Throw New BusinessException("Category name is required.")
            If id > 0 Then
                Db.Exec("UPDATE expense_categories SET name = @p0 WHERE id = @p1", name, id)
                Return id
            End If
            Return Db.Insert("INSERT INTO expense_categories (name) VALUES (@p0)", name)
        End Function

        Public Sub DeleteCategory(id As Long)
            If Db.ScalarLong("SELECT COUNT(*) FROM expenses WHERE category_id = @p0", id) > 0 Then
                Throw New BusinessException("This category has expenses. Delete them first.")
            End If
            Db.Exec("DELETE FROM expense_categories WHERE id = @p0", id)
        End Sub

        Public Function Expenses(fromDate As Date, toDate As Date, Optional categoryId As Long = 0) As DataTable
            Return Db.Query("SELECT e.id, e.expense_date, ec.name AS category, e.amount, e.notes, e.category_id FROM expenses e JOIN expense_categories ec ON ec.id = e.category_id " &
                            "WHERE e.expense_date BETWEEN @p0 AND @p1 AND (@p2 = 0 OR e.category_id = @p2) ORDER BY e.expense_date DESC, e.id DESC", fromDate, toDate, categoryId)
        End Function

        Public Function SaveExpense(id As Long, categoryId As Long, amount As Decimal, expenseDate As Date, notes As String) As Long
            If categoryId <= 0 Then Throw New BusinessException("Please select a category.")
            If amount <= 0 Then Throw New BusinessException("Amount must be greater than zero.")
            amount = Fmt.Round2(amount)
            If id > 0 Then
                Db.Exec("UPDATE expenses SET category_id=@p0, amount=@p1, expense_date=@p2, notes=@p3 WHERE id=@p4", categoryId, amount, expenseDate, NullIfEmpty(notes), id)
                Return id
            End If
            Return Db.Insert("INSERT INTO expenses (category_id, amount, expense_date, notes) VALUES (@p0,@p1,@p2,@p3)", categoryId, amount, expenseDate, NullIfEmpty(notes))
        End Function

        Public Sub DeleteExpense(id As Long)
            Db.Exec("DELETE FROM expenses WHERE id = @p0", id)
        End Sub

    End Module

    Public Class PartLine
        Public Property Description As String = ""
        Public Property Amount As Decimal
    End Class

    Public Module MaintenanceService

        Public Function Jobs(fromDate As Date, toDate As Date, Optional search As String = "") As DataTable
            Return Db.Query("SELECT id, maintenance_date, customer_name, customer_phone, item_description, issue_notes, total_amount AS parts_cost, customer_charge, (customer_charge - total_amount) AS profit " &
                            "FROM maintenance_jobs WHERE maintenance_date BETWEEN @p0 AND @p1 AND (@p2 = '' OR item_description LIKE '%' || @p2 || '%' OR IFNULL(customer_name,'') LIKE '%' || @p2 || '%' OR IFNULL(customer_phone,'') LIKE '%' || @p2 || '%') " &
                            "ORDER BY maintenance_date DESC, id DESC", fromDate, toDate, If(search, "").Trim())
        End Function

        Public Function GetJob(id As Long) As DataRow
            Return Db.QueryRow("SELECT * FROM maintenance_jobs WHERE id = @p0", id)
        End Function

        Public Function GetParts(id As Long) As DataTable
            Return Db.Query("SELECT description, amount FROM maintenance_parts WHERE maintenance_id = @p0 ORDER BY id", id)
        End Function

        Public Function SaveJob(id As Long, jobDate As Date, customerName As String, customerPhone As String, item As String,
                                issue As String, customerCharge As Decimal, parts As IEnumerable(Of PartLine)) As Long
            item = If(item, "").Trim()
            If item = "" Then Throw New BusinessException("Item description is required.")
            Dim valid = parts.Where(Function(p) Not String.IsNullOrWhiteSpace(p.Description)).ToList()
            Dim cost = valid.Sum(Function(p) Fmt.Round2(Math.Max(0D, p.Amount)))
            Return Db.Tx(Function(s)
                             If id > 0 Then
                                 s.Exec("UPDATE maintenance_jobs SET customer_name=@p0, customer_phone=@p1, item_description=@p2, issue_notes=@p3, total_amount=@p4, customer_charge=@p5, maintenance_date=@p6 WHERE id=@p7",
                                        NullIfEmpty(customerName), NullIfEmpty(customerPhone), item, NullIfEmpty(issue), cost, Fmt.Round2(customerCharge), jobDate, id)
                                 s.Exec("DELETE FROM maintenance_parts WHERE maintenance_id = @p0", id)
                             Else
                                 id = s.Insert("INSERT INTO maintenance_jobs (customer_name, customer_phone, item_description, issue_notes, total_amount, customer_charge, maintenance_date) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                                               NullIfEmpty(customerName), NullIfEmpty(customerPhone), item, NullIfEmpty(issue), cost, Fmt.Round2(customerCharge), jobDate)
                             End If
                             For Each p In valid
                                 s.Exec("INSERT INTO maintenance_parts (maintenance_id, description, amount) VALUES (@p0,@p1,@p2)", id, p.Description.Trim(), Fmt.Round2(Math.Max(0D, p.Amount)))
                             Next
                             Return id
                         End Function)
        End Function

        Public Sub DeleteJob(id As Long)
            Db.Exec("DELETE FROM maintenance_jobs WHERE id = @p0", id)
        End Sub

    End Module

    Public Module UserService

        Public Function Users() As DataTable
            Return Db.Query("SELECT id, username, full_name, role, CASE is_active WHEN 1 THEN 'Active' ELSE 'Disabled' END AS status, created_at FROM users ORDER BY username")
        End Function

        Public Function HasUsers() As Boolean
            Return Db.ScalarLong("SELECT COUNT(*) FROM users") > 0
        End Function

        Public Function Login(username As String, password As String) As Boolean
            Dim u = Db.QueryRow("SELECT * FROM users WHERE username = @p0 AND is_active = 1", If(username, "").Trim())
            If u Is Nothing OrElse Not Security.VerifyPassword(password, GetStr(u, "password_hash")) Then Return False
            Session.UserId = GetLng(u, "id")
            Session.UserName = GetStr(u, "username")
            Session.FullName = GetStr(u, "full_name")
            Session.Role = GetStr(u, "role")
            Db.Exec("INSERT INTO login_history (user_id, machine_name) VALUES (@p0,@p1)", Session.UserId, Environment.MachineName)
            Return True
        End Function

        Public Function SaveUser(id As Long, username As String, fullName As String, role As String, active As Boolean, password As String) As Long
            username = If(username, "").Trim()
            If username = "" Then Throw New BusinessException("Username is required.")
            If role <> "admin" AndAlso role <> "cashier" Then role = "cashier"
            If id = 0 AndAlso String.IsNullOrEmpty(password) Then Throw New BusinessException("Password is required for a new user.")
            If Not String.IsNullOrEmpty(password) AndAlso password.Length < 4 Then Throw New BusinessException("Password must be at least 4 characters.")
            Return Db.Tx(Function(s)
                             If s.ScalarLong("SELECT COUNT(*) FROM users WHERE username = @p0 AND id <> @p1", username, id) > 0 Then Throw New BusinessException("This username is taken.")
                             If id > 0 Then
                                 s.Exec("UPDATE users SET username=@p0, full_name=@p1, role=@p2, is_active=@p3 WHERE id=@p4", username, NullIfEmpty(fullName), role, active, id)
                                 If Not String.IsNullOrEmpty(password) Then s.Exec("UPDATE users SET password_hash=@p0 WHERE id=@p1", Security.HashPassword(password), id)
                             Else
                                 id = s.Insert("INSERT INTO users (username, full_name, password_hash, role, is_active) VALUES (@p0,@p1,@p2,@p3,@p4)", username, NullIfEmpty(fullName), Security.HashPassword(password), role, active)
                             End If
                             If s.ScalarLong("SELECT COUNT(*) FROM users WHERE role='admin' AND is_active=1") = 0 Then
                                 Throw New BusinessException("At least one active admin user is required.")
                             End If
                             Return id
                         End Function)
        End Function

        Public Sub DeleteUser(id As Long)
            If id = Session.UserId Then Throw New BusinessException("You can't delete the user you are signed in as.")
            Db.Tx(Sub(s)
                      s.Exec("DELETE FROM users WHERE id = @p0", id)
                      If s.ScalarLong("SELECT COUNT(*) FROM users WHERE role='admin' AND is_active=1") = 0 Then
                          Throw New BusinessException("At least one active admin user is required.")
                      End If
                  End Sub)
        End Sub

        Public Sub ChangeOwnPassword(current As String, newPassword As String)
            Dim u = Db.QueryRow("SELECT password_hash FROM users WHERE id = @p0", Session.UserId)
            If u Is Nothing OrElse Not Security.VerifyPassword(current, GetStr(u, "password_hash")) Then Throw New BusinessException("Current password is incorrect.")
            If String.IsNullOrEmpty(newPassword) OrElse newPassword.Length < 4 Then Throw New BusinessException("New password must be at least 4 characters.")
            Db.Exec("UPDATE users SET password_hash = @p0 WHERE id = @p1", Security.HashPassword(newPassword), Session.UserId)
        End Sub

        Public Function LoginHistory() As DataTable
            Return Db.Query("SELECT h.logged_in_at, u.username, h.machine_name FROM login_history h JOIN users u ON u.id = h.user_id ORDER BY h.id DESC LIMIT 100")
        End Function

    End Module

End Namespace
