Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.UI.Pages

Namespace UI

    Public Class MainForm
        Inherits Form

        Private ReadOnly _sidebar As FlowLayoutPanel
        Private ReadOnly _content As Panel
        Private ReadOnly _pages As New Dictionary(Of String, PageBase)
        Private ReadOnly _navButtons As New Dictionary(Of String, Button)
        Private ReadOnly _factories As New Dictionary(Of String, Func(Of PageBase))
        Private ReadOnly _clock As Label
        Private ReadOnly _shopLabel As Label
        Private ReadOnly _quickSale As Button
        Private _current As String = ""

        Public Property LoggedOut As Boolean

        Public Sub New()
            Text = "Universal POS - " & AppSettings.ShopName
            Font = Theme.BaseFont
            WindowState = FormWindowState.Maximized
            MinimumSize = New Size(1100, 680)
            StartPosition = FormStartPosition.CenterScreen
            BackColor = Theme.Background
            KeyPreview = True
            If Theme.AppIcon IsNot Nothing Then Icon = Theme.AppIcon

            ' ---- sidebar ----
            Dim side As New Panel With {.Dock = DockStyle.Left, .Width = CInt(215 * Theme.Fnt(10).Size / 10), .BackColor = Theme.Sidebar}
            Dim brand As New Label With {
                .Text = "  Universal POS", .Dock = DockStyle.Top, .Height = 58, .ForeColor = Color.White,
                .Font = Theme.Fnt(14, FontStyle.Bold), .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.FromArgb(3, 7, 18)
            }
            _sidebar = New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoScroll = True, .Padding = New Padding(8, 8, 0, 8)}
            Dim userBox As New Label With {
                .Dock = DockStyle.Bottom, .Height = 48, .ForeColor = Color.FromArgb(156, 163, 175), .Font = Theme.Fnt(8.5F),
                .Text = "  Signed in: " & Session.UserName & Environment.NewLine & "  Role: " & Session.Role, .TextAlign = ContentAlignment.MiddleLeft
            }
            side.Controls.Add(_sidebar)
            side.Controls.Add(userBox)
            side.Controls.Add(brand)

            ' ---- top bar ----
            Dim top As New Panel With {.Dock = DockStyle.Top, .Height = 46, .BackColor = Theme.Card}
            AddHandler top.Paint, Sub(s, e)
                                      Using p As New Pen(Theme.Border)
                                          e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1)
                                      End Using
                                  End Sub
            _shopLabel = Ui.Lbl(AppSettings.ShopName, 11, FontStyle.Bold)
            _shopLabel.Location = New Point(18, 12)
            _clock = Ui.Lbl("", 9.5F, FontStyle.Regular, Theme.Muted)
            _clock.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _quickSale = Ui.Btn("+ New Sale (F2)", BtnKind.Success, Sub() Navigate("sale"))
            Dim quickSale = _quickSale
            quickSale.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            Dim logout = Ui.Btn("Log out", BtnKind.Secondary, AddressOf DoLogout)
            logout.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            top.Controls.AddRange({_shopLabel, _clock, quickSale, logout})
            AddHandler top.Resize, Sub()
                                       logout.Location = New Point(top.Width - logout.Width - 14, 8)
                                       quickSale.Location = New Point(logout.Left - quickSale.Width - 8, 8)
                                       _clock.Location = New Point(quickSale.Left - _clock.Width - 18, 14)
                                   End Sub

            _content = New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.Background}

            Controls.Add(_content)
            Controls.Add(top)
            Controls.Add(side)

            ' ---- navigation ----
            AddSection("MAIN")
            AddNav("dashboard", "Dashboard", Function() New DashboardPage(AddressOf Navigate))
            AddNav("sale", "New Sale  (F2)", Function() New SalePage())
            AddNav("sales", "Sales History  (F4)", Function() New SalesPage())
            AddNav("quotations", "Quotations", Function() New QuotationsPage())
            AddSection("INVENTORY")
            AddNav("products", "Products  (F3)", Function() New ProductsPage())
            AddNav("categories", "Categories", Function() New CategoriesPage())
            If Session.IsAdmin Then
                AddNav("vendors", "Vendors && Purchases", Function() New VendorsPage())
            End If
            AddSection("ACCOUNTS")
            AddNav("customers", "Customers (Udhaar)", Function() New CustomersPage())
            AddNav("shopkeepers", "Shopkeepers Ledger", Function() New ShopkeepersPage())
            If Session.IsAdmin Then AddNav("expenses", "Expenses", Function() New ExpensesPage())
            AddNav("maintenance", "Maintenance / Repairs", Function() New MaintenancePage())
            If Session.IsAdmin Then
                AddSection("ADMIN")
                AddNav("reports", "Reports", Function() New ReportsPage())
                AddNav("settings", "Settings", Function() New SettingsPage(AddressOf OnSettingsChanged))
            End If

            Dim timer As New Timer With {.Interval = 1000, .Enabled = True}
            AddHandler timer.Tick, Sub() UpdateClock()
            UpdateClock()

            AddHandler Load, Sub() Navigate("dashboard")
        End Sub

        Private Sub UpdateClock()
            _clock.Text = Date.Now.ToString("ddd, dd MMM yyyy   hh:mm tt", Fmt.Inv)
            _clock.Location = New Point(_quickSale.Left - _clock.Width - 18, 14)
        End Sub

        Private Sub AddSection(title As String)
            _sidebar.Controls.Add(New Label With {
                .Text = title, .ForeColor = Color.FromArgb(107, 114, 128), .Font = Theme.Fnt(7.5F, FontStyle.Bold),
                .AutoSize = False, .Width = _sidebar.Parent.Width - 16, .Height = 26, .TextAlign = ContentAlignment.BottomLeft,
                .Padding = New Padding(8, 0, 0, 2), .Margin = New Padding(0, 6, 0, 0)
            })
        End Sub

        Private Sub AddNav(key As String, text As String, factory As Func(Of PageBase))
            Dim b As New Button With {
                .Text = "   " & text, .TextAlign = ContentAlignment.MiddleLeft, .FlatStyle = FlatStyle.Flat,
                .ForeColor = Color.FromArgb(209, 213, 219), .BackColor = Theme.Sidebar, .Font = Theme.Fnt(9.75F),
                .Width = _sidebar.Parent.Width - 16, .Height = CInt(36 * Theme.Fnt(10).Size / 10), .Cursor = Cursors.Hand,
                .Margin = New Padding(0, 1, 0, 1), .UseVisualStyleBackColor = False
            }
            b.FlatAppearance.BorderSize = 0
            b.FlatAppearance.MouseOverBackColor = Theme.SidebarHover
            AddHandler b.Click, Sub() Navigate(key)
            _sidebar.Controls.Add(b)
            _navButtons(key) = b
            _factories(key) = factory
        End Sub

        Public Sub Navigate(key As String)
            If Not _factories.ContainsKey(key) Then Return
            Dim page As PageBase = Nothing
            If Not _pages.TryGetValue(key, page) Then
                page = _factories(key)()
                _pages(key) = page
                _content.Controls.Add(page)
            End If
            For Each kv In _navButtons
                Dim active = kv.Key = key
                kv.Value.BackColor = If(active, Theme.SidebarActive, Theme.Sidebar)
                kv.Value.ForeColor = If(active, Color.White, Color.FromArgb(209, 213, 219))
                kv.Value.FlatAppearance.MouseOverBackColor = If(active, Theme.SidebarActive, Theme.SidebarHover)
            Next
            _current = key
            page.BringToFront()
            page.Visible = True
            For Each other In _pages.Values
                If other IsNot page Then other.Visible = False
            Next
            Ui.Attempt(Sub() page.RefreshData())
        End Sub

        Private Sub OnSettingsChanged()
            _shopLabel.Text = AppSettings.ShopName
            Text = "Universal POS - " & AppSettings.ShopName
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.F2 : Navigate("sale") : Return True
                Case Keys.F3 : Navigate("products") : Return True
                Case Keys.F4 : Navigate("sales") : Return True
                Case Keys.F5
                    If _current <> "" Then Ui.Attempt(Sub() _pages(_current).RefreshData())
                    Return True
            End Select
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Private Sub DoLogout()
            If Not Ui.Confirm("Log out of Universal POS?") Then Return
            LoggedOut = True
            Close()
        End Sub

    End Class

End Namespace
