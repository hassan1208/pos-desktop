Imports System.Drawing
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Services

Namespace UI

    Public Class LoginForm
        Inherits Form

        Private ReadOnly _user As TextBox
        Private ReadOnly _pass As TextBox

        Public Sub New()
            Text = "Universal POS - Sign in"
            Font = Theme.BaseFont
            FormBorderStyle = FormBorderStyle.FixedSingle
            MaximizeBox = False
            StartPosition = FormStartPosition.CenterScreen
            BackColor = Theme.Card
            ClientSize = New Size(760, 440)

            Dim side As New Panel With {.Dock = DockStyle.Left, .Width = 330, .BackColor = Theme.Sidebar}
            AddHandler side.Paint, Sub(s, e)
                                       Using b As New Drawing2D.LinearGradientBrush(side.ClientRectangle, Theme.Primary, Theme.Sidebar, 60.0F)
                                           e.Graphics.FillRectangle(b, side.ClientRectangle)
                                       End Using
                                   End Sub
            Dim brand = Ui.Lbl("Universal POS", 22, FontStyle.Bold, Color.White)
            brand.BackColor = Color.Transparent
            brand.Location = New Point(30, 150)
            Dim shop = Ui.Lbl(AppSettings.ShopName, 12, FontStyle.Regular, Color.FromArgb(219, 234, 254))
            shop.BackColor = Color.Transparent
            shop.Location = New Point(33, 200)
            shop.MaximumSize = New Size(280, 0)
            Dim tag = Ui.Lbl("Sales  •  Stock  •  Vendors  •  Reports", 9, FontStyle.Regular, Color.FromArgb(191, 219, 254))
            tag.BackColor = Color.Transparent
            tag.Location = New Point(33, 380)
            side.Controls.AddRange({brand, shop, tag})

            Dim form As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(50, 70, 50, 20)}
            Dim title = Ui.Lbl("Sign in", 18, FontStyle.Bold)
            title.Location = New Point(50, 70)
            Dim l1 = Ui.Lbl("Username", 9.5F, FontStyle.Regular, Theme.Muted)
            l1.Location = New Point(52, 135)
            _user = Ui.Txt(320)
            _user.Location = New Point(52, 158)
            Dim l2 = Ui.Lbl("Password", 9.5F, FontStyle.Regular, Theme.Muted)
            l2.Location = New Point(52, 200)
            _pass = Ui.Txt(320)
            _pass.UseSystemPasswordChar = True
            _pass.Location = New Point(52, 223)
            Dim go = Ui.Btn("  Sign in  ", BtnKind.Primary, AddressOf DoLogin)
            go.AutoSize = False
            go.Size = New Size(320, 40)
            go.Location = New Point(52, 272)
            Dim hint = Ui.Lbl("Data folder: " & AppPaths.DataDir, 8, FontStyle.Regular, Theme.Muted)
            hint.MaximumSize = New Size(360, 0)
            hint.Location = New Point(52, 380)
            form.Controls.AddRange({title, l1, _user, l2, _pass, go, hint})

            Controls.Add(form)
            Controls.Add(side)
            AcceptButton = go
            _user.Text = AppSettings.GetValue("last_username")
            AddHandler Shown, Sub()
                                  If _user.Text = "" Then _user.Focus() Else _pass.Focus()
                              End Sub
        End Sub

        Private Sub DoLogin()
            If UserService.Login(_user.Text, _pass.Text) Then
                AppSettings.SetValue("last_username", Session.UserName)
                DialogResult = DialogResult.OK
                Close()
            Else
                Ui.Warn("Wrong username or password.")
                _pass.SelectAll()
                _pass.Focus()
            End If
        End Sub

    End Class

    ''' <summary>First-run wizard: shop profile + the first admin account.</summary>
    Public Class SetupForm
        Inherits DialogBase

        Private ReadOnly _shop As TextBox = Ui.Txt(300)
        Private ReadOnly _owner As TextBox = Ui.Txt(300)
        Private ReadOnly _phone As TextBox = Ui.Txt(300)
        Private ReadOnly _address As TextBox = Ui.Txt(300)
        Private ReadOnly _currency As TextBox = Ui.Txt(80)
        Private ReadOnly _user As TextBox = Ui.Txt(200)
        Private ReadOnly _pass As TextBox = Ui.Txt(200)
        Private ReadOnly _pass2 As TextBox = Ui.Txt(200)
        Private ReadOnly _defaults As New CheckBox With {.Text = "Create starter categories (General, Rent, Electricity, Salaries...)", .Checked = True, .AutoSize = True}

        Public Sub New()
            MyBase.New("Welcome to Universal POS - Shop Setup", 560)
            StartPosition = FormStartPosition.CenterScreen
            ShowInTaskbar = True
            _currency.Text = "Rs."
            _user.Text = "admin"
            _pass.UseSystemPasswordChar = True
            _pass2.UseSystemPasswordChar = True
            AddField("Shop name", _shop, True)
            AddField("Owner name", _owner)
            AddField("Phone", _phone)
            AddField("Address", _address)
            AddField("Currency symbol", _currency)
            AddNote("Admin account (used to sign in):")
            AddField("Username", _user, True)
            AddField("Password", _pass, True)
            AddField("Confirm password", _pass2, True)
            AddField("", _defaults)
            SaveButton.Text = "Start"
        End Sub

        Protected Overrides Sub OnSave()
            If _shop.Text.Trim() = "" Then Ui.Warn("Please enter the shop name.") : Return
            If _pass.Text <> _pass2.Text Then Ui.Warn("Passwords do not match.") : Return
            If Ui.Attempt(Sub()
                              If Not UserService.HasUsers() Then UserService.SaveUser(0, _user.Text, _owner.Text, "admin", True, _pass.Text)
                              AppSettings.SetValue("shop_name", _shop.Text.Trim())
                              AppSettings.SetValue("owner_name", _owner.Text.Trim())
                              AppSettings.SetValue("phone", _phone.Text.Trim())
                              AppSettings.SetValue("address", _address.Text.Trim())
                              AppSettings.SetValue("currency_symbol", If(_currency.Text.Trim() = "", "Rs.", _currency.Text.Trim()))
                              If _defaults.Checked Then
                                  If InventoryService.Categories().Rows.Count = 0 Then InventoryService.SaveCategory(0, "General", New List(Of CategoryFieldDef))
                                  If ExpenseService.Categories().Rows.Count = 0 Then
                                      For Each n In {"Rent", "Electricity", "Salaries", "Tea / Food", "Transport", "Other"}
                                          ExpenseService.SaveCategory(0, n)
                                      Next
                                  End If
                              End If
                              AppSettings.SetValue("setup_done", "1")
                              AppSettings.SetValue("last_username", _user.Text.Trim())
                          End Sub) Then
                Done()
            End If
        End Sub

    End Class

End Namespace
