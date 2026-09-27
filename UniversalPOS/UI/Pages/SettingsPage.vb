Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.Services

Namespace UI.Pages

    Public Class SettingsPage
        Inherits PageBase

        Private ReadOnly _onChanged As Action
        Private ReadOnly _tabs As New TabControl With {.Dock = DockStyle.Fill}

        ' profile
        Private ReadOnly _shop As TextBox = Ui.Txt(320)
        Private ReadOnly _owner As TextBox = Ui.Txt(320)
        Private ReadOnly _phone As TextBox = Ui.Txt(220)
        Private ReadOnly _email As TextBox = Ui.Txt(320)
        Private ReadOnly _address As TextBox = Ui.Txt(420)
        Private ReadOnly _slogan As TextBox = Ui.Txt(420)
        Private ReadOnly _currency As TextBox = Ui.Txt(80)
        Private ReadOnly _logo As New PictureBox With {.Size = New Size(160, 70), .SizeMode = PictureBoxSizeMode.Zoom, .BorderStyle = BorderStyle.FixedSingle, .BackColor = Color.White}
        Private ReadOnly _contacts As DataGridView = Ui.Grid()
        Private ReadOnly _contactTable As New DataTable()

        ' invoice / display
        Private ReadOnly _format As ComboBox = Ui.Combo(220)
        Private ReadOnly _watermark As TextBox = Ui.Txt(220)
        Private ReadOnly _footer As TextBox = Ui.Txt(420)
        Private ReadOnly _directPrint As New CheckBox With {.Text = "Print invoices directly to the default printer (skip preview)", .AutoSize = True}
        Private ReadOnly _lowStock As NumericUpDown = Ui.NumBox(0, 90, 100000)
        Private ReadOnly _fontScale As NumericUpDown = Ui.NumBox(0, 90, 150)

        ' users / data
        Private ReadOnly _users As DataGridView = Ui.Grid()
        Private ReadOnly _history As DataGridView = Ui.Grid()
        Private ReadOnly _backups As DataGridView = Ui.Grid()

        Public Sub New(onChanged As Action)
            MyBase.New("Settings", "Shop profile, invoices, users and backups")
            _onChanged = onChanged
            _format.Items.AddRange({"A4", "Half A4 (A5)", "Thermal receipt (80mm)"})
            _fontScale.Minimum = 80
            _fontScale.Increment = 5

            _tabs.TabPages.Add(BuildProfileTab())
            _tabs.TabPages.Add(BuildInvoiceTab())
            _tabs.TabPages.Add(BuildUsersTab())
            _tabs.TabPages.Add(BuildDataTab())
            Body.Controls.Add(_tabs)
        End Sub

        Private Shared Function FormTable() As TableLayoutPanel
            Dim t As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Dock = DockStyle.Top, .Padding = New Padding(16, 14, 16, 8)}
            t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            Return t
        End Function

        Private Shared Sub Row(t As TableLayoutPanel, label As String, ctl As Control)
            Dim l = Ui.Lbl(label, 9.5F, FontStyle.Regular, Theme.Muted)
            l.Margin = New Padding(0, 8, 14, 4)
            t.RowCount += 1
            t.Controls.Add(l, 0, t.RowCount - 1)
            t.Controls.Add(ctl, 1, t.RowCount - 1)
        End Sub

        Private Function BuildProfileTab() As TabPage
            Dim page As New TabPage("Shop Profile") With {.BackColor = Theme.Card, .AutoScroll = True}
            Dim t = FormTable()
            Row(t, "Shop name *", _shop)
            Row(t, "Owner", _owner)
            Row(t, "Phone", _phone)
            Row(t, "Email", _email)
            Row(t, "Address", _address)
            Row(t, "Slogan / tagline", _slogan)
            Row(t, "Currency symbol", _currency)
            Dim logoRow = Ui.Flow(False)
            logoRow.Controls.Add(_logo)
            logoRow.Controls.Add(Ui.Btn("Choose logo...", BtnKind.Secondary, AddressOf PickLogo))
            logoRow.Controls.Add(Ui.Btn("Remove", BtnKind.Secondary, Sub()
                                                                       AppSettings.SetValue("logo_path", "")
                                                                       LoadLogo()
                                                                   End Sub))
            Row(t, "Logo (on invoices)", logoRow)

            _contactTable.Columns.Add("Label", GetType(String))
            _contactTable.Columns.Add("Phone", GetType(String))
            _contacts.ReadOnly = False
            _contacts.AllowUserToAddRows = True
            _contacts.AllowUserToDeleteRows = True
            _contacts.SelectionMode = DataGridViewSelectionMode.CellSelect
            _contacts.Dock = DockStyle.None
            _contacts.Size = New Size(420, 130)
            _contacts.BorderStyle = BorderStyle.FixedSingle
            _contacts.DataSource = _contactTable
            Row(t, "Extra contact numbers", _contacts)

            Dim save = Ui.Btn("Save Profile", BtnKind.Primary, AddressOf SaveProfile)
            Row(t, "", save)
            page.Controls.Add(t)
            Return page
        End Function

        Private Function BuildInvoiceTab() As TabPage
            Dim page As New TabPage("Invoice && Display") With {.BackColor = Theme.Card, .AutoScroll = True}
            Dim t = FormTable()
            Row(t, "Default invoice format", _format)
            Row(t, "Watermark text", _watermark)
            Row(t, "Invoice footer", _footer)
            Row(t, "Printing", _directPrint)
            Row(t, "Low stock alert at (units)", _lowStock)
            Row(t, "Text size (%)", _fontScale)
            Dim note = Ui.Lbl("Tip: to save an invoice as PDF, choose ""Microsoft Print to PDF"" as the printer." & Environment.NewLine &
                              "Text size changes apply after restarting the app.", 9, FontStyle.Italic, Theme.Muted)
            Row(t, "", note)
            Row(t, "", Ui.Btn("Save", BtnKind.Primary, AddressOf SaveInvoice))
            page.Controls.Add(t)
            Return page
        End Function

        Private Function BuildUsersTab() As TabPage
            Dim page As New TabPage("Users") With {.BackColor = Theme.Card}
            Dim bar = Ui.Flow()
            bar.Dock = DockStyle.Top
            bar.Padding = New Padding(10)
            bar.Controls.Add(Ui.Btn("+ Add User", BtnKind.Primary, Sub() EditUser(0)))
            bar.Controls.Add(Ui.Btn("Edit", BtnKind.Secondary, Sub() EditUser(Ui.SelectedRowId(_users))))
            bar.Controls.Add(Ui.Btn("Delete", BtnKind.Danger, AddressOf DeleteUser))
            bar.Controls.Add(Ui.Btn("Change My Password", BtnKind.Secondary, AddressOf ChangePassword))
            Dim note = Ui.Lbl("Admin: full access.   Cashier: sales, quotations, products, repairs - no reports, costs, vendors, expenses or settings.", 9, FontStyle.Italic, Theme.Muted)
            note.Dock = DockStyle.Bottom
            note.Padding = New Padding(10, 6, 0, 8)
            page.Controls.Add(_users)
            page.Controls.Add(note)
            page.Controls.Add(bar)
            AddHandler _users.CellDoubleClick, Sub(s, e)
                                                   If e.RowIndex >= 0 Then EditUser(Ui.SelectedRowId(_users))
                                               End Sub
            Return page
        End Function

        Private Function BuildDataTab() As TabPage
            Dim page As New TabPage("Backup && Data") With {.BackColor = Theme.Card}
            Dim bar = Ui.Flow()
            bar.Dock = DockStyle.Top
            bar.Padding = New Padding(10)
            bar.Controls.Add(Ui.Btn("Backup Now...", BtnKind.Primary, AddressOf BackupNow))
            bar.Controls.Add(Ui.Btn("Restore from Backup...", BtnKind.Warning, AddressOf Restore))
            bar.Controls.Add(Ui.Btn("Open Data Folder", BtnKind.Secondary, Sub() Process.Start(New ProcessStartInfo(AppPaths.DataDir) With {.UseShellExecute = True})))
            Dim info = Ui.Lbl("Database: " & AppPaths.DatabaseFile & Environment.NewLine &
                              "An automatic backup is made every day the app is opened (last 30 kept). Copy backups to a USB / cloud drive regularly.", 9, FontStyle.Regular, Theme.Muted)
            info.Dock = DockStyle.Top
            info.Padding = New Padding(10, 0, 0, 8)
            Dim split As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2}
            split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            Dim c1 = CardPanel("Automatic backups")
            c1.Controls.Add(_backups)
            _backups.BringToFront()
            Dim c2 = CardPanel("Login history")
            c2.Controls.Add(_history)
            _history.BringToFront()
            split.Controls.Add(c1, 0, 0)
            split.Controls.Add(c2, 1, 0)
            page.Controls.Add(split)
            page.Controls.Add(info)
            page.Controls.Add(bar)
            Return page
        End Function

        Public Overrides Sub RefreshData()
            AppSettings.Reload()
            _shop.Text = AppSettings.ShopName
            _owner.Text = AppSettings.OwnerName
            _phone.Text = AppSettings.Phone
            _email.Text = AppSettings.Email
            _address.Text = AppSettings.Address
            _slogan.Text = AppSettings.Slogan
            _currency.Text = AppSettings.CurrencySymbol
            LoadLogo()
            _contactTable.Rows.Clear()
            For Each r As DataRow In Data.Db.Query("SELECT label, phone FROM shop_contact_numbers ORDER BY id").Rows
                _contactTable.Rows.Add(GetStr(r, "label"), GetStr(r, "phone"))
            Next
            _format.SelectedIndex = Array.IndexOf({"a4", "half_a4", "thermal"}, AppSettings.InvoiceFormat)
            If _format.SelectedIndex < 0 Then _format.SelectedIndex = 0
            _watermark.Text = AppSettings.WatermarkText
            _footer.Text = AppSettings.InvoiceFooter
            _directPrint.Checked = AppSettings.GetValue("direct_print") = "1"
            _lowStock.Value = AppSettings.LowStockThreshold
            _fontScale.Value = AppSettings.FontScale
            Ui.ShowTable(_users, UserService.Users(), "username=Username", "full_name=Name", "role=Role", "status=Status", "created_at=Created")
            Ui.ShowTable(_history, UserService.LoginHistory(), "logged_in_at=Time", "username=User", "machine_name=Computer")
            Dim bk As New DataTable()
            bk.Columns.Add("file", GetType(String))
            bk.Columns.Add("size", GetType(String))
            For Each f In New DirectoryInfo(AppPaths.BackupsDir).GetFiles("*.db").OrderByDescending(Function(x) x.Name)
                bk.Rows.Add(f.Name, (f.Length / 1024.0).ToString("N0") & " KB")
            Next
            Ui.ShowTable(_backups, bk, "file=File", "size=Size")
        End Sub

        Private Sub LoadLogo()
            If _logo.Image IsNot Nothing Then
                _logo.Image.Dispose()
                _logo.Image = Nothing
            End If
            Dim f = AppSettings.LogoFile
            If f <> "" Then
                Using fs As New FileStream(f, FileMode.Open, FileAccess.Read)
                    _logo.Image = Image.FromStream(fs)
                End Using
            End If
        End Sub

        Private Sub PickLogo()
            Using dlg As New OpenFileDialog With {.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"}
                If dlg.ShowDialog() <> DialogResult.OK Then Return
                Ui.Attempt(Sub()
                               AppSettings.SetValue("logo_path", AppPaths.StoreFile(dlg.FileName, "logo", "logo"))
                               LoadLogo()
                           End Sub)
            End Using
        End Sub

        Private Sub SaveProfile()
            If _shop.Text.Trim() = "" Then
                Ui.Warn("Shop name is required.")
                Return
            End If
            Ui.CommitGrid(_contacts)
            If Ui.Attempt(Sub()
                              AppSettings.SetValue("shop_name", _shop.Text.Trim())
                              AppSettings.SetValue("owner_name", _owner.Text.Trim())
                              AppSettings.SetValue("phone", _phone.Text.Trim())
                              AppSettings.SetValue("email", _email.Text.Trim())
                              AppSettings.SetValue("address", _address.Text.Trim())
                              AppSettings.SetValue("slogan", _slogan.Text.Trim())
                              AppSettings.SetValue("currency_symbol", If(_currency.Text.Trim() = "", "Rs.", _currency.Text.Trim()))
                              Data.Db.Tx(Sub(s)
                                             s.Exec("DELETE FROM shop_contact_numbers")
                                             For Each r As DataRow In _contactTable.Rows
                                                 If r.RowState = DataRowState.Deleted Then Continue For
                                                 Dim ph = GetStr(r, "Phone").Trim()
                                                 If ph <> "" Then s.Exec("INSERT INTO shop_contact_numbers (label, phone) VALUES (@p0,@p1)", NullIfEmpty(GetStr(r, "Label")), ph)
                                             Next
                                         End Sub)
                          End Sub) Then
                _onChanged?.Invoke()
                Ui.Info("Shop profile saved.")
            End If
        End Sub

        Private Sub SaveInvoice()
            If Ui.Attempt(Sub()
                              AppSettings.SetValue("invoice_format", {"a4", "half_a4", "thermal"}(Math.Max(0, _format.SelectedIndex)))
                              AppSettings.SetValue("watermark_text", _watermark.Text.Trim())
                              AppSettings.SetValue("invoice_footer", _footer.Text.Trim())
                              AppSettings.SetValue("direct_print", If(_directPrint.Checked, "1", "0"))
                              AppSettings.SetValue("low_stock_threshold", CInt(_lowStock.Value).ToString())
                              AppSettings.SetValue("ui_font_scale", CInt(_fontScale.Value).ToString())
                          End Sub) Then
                Ui.Info("Settings saved.")
            End If
        End Sub

        Private Sub EditUser(id As Long)
            Using dlg As New UserDialog(id)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then RefreshData()
            End Using
        End Sub

        Private Sub DeleteUser()
            Dim r = Ui.SelectedRow(_users)
            If r Is Nothing Then Return
            If Not Ui.Confirm("Delete user """ & GetStr(r, "username") & """?") Then Return
            If Ui.Attempt(Sub() UserService.DeleteUser(GetLng(r, "id"))) Then RefreshData()
        End Sub

        Private Sub ChangePassword()
            Using dlg As New ChangePasswordDialog()
                dlg.ShowDialog(FindForm())
            End Using
        End Sub

        Private Sub BackupNow()
            Using dlg As New SaveFileDialog With {.Filter = "POS backup (*.db)|*.db", .FileName = "pos_backup_" & Date.Now.ToString("yyyy-MM-dd_HHmm") & ".db"}
                If dlg.ShowDialog() <> DialogResult.OK Then Return
                If Ui.Attempt(Sub() Data.Db.BackupTo(dlg.FileName)) Then Ui.Info("Backup saved:" & Environment.NewLine & dlg.FileName)
            End Using
        End Sub

        Private Sub Restore()
            Using dlg As New OpenFileDialog With {.Filter = "POS backup (*.db)|*.db", .InitialDirectory = AppPaths.BackupsDir}
                If dlg.ShowDialog() <> DialogResult.OK Then Return
                If Not Ui.Confirm("Restore will REPLACE all current data with the backup:" & Environment.NewLine & dlg.FileName & Environment.NewLine & Environment.NewLine &
                                  "A safety copy of the current data is saved first. Continue?") Then Return
                If Ui.Attempt(Sub()
                                  Data.Db.BackupTo(Path.Combine(AppPaths.BackupsDir, "before_restore_" & Date.Now.ToString("yyyyMMdd_HHmmss") & ".db"))
                                  Data.Db.RestoreFrom(dlg.FileName)
                                  AppSettings.Reload()
                              End Sub) Then
                    Ui.Info("Data restored. The app will now close - please open it again.")
                    Application.Exit()
                End If
            End Using
        End Sub

    End Class

    Public Class UserDialog
        Inherits DialogBase

        Private ReadOnly _id As Long
        Private ReadOnly _username As TextBox = Ui.Txt(220)
        Private ReadOnly _name As TextBox = Ui.Txt(260)
        Private ReadOnly _role As ComboBox = Ui.Combo(140)
        Private ReadOnly _active As New CheckBox With {.Text = "Active (can sign in)", .Checked = True, .AutoSize = True}
        Private ReadOnly _pass As TextBox = Ui.Txt(220)

        Public Sub New(id As Long)
            MyBase.New(If(id > 0, "Edit User", "Add User"), 460)
            _id = id
            _role.Items.AddRange({"admin", "cashier"})
            _role.SelectedIndex = 1
            _pass.UseSystemPasswordChar = True
            AddField("Username", _username, True)
            AddField("Full name", _name)
            AddField("Role", _role)
            AddField("", _active)
            AddField(If(id > 0, "New password", "Password"), _pass, id = 0)
            If id > 0 Then
                AddNote("Leave the password empty to keep the current one.")
                Dim r = Data.Db.QueryRow("SELECT * FROM users WHERE id = @p0", id)
                _username.Text = GetStr(r, "username")
                _name.Text = GetStr(r, "full_name")
                _role.SelectedItem = GetStr(r, "role")
                _active.Checked = GetLng(r, "is_active") = 1
            End If
        End Sub

        Protected Overrides Sub OnSave()
            If Ui.Attempt(Sub() UserService.SaveUser(_id, _username.Text, _name.Text, CStr(_role.SelectedItem), _active.Checked, _pass.Text)) Then Done()
        End Sub

    End Class

    Public Class ChangePasswordDialog
        Inherits DialogBase

        Private ReadOnly _current As TextBox = Ui.Txt(220)
        Private ReadOnly _new1 As TextBox = Ui.Txt(220)
        Private ReadOnly _new2 As TextBox = Ui.Txt(220)

        Public Sub New()
            MyBase.New("Change My Password", 440)
            For Each tb In {_current, _new1, _new2}
                tb.UseSystemPasswordChar = True
            Next
            AddField("Current password", _current, True)
            AddField("New password", _new1, True)
            AddField("Confirm new password", _new2, True)
        End Sub

        Protected Overrides Sub OnSave()
            If _new1.Text <> _new2.Text Then
                Ui.Warn("New passwords do not match.")
                Return
            End If
            If Ui.Attempt(Sub() UserService.ChangeOwnPassword(_current.Text, _new1.Text)) Then
                Ui.Info("Password changed.")
                Done()
            End If
        End Sub

    End Class

End Namespace
