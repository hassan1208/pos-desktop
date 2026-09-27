Imports System.Drawing
Imports System.Windows.Forms

Namespace UI

    ''' <summary>
    ''' A screen hosted in the main window: a title row, a toolbar (buttons / filters)
    ''' and a body that fills the rest.
    ''' </summary>
    Public Class PageBase
        Inherits UserControl

        Protected ReadOnly TitleLabel As Label
        Protected ReadOnly SubtitleLabel As Label
        Protected ReadOnly Toolbar As FlowLayoutPanel
        Protected ReadOnly Body As Panel

        Public Sub New(title As String, Optional subtitle As String = "")
            Dock = DockStyle.Fill
            BackColor = Theme.Background
            Padding = New Padding(20, 16, 20, 16)
            Font = Theme.BaseFont

            Body = New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.Background}

            Toolbar = Ui.Flow()
            Toolbar.Dock = DockStyle.Top
            Toolbar.Padding = New Padding(0, 4, 0, 10)
            Toolbar.AutoSize = True

            Dim head As New Panel With {.Dock = DockStyle.Top, .Height = CInt(56 * Theme.Fnt(10).Size / 10)}
            TitleLabel = Ui.Lbl(title, 16, FontStyle.Bold)
            TitleLabel.Location = New Point(0, 0)
            SubtitleLabel = Ui.Lbl(subtitle, 9.5F, FontStyle.Regular, Theme.Muted)
            SubtitleLabel.Location = New Point(2, TitleLabel.PreferredHeight + 2)
            head.Controls.Add(TitleLabel)
            head.Controls.Add(SubtitleLabel)

            Controls.Add(Body)
            Controls.Add(Toolbar)
            Controls.Add(head)
        End Sub

        ''' <summary>Called when the page is shown (and after edits) to reload its data.</summary>
        Public Overridable Sub RefreshData()
        End Sub

        ''' <summary>A white rounded-looking card that holds a grid or content.</summary>
        Public Shared Function CardPanel(Optional title As String = "") As Panel
            Dim p As New Panel With {.BackColor = Theme.Card, .Padding = New Padding(1), .Dock = DockStyle.Fill}
            AddHandler p.Paint, Sub(s, e)
                                    Dim c = DirectCast(s, Control)
                                    Using pen As New Pen(Theme.Border)
                                        e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1)
                                    End Using
                                End Sub
            AddHandler p.Resize, Sub() p.Invalidate()
            If title <> "" Then
                Dim t = Ui.Lbl(title, 10.5F, FontStyle.Bold)
                t.Dock = DockStyle.Top
                t.AutoSize = False
                t.Height = CInt(34 * Theme.Fnt(10).Size / 10)
                t.Padding = New Padding(10, 8, 0, 0)
                t.Margin = New Padding(0)
                p.Controls.Add(t)
            End If
            Return p
        End Function

        ''' <summary>Adds a grid inside a card to the body (fills it).</summary>
        Protected Function AddGridCard(g As DataGridView, Optional title As String = "") As Panel
            Dim card = CardPanel(title)
            card.Controls.Add(g)
            g.BringToFront()
            Body.Controls.Add(card)
            Return card
        End Function

    End Class

    ''' <summary>
    ''' Modal dialog with a two-column (label | control) form layout and Save / Cancel buttons.
    ''' </summary>
    Public Class DialogBase
        Inherits Form

        Protected ReadOnly Fields As TableLayoutPanel
        Protected ReadOnly ButtonBar As FlowLayoutPanel
        Protected ReadOnly SaveButton As Button
        Protected ReadOnly CancelBtn As Button
        Protected ReadOnly Content As Panel

        Public Sub New(title As String, Optional width As Integer = 520, Optional height As Integer = 0)
            Text = title
            Font = Theme.BaseFont
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            StartPosition = FormStartPosition.CenterParent
            ShowInTaskbar = False
            ShowIcon = False
            BackColor = Theme.Card
            KeyPreview = True
            AutoScaleMode = AutoScaleMode.Font

            Fields = New TableLayoutPanel With {
                .Dock = DockStyle.Top,
                .AutoSize = True,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .ColumnCount = 2,
                .Padding = New Padding(16, 14, 16, 6)
            }
            Fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            Fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

            Content = New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 0, 16, 6), .BackColor = Theme.Card}

            ButtonBar = New FlowLayoutPanel With {
                .Dock = DockStyle.Bottom,
                .FlowDirection = FlowDirection.RightToLeft,
                .Height = 54,
                .Padding = New Padding(12, 10, 12, 10),
                .BackColor = Theme.Background
            }
            SaveButton = Ui.Btn("Save", BtnKind.Primary)
            CancelBtn = Ui.Btn("Cancel", BtnKind.Secondary)
            CancelBtn.DialogResult = DialogResult.Cancel
            AddHandler SaveButton.Click, Sub() OnSave()
            ButtonBar.Controls.Add(SaveButton)
            ButtonBar.Controls.Add(CancelBtn)
            CancelButton = CancelBtn

            Controls.Add(Content)
            Controls.Add(Fields)
            Controls.Add(ButtonBar)
            ' Tab / initial focus: the form fields first, then the content area, then the buttons.
            Fields.TabIndex = 0
            Content.TabIndex = 1
            ButtonBar.TabIndex = 2

            ClientSize = New Size(width, If(height > 0, height, 300))
            If height = 0 Then
                AddHandler Load, Sub()
                                     ' Size to the field layout when no explicit height was given.
                                     Dim h = Fields.PreferredSize.Height + ButtonBar.Height + Content.Padding.Vertical + ContentExtraHeight
                                     ClientSize = New Size(ClientSize.Width, Math.Min(h, Screen.FromControl(Me).WorkingArea.Height - 60))
                                 End Sub
            End If
        End Sub

        ''' <summary>Extra height reserved for Content when the dialog auto-sizes.</summary>
        Protected Overridable ReadOnly Property ContentExtraHeight As Integer
            Get
                Return 0
            End Get
        End Property

        Protected Function AddField(label As String, ctl As Control, Optional required As Boolean = False) As Control
            Dim l = Ui.Lbl(label & If(required, " *", ""), 9.5F, FontStyle.Regular, Theme.Muted)
            l.Anchor = AnchorStyles.Left
            l.Margin = New Padding(0, 6, 12, 6)
            ctl.Margin = New Padding(0, 4, 0, 4)
            If TypeOf ctl Is TextBox OrElse TypeOf ctl Is ComboBox Then ctl.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            Fields.RowCount += 1
            Fields.Controls.Add(l, 0, Fields.RowCount - 1)
            Fields.Controls.Add(ctl, 1, Fields.RowCount - 1)
            Return ctl
        End Function

        Protected Sub AddNote(text As String)
            Dim l = Ui.Lbl(text, 8.75F, FontStyle.Italic, Theme.Muted)
            l.MaximumSize = New Size(ClientSize.Width - 60, 0)
            Fields.RowCount += 1
            Fields.Controls.Add(l, 1, Fields.RowCount - 1)
        End Sub

        ''' <summary>Override to validate and save; call Close with DialogResult.OK on success.</summary>
        Protected Overridable Sub OnSave()
            DialogResult = DialogResult.OK
        End Sub

        Protected Sub Done()
            DialogResult = DialogResult.OK
            Close()
        End Sub

    End Class

End Namespace
