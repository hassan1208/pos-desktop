Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Services

Namespace Printing

    ''' <summary>Builds the printable documents (invoice, quotation, bill, statements, lists).</summary>
    Public Module Documents

        Public Function SaleInvoice(saleId As Long) As PrintDoc
            Dim sale = SalesService.GetSale(saleId)
            If sale Is Nothing Then Throw New BusinessException("Sale not found.")
            Dim doc As New PrintDoc With {.Title = "INVOICE", .DocNumber = Fmt.DocNo("", saleId), .DocDate = Fmt.ShowDate(GetStr(sale, "sale_date"))}
            doc.PartyLines.Add("Customer: " & If(GetStr(sale, "customer_name") = "", "Walk-in Customer", GetStr(sale, "customer_name")))
            If GetStr(sale, "customer_phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(sale, "customer_phone"))
            doc.Columns.Add(New PrintColumn("#", 0.5F, StringAlignment.Near, True))
            doc.Columns.Add(New PrintColumn("Product", 5))
            doc.Columns.Add(New PrintColumn("Qty", 1, StringAlignment.Center))
            doc.Columns.Add(New PrintColumn("Price", 2, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Total", 2.2F, StringAlignment.Far))
            Dim n = 0
            For Each r As DataRow In SalesService.GetSaleItems(saleId).Rows
                n += 1
                doc.Rows.Add({n.ToString(), GetStr(r, "product_name"), GetInt(r, "quantity").ToString(), Fmt.Num(GetDec(r, "sale_price")), Fmt.Num(GetDec(r, "line_total"))})
            Next
            Dim discount = GetDec(sale, "discount")
            If discount > 0 Then
                doc.AddTotal("Subtotal:", Fmt.Money(GetDec(sale, "subtotal")))
                doc.AddTotal("Discount:", "- " & Fmt.Money(discount))
            End If
            doc.AddTotal("Grand Total:", Fmt.Money(GetDec(sale, "total_amount")))
            Dim due = GetDec(sale, "total_amount") - GetDec(sale, "paid_amount")
            If due > 0.004D Then
                doc.GrandTotalIndex = doc.Totals.Count - 1
                doc.AddTotal("Paid:", Fmt.Money(GetDec(sale, "paid_amount")))
                doc.AddTotal("Balance Due:", Fmt.Money(due))
            End If
            Return doc
        End Function

        Public Function Quotation(quoteId As Long) As PrintDoc
            Dim q = QuotationService.GetQuotation(quoteId)
            If q Is Nothing Then Throw New BusinessException("Quotation not found.")
            Dim doc As New PrintDoc With {.Title = "QUOTATION", .DocNumber = Fmt.DocNo("Q-", quoteId), .DocDate = Fmt.ShowDate(GetStr(q, "quote_date")), .Notes = GetStr(q, "notes"),
                                          .Footer = "This is a price quotation, not an invoice. Prices are subject to stock availability."}
            doc.PartyLines.Add("Customer: " & If(GetStr(q, "customer_name") = "", "-", GetStr(q, "customer_name")))
            If GetStr(q, "customer_phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(q, "customer_phone"))
            doc.Columns.Add(New PrintColumn("#", 0.5F, StringAlignment.Near, True))
            doc.Columns.Add(New PrintColumn("Item", 5))
            doc.Columns.Add(New PrintColumn("Qty", 1, StringAlignment.Center))
            doc.Columns.Add(New PrintColumn("Price", 2, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Total", 2.2F, StringAlignment.Far))
            Dim n = 0
            For Each r As DataRow In QuotationService.GetItems(quoteId).Rows
                n += 1
                doc.Rows.Add({n.ToString(), GetStr(r, "product_name"), GetInt(r, "quantity").ToString(), Fmt.Num(GetDec(r, "sale_price")), Fmt.Num(GetDec(r, "line_total"))})
            Next
            doc.AddTotal("Total:", Fmt.Money(GetDec(q, "total_amount")))
            Return doc
        End Function

        Public Function PurchaseBill(purchaseId As Long) As PrintDoc
            Dim p = PurchaseService.GetPurchase(purchaseId)
            If p Is Nothing Then Throw New BusinessException("Purchase not found.")
            Dim doc As New PrintDoc With {.Title = "PURCHASE BILL", .DocNumber = If(GetStr(p, "bill_number") = "", Fmt.DocNo("P-", purchaseId), GetStr(p, "bill_number")),
                                          .DocDate = Fmt.ShowDate(GetStr(p, "purchase_date")), .Notes = GetStr(p, "notes"), .Footer = " ", .Format = If(AppSettings.InvoiceFormat = "thermal", "a4", "")}
            doc.PartyLines.Add("Vendor: " & GetStr(p, "vendor_name"))
            If GetStr(p, "vendor_phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(p, "vendor_phone"))
            doc.Columns.Add(New PrintColumn("Product", 4))
            doc.Columns.Add(New PrintColumn("Category", 2))
            doc.Columns.Add(New PrintColumn("Qty", 1, StringAlignment.Center))
            doc.Columns.Add(New PrintColumn("Cost", 1.6F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Sale Price", 1.6F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Total", 2, StringAlignment.Far))
            For Each r As DataRow In PurchaseService.GetPurchaseItems(purchaseId).Rows
                doc.Rows.Add({GetStr(r, "product_name"), GetStr(r, "category"), GetInt(r, "quantity").ToString(), Fmt.Num(GetDec(r, "purchase_price")), Fmt.Num(GetDec(r, "sale_price")), Fmt.Num(GetDec(r, "line_total"))})
            Next
            doc.AddTotal("Bill Total:", Fmt.Money(GetDec(p, "total_amount")))
            doc.AddTotal("Paid:", Fmt.Money(GetDec(p, "paid_amount")))
            doc.AddTotal("Balance Due:", Fmt.Money(GetDec(p, "balance_due")))
            doc.GrandTotalIndex = 2
            Return doc
        End Function

        Public Function VendorStatement(vendorId As Long) As PrintDoc
            Dim v = Data.Db.QueryRow("SELECT * FROM vendors WHERE id = @p0", vendorId)
            Dim doc As New PrintDoc With {.Title = "VENDOR STATEMENT", .DocDate = Date.Today.ToString("dd MMM yyyy", Fmt.Inv), .Format = "a4", .Footer = " "}
            doc.PartyLines.Add("Vendor: " & GetStr(v, "name"))
            If GetStr(v, "phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(v, "phone"))
            If GetStr(v, "address") <> "" Then doc.PartyLines.Add("Address: " & GetStr(v, "address"))
            doc.Columns.Add(New PrintColumn("Date", 1.6F))
            doc.Columns.Add(New PrintColumn("Description", 4))
            doc.Columns.Add(New PrintColumn("Bill Amount", 1.8F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Paid", 1.8F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Balance", 1.8F, StringAlignment.Far))
            Dim t = PurchaseService.VendorStatement(vendorId)
            Dim debit = 0D, credit = 0D
            For Each r As DataRow In t.Rows
                debit += GetDec(r, "debit") : credit += GetDec(r, "credit")
                doc.Rows.Add({Fmt.ShowDate(GetStr(r, "date")), GetStr(r, "description"), NumOrBlank(GetDec(r, "debit")), NumOrBlank(GetDec(r, "credit")), Fmt.Num(GetDec(r, "balance"))})
            Next
            doc.AddTotal("Total Purchased:", Fmt.Money(debit))
            doc.AddTotal("Total Paid:", Fmt.Money(credit))
            doc.AddTotal("Balance Payable:", Fmt.Money(debit - credit))
            Return doc
        End Function

        Public Function ShopkeeperStatement(contactId As Long) As PrintDoc
            Dim c = ShopkeeperService.GetContact(contactId)
            Dim doc As New PrintDoc With {.Title = "ACCOUNT STATEMENT", .DocDate = Date.Today.ToString("dd MMM yyyy", Fmt.Inv), .Format = "a4", .Footer = " "}
            doc.PartyLines.Add("Account: " & GetStr(c, "name"))
            If GetStr(c, "phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(c, "phone"))
            doc.Columns.Add(New PrintColumn("Date", 1.5F))
            doc.Columns.Add(New PrintColumn("Description", 4))
            doc.Columns.Add(New PrintColumn("Qty", 0.8F, StringAlignment.Center))
            doc.Columns.Add(New PrintColumn("Given", 1.6F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Received", 1.6F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Balance", 1.8F, StringAlignment.Far))
            Dim given = 0D, received = 0D
            For Each r As DataRow In ShopkeeperService.Ledger(contactId).Rows
                given += GetDec(r, "given") : received += GetDec(r, "received")
                Dim desc = GetStr(r, "description") & If(GetStr(r, "notes") <> "", " (" & GetStr(r, "notes") & ")", "") & If(GetLng(r, "returned") = 1, " [returned]", "")
                doc.Rows.Add({Fmt.ShowDate(GetStr(r, "transaction_date")), desc, If(GetLng(r, "quantity") > 0, GetLng(r, "quantity").ToString(), ""),
                              NumOrBlank(GetDec(r, "given")), NumOrBlank(GetDec(r, "received")), Fmt.Num(GetDec(r, "balance"))})
            Next
            Dim bal = given - received
            doc.AddTotal("Total Given:", Fmt.Money(given))
            doc.AddTotal("Total Received:", Fmt.Money(received))
            doc.AddTotal(If(bal >= 0, "Balance Receivable:", "Balance Payable:"), Fmt.Money(Math.Abs(bal)))
            Return doc
        End Function

        Public Function CustomerStatement(ckey As String) As PrintDoc
            Dim c = Data.Db.QueryRow("SELECT MAX(customer_name) AS name, MAX(customer_phone) AS phone FROM sales WHERE " &
                                     "CASE WHEN IFNULL(TRIM(customer_phone),'') <> '' THEN 'p:' || TRIM(customer_phone) ELSE 'n:' || LOWER(TRIM(IFNULL(customer_name,''))) END = @p0", ckey)
            Dim doc As New PrintDoc With {.Title = "CUSTOMER STATEMENT", .DocDate = Date.Today.ToString("dd MMM yyyy", Fmt.Inv), .Format = "a4", .Footer = " "}
            doc.PartyLines.Add("Customer: " & GetStr(c, "name"))
            If GetStr(c, "phone") <> "" Then doc.PartyLines.Add("Phone: " & GetStr(c, "phone"))
            doc.Columns.Add(New PrintColumn("Date", 1.6F))
            doc.Columns.Add(New PrintColumn("Description", 4))
            doc.Columns.Add(New PrintColumn("Sale", 1.8F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Paid", 1.8F, StringAlignment.Far))
            doc.Columns.Add(New PrintColumn("Balance", 1.8F, StringAlignment.Far))
            Dim debit = 0D, credit = 0D
            For Each r As DataRow In CustomerService.Statement(ckey).Rows
                debit += GetDec(r, "debit") : credit += GetDec(r, "credit")
                doc.Rows.Add({Fmt.ShowDate(GetStr(r, "date")), GetStr(r, "description"), NumOrBlank(GetDec(r, "debit")), NumOrBlank(GetDec(r, "credit")), Fmt.Num(GetDec(r, "balance"))})
            Next
            doc.AddTotal("Total Purchases:", Fmt.Money(debit))
            doc.AddTotal("Total Paid:", Fmt.Money(credit))
            doc.AddTotal("Balance Due:", Fmt.Money(debit - credit))
            Return doc
        End Function

        ''' <summary>Prints any grid's visible columns as a report (A4, landscape when wide).</summary>
        Public Function FromGrid(title As String, subtitle As String, g As DataGridView, ParamArray totals As KeyValuePair(Of String, String)()) As PrintDoc
            Dim cols = g.Columns.Cast(Of DataGridViewColumn)().Where(Function(c) c.Visible).OrderBy(Function(c) c.DisplayIndex).ToList()
            Dim doc As New PrintDoc With {.Title = title, .DocDate = Date.Today.ToString("dd MMM yyyy", Fmt.Inv), .Format = "a4", .Landscape = cols.Count > 6, .Footer = " "}
            If subtitle <> "" Then doc.PartyLines.Add(subtitle)
            For Each c In cols
                Dim align = StringAlignment.Near
                If c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight Then align = StringAlignment.Far
                If c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter Then align = StringAlignment.Center
                doc.Columns.Add(New PrintColumn(c.HeaderText, Math.Max(1.0F, c.FillWeight / 50.0F), align))
            Next
            For Each row As DataGridViewRow In g.Rows
                doc.Rows.Add(cols.Select(Function(c) Convert.ToString(row.Cells(c.Index).FormattedValue, Fmt.Inv)).ToArray())
            Next
            For Each t In totals
                doc.Totals.Add(t)
            Next
            Return doc
        End Function

        Public Function KV(label As String, value As String) As KeyValuePair(Of String, String)
            Return New KeyValuePair(Of String, String)(label, value)
        End Function

        Private Function NumOrBlank(v As Decimal) As String
            Return If(v = 0, "", Fmt.Num(v))
        End Function

    End Module

End Namespace
