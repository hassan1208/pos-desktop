# Universal POS (Desktop, VB.NET)

A complete offline point-of-sale and shop management app for Windows, written in
**VB.NET (WinForms, .NET 8)** with a built-in **SQLite** database. It is a desktop
port of the Universal POS web app (PHP/MySQL). One `.exe` runs on the client's PC
with nothing else to install: no XAMPP, no MySQL, no internet.

## Features

| Area | What it does |
|---|---|
| **Dashboard** | Today's sales and profit, month totals, net profit, stock value, low stock, vendor payables, shopkeeper balances, 7-day sales chart, recent sales |
| **New Sale (POS)** | Barcode scanning or name search, cart with editable qty and price, discount, cash received and change, stock check. F9 saves, F10 saves and prints |
| **Sales History** | Date presets, search by customer, phone, product or invoice number, item view, edit (stock is re-balanced), delete (stock is restored), reprint invoice, CSV and print |
| **Quotations** | Catalog or custom items, status (draft, sent, accepted, rejected), print, convert to a sale (checks stock) |
| **Products** | Categories, barcode, cost, sale price and auto profit %, stock, per-category custom fields (Size, Color, IMEI…), stock adjustment, low-stock highlight, print and CSV |
| **Categories** | Custom fields per category, with a "required" option for each field |
| **Vendors & Purchases** | Multi-line purchase bills. A product with an existing name is restocked; a new name creates a new product. Supports full, partial and credit payment, bill photo, payments against one bill or spread over the oldest bills, ledger and printable statement |
| **Customers (Udhaar)** | Credit sales: tick "Credit sale" on the sale screen, record what was paid, and the rest goes on the customer's account. Payments received are applied to the oldest unpaid invoices. Includes a ledger, a printable customer statement, and Paid / Balance Due on the invoice |
| **Shopkeepers Ledger** | Two-way running account with nearby shops. You can give or receive products or cash. Giving a product deducts stock and creates a sale; receiving one adds stock. You can settle all or part of an entry on the spot, record returns, and print a statement |
| **Expenses** | Expense categories, date filter, print and CSV |
| **Maintenance / Repairs** | Repair jobs with parts cost and customer charge, which gives the profit. Prints a receipt |
| **Reports** | Profit & loss for any period (sales, cost of goods, discounts, repair profit, expenses, net profit), sales by category, top products, daily, expense breakdown, monthly and yearly (best month highlighted) |
| **Printing** | A4, Half-A4 (A5) or 80 mm thermal receipt. Includes logo, slogan, address, contact numbers, watermark and footer. Choose "Microsoft Print to PDF" to save as PDF |
| **Users** | Admin and Cashier roles. Cashiers cannot see costs, profit, reports, vendors, expenses or settings. Passwords are hashed with PBKDF2. Login history is kept |
| **Backup** | Automatic daily backup (the last 30 are kept), Backup Now and Restore. A safety copy is taken before every restore |

Keyboard: **F2** New Sale · **F3** Products · **F4** Sales History · **F5** Refresh · **F9 / F10** Save / Save & Print (in a sale) · **Ctrl+F** product search.

## Build the EXE for a client

### Option 1: Visual Studio (Windows)
1. Install **Visual Studio 2022** (Community is free) with the **.NET desktop development** workload.
2. Open `UniversalPOS.sln` and press **F5** to run.
3. To make the client exe, open a terminal in the repo folder and run:
   ```
   dotnet publish UniversalPOS\UniversalPOS.vbproj -c Release -o publish
   ```
   This produces **`publish\UniversalPOS.exe`**, a single self-contained file (about 60–70 MB). Copy it to the client's PC and run it. The client does not need to install .NET.

### Option 2: Setup installer
Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), publish as above, then run:
```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\UniversalPOS.iss
```
This produces `dist\UniversalPOS-Setup-1.0.0.exe`, which adds Start menu and desktop shortcuts and an uninstaller.

### Option 3: GitHub Actions (no Windows PC needed)
Every push runs `.github/workflows/build.yml` on a Windows runner. It runs the tests, then
builds the exe and the installer. Download them from the run's **Artifacts** section.
If you push a tag like `v1.0.0`, both files are also attached to a GitHub Release.

## First run
1. A **Shop Setup** screen asks for the shop name, currency and the admin username and password.
2. Sign in, then open **Settings** to add a logo, slogan, extra phone numbers and the invoice format (A4, A5 or thermal).
3. Add **Categories**, then **Products**, or record a **Purchase Bill** from a vendor, which creates the products and their stock.
4. Start selling from **New Sale (F2)**.

## Where the data is stored
- Default: `%LOCALAPPDATA%\UniversalPOS\` holds `pos.db` (the database), the `backups\`, `bills\` and `logo\` folders, and `error.log`.
- **Portable mode**: create a folder named `data` next to `UniversalPOS.exe`, and all data is kept there instead. This is useful on a USB drive.
- To move a shop to a new PC, go to **Settings → Backup & Data → Backup Now**, then **Restore** that file on the new PC.

## Project layout
```
UniversalPOS/            WinForms app (VB.NET)
  Core/                  settings, formatting, password hashing, session
  Data/                  SQLite access (Db.vb) and schema (Schema.vb)
  Services/              business logic: sales, inventory, purchases, shopkeepers, quotations, expenses, repairs, reports, users
  Printing/              print engine (A4 / A5 / thermal) and document builders
  UI/                    main window, login/setup, shared controls
  UI/Pages/              one file per screen (+ its dialogs)
tests/LogicTests/        console test runner for the business logic (runs on any OS)
installer/               Inno Setup script
```
All screens are built in code (no designer files), so they compile the same on every machine.

Run the logic tests: `dotnet run --project tests/LogicTests/LogicTests.vbproj`.

## Differences from the web version
- Single shop per install. The multi-tenant admin panel, shop approval, email OTP and public share links are removed because the app runs offline.
- New: customer credit (udhaar khata), barcode support, discounts, cash and change, cashier role, stock adjustment, thermal receipts, automatic backups, and a full payment trail in the vendor statement.
- Deleting a category is blocked while it still has products. On the web, this deleted the products and their sales lines.
