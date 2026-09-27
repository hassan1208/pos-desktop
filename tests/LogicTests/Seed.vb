Imports System.Data
Imports UniversalPOS.Core
Imports UniversalPOS.Data
Imports UniversalPOS.Services

Namespace Tests

    ''' <summary>Fills a database with realistic demo data:  dotnet run -- --seed path\to\pos.db</summary>
    Module Seed

        Public Sub Run(dbPath As String)
            Db.Initialize(dbPath)
            AppSettings.Reload()
            Dim rnd As New Random(7)
            Dim cats As New Dictionary(Of String, Long)
            For Each c In {"Mobiles", "Chargers", "Accessories", "Headphones"}
                Dim existing = Db.Scalar("SELECT id FROM categories WHERE name = @p0", c)
                cats(c) = If(existing Is Nothing, InventoryService.SaveCategory(0, c, If(c = "Mobiles",
                    New List(Of CategoryFieldDef) From {New CategoryFieldDef With {.Name = "IMEI", .IsRequired = False}, New CategoryFieldDef With {.Name = "Color"}},
                    New List(Of CategoryFieldDef))), CLng(existing))
            Next
            Dim v1 = PurchaseService.SaveVendor(0, "Al-Madina Traders", "0321-5550001", "Hall Road, Lahore")
            Dim v2 = PurchaseService.SaveVendor(0, "Star Mobile Wholesale", "0300-5550002", "Saddar, Karachi")
            PurchaseService.SavePurchase(v1, "AMT-1041", Date.Today.AddDays(-25), "partial", 150000, "", "", New List(Of PurchaseLine) From {
                New PurchaseLine With {.CategoryId = cats("Mobiles"), .ProductName = "Samsung Galaxy A15", .Quantity = 8, .PurchasePrice = 38500, .SalePrice = 42999},
                New PurchaseLine With {.CategoryId = cats("Mobiles"), .ProductName = "Infinix Hot 40", .Quantity = 10, .PurchasePrice = 29000, .SalePrice = 32499},
                New PurchaseLine With {.CategoryId = cats("Mobiles"), .ProductName = "Tecno Spark 20", .Quantity = 6, .PurchasePrice = 27500, .SalePrice = 30999}})
            PurchaseService.SavePurchase(v2, "SMW-778", Date.Today.AddDays(-18), "credit", 0, "", "", New List(Of PurchaseLine) From {
                New PurchaseLine With {.CategoryId = cats("Chargers"), .ProductName = "Samsung 25W Fast Charger", .Quantity = 30, .PurchasePrice = 1450, .SalePrice = 2200},
                New PurchaseLine With {.CategoryId = cats("Chargers"), .ProductName = "Anker USB-C Cable 1m", .Quantity = 40, .PurchasePrice = 650, .SalePrice = 1100},
                New PurchaseLine With {.CategoryId = cats("Accessories"), .ProductName = "Tempered Glass (universal)", .Quantity = 100, .PurchasePrice = 60, .SalePrice = 250},
                New PurchaseLine With {.CategoryId = cats("Accessories"), .ProductName = "Silicone Back Cover", .Quantity = 60, .PurchasePrice = 120, .SalePrice = 400},
                New PurchaseLine With {.CategoryId = cats("Headphones"), .ProductName = "Audionic Airbud 425", .Quantity = 15, .PurchasePrice = 2900, .SalePrice = 3999},
                New PurchaseLine With {.CategoryId = cats("Headphones"), .ProductName = "Wired Earphones", .Quantity = 4, .PurchasePrice = 220, .SalePrice = 450}})
            PurchaseService.PayVendor(v2, 40000, Date.Today.AddDays(-5), "Bank transfer")
            Db.Exec("UPDATE products SET barcode = '8801643' || id WHERE barcode IS NULL")

            Dim products = InventoryService.ProductPicker().AsEnumerable().ToList()
            Dim names = {"Ali Raza", "", "Usman", "", "Ayesha Khan", "Bilal", "", "Hamza", "Sana", ""}
            For d = 20 To 0 Step -1
                Dim count = rnd.Next(1, 5)
                For k = 1 To count
                    Dim lines As New List(Of SaleLine)
                    For Each p In products.OrderBy(Function(x) rnd.Next()).Take(rnd.Next(1, 3))
                        Dim stock = Fmt.GetInt(p, "stock_quantity")
                        If stock < 2 Then Continue For
                        lines.Add(New SaleLine With {.ProductId = Fmt.GetLng(p, "id"), .Quantity = 1, .SalePrice = Fmt.GetDec(p, "sale_price")})
                    Next
                    If lines.Count = 0 Then Continue For
                    Try
                        SalesService.SaveSale(0, Date.Today.AddDays(-d), names(rnd.Next(names.Length)), "", If(rnd.Next(4) = 0, 100D, 0D), lines)
                    Catch ex As BusinessException
                    End Try
                    products = InventoryService.ProductPicker().AsEnumerable().ToList()
                Next
            Next

            Dim ecats = ExpenseService.Categories()
            Dim rent = CLng(Db.Scalar("SELECT id FROM expense_categories WHERE name='Rent'"))
            Dim elec = CLng(Db.Scalar("SELECT id FROM expense_categories WHERE name='Electricity'"))
            Dim tea = CLng(Db.Scalar("SELECT id FROM expense_categories WHERE name LIKE 'Tea%'"))
            ExpenseService.SaveExpense(0, rent, 35000, New Date(Date.Today.Year, Date.Today.Month, 1), "Shop rent")
            ExpenseService.SaveExpense(0, elec, 8200, Date.Today.AddDays(-3), "LESCO bill")
            For d = 1 To 10
                ExpenseService.SaveExpense(0, tea, 300 + rnd.Next(0, 4) * 50, Date.Today.AddDays(-d), "")
            Next

            MaintenanceService.SaveJob(0, Date.Today.AddDays(-2), "Kashif", "0333-1112223", "iPhone 11", "Screen replacement", 14500, New List(Of PartLine) From {New PartLine With {.Description = "LCD panel", .Amount = 11000}})
            MaintenanceService.SaveJob(0, Date.Today, "Nadia", "", "Samsung A52", "Charging port", 2500, New List(Of PartLine) From {New PartLine With {.Description = "Port strip", .Amount = 700}})

            Dim sk = ShopkeeperService.SaveContact(0, "Bhai Jan Mobile Shop", "0345-7778889", "Shop 12, same market")
            Dim glass = CLng(Db.Scalar("SELECT id FROM products WHERE product_name LIKE 'Tempered%'"))
            ShopkeeperService.AddEntry(New ShopkeeperEntry With {.ContactId = sk, .Direction = "given", .EntryType = "product", .CategoryId = cats("Accessories"), .ProductId = glass, .Quantity = 10, .UnitPrice = 150, .SettlementType = "partial", .SettledAmount = 500})
            ShopkeeperService.AddEntry(New ShopkeeperEntry With {.ContactId = sk, .Direction = "received", .EntryType = "cash", .Description = "Cash borrowed", .CashAmount = 2000})

            Dim a15 = CLng(Db.Scalar("SELECT id FROM products WHERE product_name LIKE 'Samsung Galaxy%'"))
            QuotationService.SaveQuotation(0, Date.Today, "City School", "042-3334444", "sent", "Bulk order for staff", New List(Of QuoteLine) From {
                New QuoteLine With {.ProductId = a15, .CategoryId = cats("Mobiles"), .ProductName = "Samsung Galaxy A15", .Quantity = 3, .SalePrice = 41500},
                New QuoteLine With {.ProductName = "Data transfer & setup", .Quantity = 3, .SalePrice = 500}})
            Console.WriteLine("Seeded " & dbPath)
        End Sub

    End Module

End Namespace
