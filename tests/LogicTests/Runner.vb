Imports System.Data
Imports System.IO
Imports UniversalPOS.Core
Imports UniversalPOS.Data
Imports UniversalPOS.Services

Namespace Tests

    Module Runner

        Private _failed As Integer
        Private _passed As Integer

        Private Sub Check(cond As Boolean, what As String)
            If cond Then
                _passed += 1
            Else
                _failed += 1
                Console.WriteLine("FAIL: " & what)
            End If
        End Sub

        Private Sub Throws(action As Action, what As String)
            Try
                action()
                Check(False, what & " (expected an error)")
            Catch ex As BusinessException
                _passed += 1
            End Try
        End Sub

        Private Function Stock(id As Long) As Long
            Return Db.ScalarLong("SELECT stock_quantity FROM products WHERE id = @p0", id)
        End Function

        Function Main(args As String()) As Integer
            If args.Length = 2 AndAlso args(0) = "--seed" Then
                Seed.Run(args(1))
                Return 0
            End If
            Dim path = IO.Path.Combine(IO.Path.GetTempPath(), "pos_test_" & Guid.NewGuid().ToString("N") & ".db")
            Db.Initialize(path)
            Schema.Apply() ' idempotent

            ' password hashing matches the standard PBKDF2-SHA256
            Dim salt = Text.Encoding.ASCII.GetBytes("0123456789abcdef")
            Dim std = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("s3cret", salt, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32)
            Check(Core.Security.VerifyPassword("s3cret", "1000." & Convert.ToBase64String(salt) & "." & Convert.ToBase64String(std)), "PBKDF2 matches RFC 8018")
            Check(Not Core.Security.VerifyPassword("wrong", Core.Security.HashPassword("s3cret")), "hash rejects wrong password")

            ' users
            UserService.SaveUser(0, "admin", "Owner", "admin", True, "admin123")
            Check(UserService.Login("ADMIN", "admin123"), "login case-insensitive username")
            Check(Not UserService.Login("admin", "wrong"), "wrong password rejected")
            Throws(Sub() UserService.SaveUser(Session.UserId, "admin", "Owner", "cashier", True, ""), "last admin can't be demoted")

            ' settings
            AppSettings.SetValue("currency_symbol", "Rs.")
            AppSettings.SetValue("low_stock_threshold", "3")
            AppSettings.Reload()
            Check(AppSettings.LowStockThreshold = 3, "setting round-trip")
            Check(Fmt.Money(1234.5D) = "Rs. 1,234.50", "money format")

            ' categories + fields
            Dim catId = InventoryService.SaveCategory(0, "Mobiles", New List(Of CategoryFieldDef) From {
                New CategoryFieldDef With {.Name = "IMEI", .IsRequired = True},
                New CategoryFieldDef With {.Name = "Color"}})
            Dim fields = InventoryService.CategoryFields(catId)
            Check(fields.Count = 2 AndAlso fields(0).IsRequired, "category fields saved")
            Throws(Sub() InventoryService.SaveCategory(0, "mobiles", New List(Of CategoryFieldDef)), "duplicate category")

            ' vendor + product with initial purchase (partial)
            Dim vId = PurchaseService.SaveVendor(0, "ABC Traders", "0300", "")
            Throws(Sub() InventoryService.SaveProduct(0, catId, 0, "Phone X", "", 100, 150, 5, New Dictionary(Of Long, String), Nothing), "required field enforced")
            Dim vals = New Dictionary(Of Long, String) From {{fields(0).Id, "123456"}, {fields(1).Id, "Black"}}
            Dim p1 = InventoryService.SaveProduct(0, catId, vId, "Phone X", "111", 100, 150, 10, vals,
                                                  New InitialPurchase With {.PaymentType = "partial", .PaidAmount = 400})
            Check(Stock(p1) = 10, "initial stock")
            Check(PurchaseService.VendorPurchases(vId).Rows.Count = 1, "initial purchase created")
            Check(Db.ScalarDec("SELECT balance_due FROM purchases") = 600D, "partial balance")
            Check(InventoryService.ProductFieldSummary(p1) = "IMEI: 123456, Color: Black", "field summary")
            Throws(Sub() InventoryService.SaveProduct(0, catId, 0, "Phone Y", "111", 1, 2, 1, vals, Nothing), "duplicate barcode")
            Check(InventoryService.FindByBarcode("111") IsNot Nothing, "barcode lookup")

            ' purchase bill restocks existing + creates new
            Dim purId = PurchaseService.SavePurchase(vId, "B-1", Date.Today, "credit", 0, "", "", New List(Of PurchaseLine) From {
                New PurchaseLine With {.CategoryId = catId, .ProductName = "phone x", .Quantity = 5, .PurchasePrice = 110, .SalePrice = 160},
                New PurchaseLine With {.CategoryId = catId, .ProductName = "Charger", .Quantity = 20, .PurchasePrice = 10, .SalePrice = 25}})
            Check(Stock(p1) = 15, "restock existing product")
            Dim charger = Db.ScalarLong("SELECT id FROM products WHERE product_name='Charger'")
            Check(Stock(charger) = 20, "new product from purchase")
            Check(Db.ScalarDec("SELECT sale_price FROM products WHERE id=@p0", p1) = 160D, "price updated by purchase")
            ' pay 500 across bills oldest first: 600 + 750 owed
            Dim applied = PurchaseService.PayVendor(vId, 700, Date.Today, "cash")
            Check(applied = 700D, "vendor payment applied")
            Check(Db.ScalarDec("SELECT SUM(balance_due) FROM purchases") = 650D, "vendor balance after payment")
            Dim stmt = PurchaseService.VendorStatement(vId)
            Check(GetDec(stmt.Rows(stmt.Rows.Count - 1), "balance") = 650D, "vendor statement running balance")
            Throws(Sub() PurchaseService.DeleteVendor(vId), "vendor with bills can't be deleted")
            Check(PurchaseService.AddPayment(purId, 10000, Date.Today, "") = 650D, "payment capped at balance")

            ' sales
            Throws(Sub() SalesService.SaveSale(0, Date.Today, "", "", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = p1, .Quantity = 99, .SalePrice = 160}}), "stock check")
            Dim saleId = SalesService.SaveSale(0, Date.Today, "Ali", "0301", 20, New List(Of SaleLine) From {
                New SaleLine With {.ProductId = p1, .Quantity = 2, .SalePrice = 160},
                New SaleLine With {.ProductId = charger, .Quantity = 3, .SalePrice = 25}})
            Check(Stock(p1) = 13 AndAlso Stock(charger) = 17, "stock deducted on sale")
            Dim sale = SalesService.GetSale(saleId)
            Check(GetDec(sale, "total_amount") = 375D, "sale total with discount")
            ' profit: (160-110)*2 + (25-10)*3 - 20 = 100 + 45 - 20 = 125
            Check(GetDec(sale, "total_profit") = 125D, "sale profit")
            ' edit sale: change qty (stock restored then re-deducted)
            SalesService.SaveSale(saleId, Date.Today, "Ali", "0301", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = p1, .Quantity = 15, .SalePrice = 160}})
            Check(Stock(p1) = 0 AndAlso Stock(charger) = 20, "edit sale restores/deducts stock")
            ' failed edit must roll back entirely
            Throws(Sub() SalesService.SaveSale(saleId, Date.Today, "", "", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = charger, .Quantity = 500, .SalePrice = 1}}), "edit over stock")
            Check(Stock(p1) = 0 AndAlso SalesService.GetSaleItems(saleId).Rows.Count = 1, "failed edit rolled back")
            SalesService.DeleteSale(saleId)
            Check(Stock(p1) = 15 AndAlso SalesService.GetSale(saleId) Is Nothing, "delete sale restores stock")

            ' customer credit (udhaar)
            Throws(Sub() SalesService.SaveSale(0, Date.Today, "", "", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = charger, .Quantity = 1, .SalePrice = 25}}, 5), "credit sale needs a customer")
            Dim cs1 = SalesService.SaveSale(0, Date.Today.AddDays(-2), "Kamran", "0311", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = charger, .Quantity = 4, .SalePrice = 25}}, 30)
            Dim cs2 = SalesService.SaveSale(0, Date.Today, "kamran ", "0311", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = charger, .Quantity = 2, .SalePrice = 25}}, 0)
            Dim ck = CustomerService.CustomerKey("Kamran", "0311")
            Dim cust = CustomerService.Customers("kamran").Rows(0)
            Check(GetDec(cust, "balance") = 120D AndAlso GetLng(cust, "sales") = 2, "customer balance across sales")
            Check(CustomerService.TotalOutstanding() = 120D, "total outstanding")
            Check(CustomerService.ReceivePayment(ck, 80, Date.Today, "cash") = 80D, "payment applied")
            Check(GetDec(SalesService.GetSale(cs1), "paid_amount") = 100D AndAlso GetDec(SalesService.GetSale(cs2), "paid_amount") = 10D, "payment goes to oldest sale first")
            Dim cst = CustomerService.Statement(ck)
            Check(GetDec(cst.Rows(cst.Rows.Count - 1), "balance") = 40D, "customer statement balance")
            ' editing keeps later payments counted
            SalesService.SaveSale(cs2, Date.Today, "Kamran", "0311", 0, New List(Of SaleLine) From {New SaleLine With {.ProductId = charger, .Quantity = 2, .SalePrice = 25}}, 0)
            Check(GetDec(SalesService.GetSale(cs2), "paid_amount") = 10D, "edit keeps received payments")
            Check(CustomerService.ReceivePayment(ck, 1000, Date.Today, "") = 40D, "overpayment capped")
            Throws(Sub() CustomerService.ReceivePayment(ck, 5, Date.Today, ""), "nothing outstanding")
            Dim pay1 = CLng(CustomerService.CustomerPayments(ck).Rows(0)("id"))
            CustomerService.DeletePayment(pay1)
            Check(CustomerService.TotalOutstanding() = 40D, "deleting a payment restores balance")
            SalesService.DeleteSale(cs1) : SalesService.DeleteSale(cs2)
            Check(CustomerService.TotalOutstanding() = 0D, "deleting sales clears credit")

            ' quotations
            Dim qId = QuotationService.SaveQuotation(0, Date.Today, "Bilal", "", "sent", "", New List(Of QuoteLine) From {
                New QuoteLine With {.ProductId = charger, .CategoryId = catId, .ProductName = "Charger", .Quantity = 4, .SalePrice = 30}})
            Dim convSale = QuotationService.ConvertToSale(qId)
            Check(Stock(charger) = 16 AndAlso GetStr(QuotationService.GetQuotation(qId), "status") = "converted", "quotation converted")
            Throws(Sub() QuotationService.ConvertToSale(qId), "double convert")
            Dim q2 = QuotationService.SaveQuotation(0, Date.Today, "", "", "draft", "", New List(Of QuoteLine) From {New QuoteLine With {.ProductName = "Custom work", .Quantity = 1, .SalePrice = 500}})
            Throws(Sub() QuotationService.ConvertToSale(q2), "manual item can't convert")

            ' shopkeepers
            Dim sk = ShopkeeperService.SaveContact(0, "Neighbour Shop", "0302", "")
            ShopkeeperService.AddEntry(New ShopkeeperEntry With {.ContactId = sk, .Direction = "given", .EntryType = "product", .CategoryId = catId, .ProductId = charger, .Quantity = 2, .UnitPrice = 25, .SettlementType = "partial", .SettledAmount = 20})
            Check(Stock(charger) = 14, "given product deducts stock")
            Check(ShopkeeperService.Balance(sk) = 30D, "shopkeeper balance after partial settlement")
            Dim givenTxn = Db.ScalarLong("SELECT id FROM shopkeeper_transactions WHERE entry_type='product' AND direction='given'")
            Dim linkedSale = Db.ScalarLong("SELECT sale_id FROM shopkeeper_transactions WHERE id=@p0", givenTxn)
            Check(linkedSale > 0, "given product creates a sale")
            ShopkeeperService.ReturnEntry(givenTxn)
            Check(Stock(charger) = 16 AndAlso SalesService.GetSale(linkedSale) Is Nothing, "return restores stock and removes sale")
            Check(ShopkeeperService.Balance(sk) = -20D, "balance after return")
            Throws(Sub() ShopkeeperService.ReturnEntry(givenTxn), "double return")
            ShopkeeperService.AddEntry(New ShopkeeperEntry With {.ContactId = sk, .Direction = "received", .EntryType = "product", .CategoryId = catId, .NewProductName = "Cable", .Quantity = 5, .UnitPrice = 8})
            Check(Db.ScalarLong("SELECT stock_quantity FROM products WHERE product_name='Cable'") = 5, "received new product created")
            ShopkeeperService.AddEntry(New ShopkeeperEntry With {.ContactId = sk, .Direction = "received", .EntryType = "cash", .Description = "Cash", .CashAmount = 100})
            Dim ledger = ShopkeeperService.Ledger(sk)
            Check(GetDec(ledger.Rows(ledger.Rows.Count - 1), "balance") = ShopkeeperService.Balance(sk), "ledger running balance")

            ' expenses + maintenance
            Dim ec = ExpenseService.SaveCategory(0, "Rent")
            ExpenseService.SaveExpense(0, ec, 1000, Date.Today, "May rent")
            Throws(Sub() ExpenseService.DeleteCategory(ec), "expense category in use")
            MaintenanceService.SaveJob(0, Date.Today, "Zain", "", "Laptop", "No power", 1500, New List(Of PartLine) From {New PartLine With {.Description = "IC", .Amount = 600}})

            ' reports
            Dim rep = ReportService.Range(Date.Today.AddDays(-1), Date.Today)
            Check(rep.SalesCount = 1 AndAlso rep.Revenue = 120D, "report sales")
            Check(rep.MaintenanceProfit = 900D AndAlso rep.ExpenseTotal = 1000D, "report maintenance/expenses")
            Check(rep.NetProfit = rep.GrossProfit + 900D - 1000D, "net profit")
            Dim dash = ReportService.Dashboard()
            Check(dash.TodaySales = 120D AndAlso dash.ProductCount = 3, "dashboard")
            Check(ReportService.Last7Days().Rows.Count = 7, "7 day trend")
            Check(ReportService.SalesList(Date.Today, Date.Today, "Charger").Rows.Count = 1, "sales search by product")
            Check(InventoryService.Products(0, "phone").Rows.Count = 1, "product search")

            ' category delete protection, backup/restore
            Throws(Sub() InventoryService.DeleteCategory(catId), "category with products")
            Dim bak = path & ".bak"
            Db.BackupTo(bak)
            InventoryService.DeleteProduct(charger)
            Db.RestoreFrom(bak)
            Check(Db.ScalarLong("SELECT COUNT(*) FROM products WHERE id=@p0", charger) = 1, "restore brings data back")

            Console.WriteLine($"{_passed} passed, {_failed} failed")
            Try
                File.Delete(path) : File.Delete(bak)
            Catch
            End Try
            Return If(_failed = 0, 0, 1)
        End Function

    End Module

End Namespace
