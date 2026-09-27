Namespace Data

    ''' <summary>Database schema (SQLite) and versioned migrations.</summary>
    Public Module Schema

        Public Const CurrentVersion As Integer = 1

        Private Const BaseSql As String = "
CREATE TABLE IF NOT EXISTS settings (
    setting_key   TEXT PRIMARY KEY,
    setting_value TEXT
);

CREATE TABLE IF NOT EXISTS users (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    username      TEXT NOT NULL UNIQUE COLLATE NOCASE,
    full_name     TEXT,
    password_hash TEXT NOT NULL,
    role          TEXT NOT NULL DEFAULT 'admin' CHECK (role IN ('admin','cashier')),
    is_active     INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS login_history (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id      INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    machine_name TEXT,
    logged_in_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS shop_contact_numbers (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    label TEXT,
    phone TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS categories (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS category_fields (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    field_name  TEXT NOT NULL,
    is_required INTEGER NOT NULL DEFAULT 0,
    field_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS vendors (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL,
    phone      TEXT,
    address    TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS products (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id    INTEGER NOT NULL REFERENCES categories(id),
    vendor_id      INTEGER REFERENCES vendors(id) ON DELETE SET NULL,
    product_name   TEXT NOT NULL,
    barcode        TEXT,
    purchase_price REAL NOT NULL DEFAULT 0,
    sale_price     REAL NOT NULL DEFAULT 0,
    profit_percent REAL NOT NULL DEFAULT 0,
    stock_quantity INTEGER NOT NULL DEFAULT 0,
    created_at     TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at     TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS idx_products_category ON products(category_id);
CREATE INDEX IF NOT EXISTS idx_products_barcode ON products(barcode);

CREATE TABLE IF NOT EXISTS product_field_values (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    product_id        INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    category_field_id INTEGER NOT NULL REFERENCES category_fields(id) ON DELETE CASCADE,
    field_value       TEXT
);

CREATE TABLE IF NOT EXISTS purchases (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    vendor_id      INTEGER NOT NULL REFERENCES vendors(id),
    bill_number    TEXT,
    purchase_date  TEXT NOT NULL,
    total_amount   REAL NOT NULL DEFAULT 0,
    paid_amount    REAL NOT NULL DEFAULT 0,
    balance_due    REAL NOT NULL DEFAULT 0,
    payment_type   TEXT NOT NULL DEFAULT 'credit' CHECK (payment_type IN ('full','partial','credit')),
    notes          TEXT,
    bill_image     TEXT,
    created_at     TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS idx_purchases_vendor ON purchases(vendor_id);

CREATE TABLE IF NOT EXISTS purchase_items (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    purchase_id    INTEGER NOT NULL REFERENCES purchases(id) ON DELETE CASCADE,
    category_id    INTEGER REFERENCES categories(id) ON DELETE SET NULL,
    product_id     INTEGER REFERENCES products(id) ON DELETE SET NULL,
    product_name   TEXT NOT NULL,
    quantity       INTEGER NOT NULL DEFAULT 1,
    purchase_price REAL NOT NULL DEFAULT 0,
    sale_price     REAL NOT NULL DEFAULT 0,
    profit_percent REAL NOT NULL DEFAULT 0,
    line_total     REAL NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS vendor_payments (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    vendor_id    INTEGER NOT NULL REFERENCES vendors(id) ON DELETE CASCADE,
    purchase_id  INTEGER REFERENCES purchases(id) ON DELETE SET NULL,
    amount       REAL NOT NULL,
    payment_date TEXT NOT NULL,
    notes        TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS sales (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    sale_date      TEXT NOT NULL,
    subtotal       REAL NOT NULL DEFAULT 0,
    discount       REAL NOT NULL DEFAULT 0,
    total_amount   REAL NOT NULL DEFAULT 0,
    total_profit   REAL NOT NULL DEFAULT 0,
    customer_name  TEXT,
    customer_phone TEXT,
    user_id        INTEGER REFERENCES users(id) ON DELETE SET NULL,
    created_at     TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS idx_sales_date ON sales(sale_date);

CREATE TABLE IF NOT EXISTS sale_items (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    sale_id        INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
    product_id     INTEGER REFERENCES products(id) ON DELETE SET NULL,
    category_id    INTEGER REFERENCES categories(id) ON DELETE SET NULL,
    product_name   TEXT NOT NULL,
    quantity       INTEGER NOT NULL DEFAULT 1,
    purchase_price REAL NOT NULL DEFAULT 0,
    sale_price     REAL NOT NULL DEFAULT 0,
    profit         REAL NOT NULL DEFAULT 0,
    line_total     REAL NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id);

CREATE TABLE IF NOT EXISTS expense_categories (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS expenses (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id  INTEGER NOT NULL REFERENCES expense_categories(id),
    amount       REAL NOT NULL DEFAULT 0,
    expense_date TEXT NOT NULL,
    notes        TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS maintenance_jobs (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_name    TEXT,
    customer_phone   TEXT,
    item_description TEXT NOT NULL,
    issue_notes      TEXT,
    total_amount     REAL NOT NULL DEFAULT 0,
    customer_charge  REAL NOT NULL DEFAULT 0,
    maintenance_date TEXT NOT NULL,
    created_at       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS maintenance_parts (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    maintenance_id INTEGER NOT NULL REFERENCES maintenance_jobs(id) ON DELETE CASCADE,
    description    TEXT NOT NULL,
    amount         REAL NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS shopkeeper_contacts (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL,
    phone      TEXT,
    address    TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS shopkeeper_transactions (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    contact_id       INTEGER NOT NULL REFERENCES shopkeeper_contacts(id) ON DELETE CASCADE,
    direction        TEXT NOT NULL CHECK (direction IN ('given','received')),
    entry_type       TEXT NOT NULL CHECK (entry_type IN ('product','cash')),
    product_id       INTEGER REFERENCES products(id) ON DELETE SET NULL,
    description      TEXT NOT NULL,
    amount           REAL NOT NULL DEFAULT 0,
    quantity         INTEGER,
    sale_id          INTEGER REFERENCES sales(id) ON DELETE SET NULL,
    transaction_date TEXT NOT NULL,
    notes            TEXT,
    returned         INTEGER NOT NULL DEFAULT 0,
    created_at       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS idx_sk_txn_contact ON shopkeeper_transactions(contact_id);

CREATE TABLE IF NOT EXISTS quotations (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_name     TEXT,
    customer_phone    TEXT,
    quote_date        TEXT NOT NULL,
    total_amount      REAL NOT NULL DEFAULT 0,
    status            TEXT NOT NULL DEFAULT 'draft' CHECK (status IN ('draft','sent','accepted','rejected','converted')),
    notes             TEXT,
    converted_sale_id INTEGER REFERENCES sales(id) ON DELETE SET NULL,
    created_at        TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS quotation_items (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    quotation_id INTEGER NOT NULL REFERENCES quotations(id) ON DELETE CASCADE,
    product_id   INTEGER REFERENCES products(id) ON DELETE SET NULL,
    category_id  INTEGER REFERENCES categories(id) ON DELETE SET NULL,
    product_name TEXT NOT NULL,
    quantity     INTEGER NOT NULL DEFAULT 1,
    sale_price   REAL NOT NULL DEFAULT 0,
    line_total   REAL NOT NULL DEFAULT 0
);
"

        Public Sub Apply()
            Using s = Db.Open()
                s.Exec(BaseSql)
                Dim version = s.ScalarLong("PRAGMA user_version")
                ' Future migrations go here:  If version < 2 Then ... : s.Exec("PRAGMA user_version = 2")
                If version < CurrentVersion Then
                    s.Exec("PRAGMA user_version = " & CurrentVersion)
                End If
            End Using
        End Sub

    End Module

End Namespace
