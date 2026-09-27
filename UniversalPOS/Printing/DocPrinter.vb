Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Windows.Forms
Imports UniversalPOS.Core

Namespace Printing

    Public Class PrintColumn
        Public Property Header As String
        ''' <summary>Relative width weight.</summary>
        Public Property Weight As Single
        Public Property Align As StringAlignment
        ''' <summary>Hidden on thermal receipts (narrow paper).</summary>
        Public Property HideOnThermal As Boolean

        Public Sub New(header As String, weight As Single, Optional align As StringAlignment = StringAlignment.Near, Optional hideOnThermal As Boolean = False)
            Me.Header = header
            Me.Weight = weight
            Me.Align = align
            Me.HideOnThermal = hideOnThermal
        End Sub
    End Class

    ''' <summary>
    ''' A printable business document (invoice, quotation, bill, statement, list).
    ''' Rendered with the shop header, a table that pages automatically, totals and a footer.
    ''' </summary>
    Public Class PrintDoc
        Public Property Title As String = ""
        Public Property DocNumber As String = ""
        Public Property DocDate As String = ""
        ''' <summary>"Bill to" style lines printed under the header.</summary>
        Public Property PartyLines As New List(Of String)
        Public Property Columns As New List(Of PrintColumn)
        Public Property Rows As New List(Of String())
        Public Property Totals As New List(Of KeyValuePair(Of String, String))
        ''' <summary>Index into Totals that is printed as the bold grand total (-1 = last).</summary>
        Public Property GrandTotalIndex As Integer = -1
        Public Property Notes As String = ""
        Public Property Footer As String = ""
        ''' <summary>a4 | half_a4 | thermal. Empty = the shop's default.</summary>
        Public Property Format As String = ""
        Public Property Landscape As Boolean

        Public Sub AddTotal(label As String, value As String)
            Totals.Add(New KeyValuePair(Of String, String)(label, value))
        End Sub
    End Class

    Public Module DocPrinter

        Public ReadOnly Property HasPrinter As Boolean
            Get
                Try
                    Return PrinterSettings.InstalledPrinters.Count > 0
                Catch
                    Return False
                End Try
            End Get
        End Property

        ''' <summary>
        ''' Opens a print preview with Print and "Save PDF". Without any printer installed, an
        ''' image preview is shown instead (Save PDF still works).
        ''' </summary>
        Public Sub Preview(doc As PrintDoc, Optional owner As IWin32Window = Nothing)
            If Not HasPrinter Then
                Using f As New ImagePreviewForm(doc)
                    f.ShowDialog(owner)
                End Using
                Return
            End If
            Using pd = Build(doc)
                Using dlg As New PrintPreviewDialog With {.Document = pd, .Width = 1000, .Height = 800, .StartPosition = FormStartPosition.CenterScreen, .ShowIcon = False}
                    dlg.Text = doc.Title & " " & doc.DocNumber
                    DirectCast(dlg, Form).WindowState = FormWindowState.Maximized
                    dlg.PrintPreviewControl.Zoom = If(EffectiveFormat(doc) = "thermal", 1.0, 0.9)
                    Dim bar = dlg.Controls.OfType(Of ToolStrip)().FirstOrDefault()
                    If bar IsNot Nothing Then
                        bar.Items.Add(New ToolStripSeparator())
                        bar.Items.Add(New ToolStripButton("Save PDF", Nothing, Sub() SavePdfWithDialog(doc, dlg)) With {.DisplayStyle = ToolStripItemDisplayStyle.Text})
                    End If
                    dlg.ShowDialog(owner)
                End Using
            End Using
        End Sub

        ''' <summary>Shows the printer dialog and prints.</summary>
        Public Sub PrintDirect(doc As PrintDoc, Optional owner As IWin32Window = Nothing)
            If Not HasPrinter Then
                Preview(doc, owner)
                Return
            End If
            Using pd = Build(doc)
                Using dlg As New PrintDialog With {.Document = pd, .UseEXDialog = True}
                    If dlg.ShowDialog(owner) = DialogResult.OK Then pd.Print()
                End Using
            End Using
        End Sub

        ''' <summary>Prints straight to the default printer (falls back to preview when there is none).</summary>
        Public Sub PrintNow(doc As PrintDoc, Optional owner As IWin32Window = Nothing)
            If Not HasPrinter Then
                Preview(doc, owner)
                Return
            End If
            Using pd = Build(doc)
                pd.Print()
            End Using
        End Sub

        Public Sub SavePdfWithDialog(doc As PrintDoc, Optional owner As IWin32Window = Nothing)
            Dim safe = String.Concat((doc.Title & " " & doc.DocNumber).Trim().Split(IO.Path.GetInvalidFileNameChars())).Replace(" ", "_")
            Using dlg As New SaveFileDialog With {.Filter = "PDF (*.pdf)|*.pdf", .FileName = safe & ".pdf"}
                If dlg.ShowDialog(owner) <> DialogResult.OK Then Return
                PdfExport.Save(RenderPages(doc, 150), PageSizeInches(doc), dlg.FileName)
                If MessageBox.Show("PDF saved:" & Environment.NewLine & dlg.FileName & Environment.NewLine & Environment.NewLine & "Open it now?", "Universal POS",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                    Process.Start(New ProcessStartInfo(dlg.FileName) With {.UseShellExecute = True})
                End If
            End Using
        End Sub

        Private Function EffectiveFormat(doc As PrintDoc) As String
            Return If(String.IsNullOrEmpty(doc.Format), AppSettings.InvoiceFormat, doc.Format)
        End Function

        ''' <summary>Paper size and margins in hundredths of an inch (already rotated for landscape).</summary>
        Private Sub Paper(doc As PrintDoc, ByRef width As Integer, ByRef height As Integer, ByRef margin As Integer)
            Select Case EffectiveFormat(doc)
                Case "thermal"
                    ' 80mm roll: 3.15 inch wide; length grows with the number of rows.
                    width = 315
                    height = 520 + doc.Rows.Count * 36 + doc.Totals.Count * 20 + doc.PartyLines.Count * 16 + If(doc.Notes <> "", 60, 0)
                    margin = 8
                    Return
                Case "half_a4"
                    width = 583 : height = 827 : margin = 30
                Case Else
                    width = 827 : height = 1169 : margin = 45
            End Select
            If doc.Landscape Then
                Dim t = width
                width = height
                height = t
            End If
        End Sub

        Public Function PageSizeInches(doc As PrintDoc) As SizeF
            Dim w, h, m As Integer
            Paper(doc, w, h, m)
            Return New SizeF(w / 100.0F, h / 100.0F)
        End Function

        Public Function Build(doc As PrintDoc) As PrintDocument
            Dim fmtName = EffectiveFormat(doc)
            Dim thermal = fmtName = "thermal"
            Dim w, h, m As Integer
            Paper(doc, w, h, m)
            Dim pd As New PrintDocument()
            pd.DocumentName = doc.Title & " " & doc.DocNumber
            Dim landscape = doc.Landscape AndAlso Not thermal
            pd.DefaultPageSettings.PaperSize = If(landscape, New PaperSize("Custom", h, w), New PaperSize("Custom", w, h))
            pd.DefaultPageSettings.Landscape = landscape
            pd.DefaultPageSettings.Margins = New Margins(m, m, m, m)

            Dim rowIndex = 0
            Dim page = 0
            AddHandler pd.BeginPrint, Sub()
                                          rowIndex = 0
                                          page = 0
                                      End Sub
            AddHandler pd.PrintPage, Sub(sender, e)
                                         page += 1
                                         rowIndex = RenderPage(doc, e.Graphics, e.MarginBounds, thermal, fmtName = "half_a4", rowIndex, page)
                                         e.HasMorePages = rowIndex < doc.Rows.Count
                                     End Sub
            Return pd
        End Function

        ''' <summary>Renders every page to a bitmap (used for the printer-less preview and PDF export).</summary>
        Public Function RenderPages(doc As PrintDoc, dpi As Integer) As List(Of Bitmap)
            Dim fmtName = EffectiveFormat(doc)
            Dim w, h, m As Integer
            Paper(doc, w, h, m)
            Dim pages As New List(Of Bitmap)
            Dim rowIndex = 0
            Dim page = 0
            Do
                page += 1
                Dim bmp As New Bitmap(CInt(w * dpi / 100.0), CInt(h * dpi / 100.0))
                ' At 100 dpi one pixel = 1/100 inch, the printer's unit, so fonts and layout match
                ' printing exactly; the transform then scales up to the requested resolution.
                bmp.SetResolution(100, 100)
                Using g = Graphics.FromImage(bmp)
                    g.Clear(Color.White)
                    g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    g.PageUnit = GraphicsUnit.Pixel
                    g.ScaleTransform(dpi / 100.0F, dpi / 100.0F)
                    rowIndex = RenderPage(doc, g, New RectangleF(m, m, w - 2 * m, h - 2 * m), fmtName = "thermal", fmtName = "half_a4", rowIndex, page)
                End Using
                pages.Add(bmp)
            Loop While rowIndex < doc.Rows.Count AndAlso page < 500
            Return pages
        End Function

        ''' <summary>Draws one page. Returns the index of the next row still to be printed.</summary>
        Private Function RenderPage(doc As PrintDoc, g As Graphics, area As RectangleF, thermal As Boolean, small As Boolean, startRow As Integer, page As Integer) As Integer
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
            Dim scale = If(thermal, 0.78F, If(small, 0.85F, 1.0F))
            Dim fBody As New Font("Segoe UI", 9.0F * scale)
            Dim fBold As New Font("Segoe UI", 9.0F * scale, FontStyle.Bold)
            Dim fSmall As New Font("Segoe UI", 8.0F * scale)
            Dim fShop As New Font("Segoe UI", 16.0F * scale, FontStyle.Bold)
            Dim fTitle As New Font("Segoe UI", 13.0F * scale, FontStyle.Bold)
            Dim fGrand As New Font("Segoe UI", 11.0F * scale, FontStyle.Bold)
            Dim ink = Brushes.Black
            Dim muted As New SolidBrush(Color.FromArgb(90, 90, 90))
            Dim linePen As New Pen(Color.FromArgb(200, 200, 200))
            Dim darkPen As New Pen(Color.Black, 1.2F)
            Dim y = area.Top

            Try
                ' ---- watermark ----
                If AppSettings.WatermarkText <> "" AndAlso Not thermal Then
                    Dim state = g.Save()
                    g.TranslateTransform(area.Left + area.Width / 2, area.Top + area.Height / 2)
                    g.RotateTransform(-30)
                    Using wf As New Font("Segoe UI", 60 * scale, FontStyle.Bold), wb As New SolidBrush(Color.FromArgb(18, 0, 0, 0))
                        Dim sz = g.MeasureString(AppSettings.WatermarkText, wf)
                        g.DrawString(AppSettings.WatermarkText, wf, wb, -sz.Width / 2, -sz.Height / 2)
                    End Using
                    g.Restore(state)
                End If

                ' ---- header (first page full, later pages compact) ----
                If page = 1 Then
                    If thermal Then
                        y = DrawCentered(g, AppSettings.ShopName, fShop, ink, area, y)
                        For Each line In ShopLines()
                            y = DrawCentered(g, line, fSmall, muted, area, y)
                        Next
                        y += 4
                        g.DrawLine(darkPen, area.Left, y, area.Right, y) : y += 4
                        y = DrawCentered(g, doc.Title, fBold, ink, area, y)
                        Dim meta = String.Join("   ", {If(doc.DocNumber <> "", "#" & doc.DocNumber, ""), doc.DocDate}.Where(Function(x) x <> ""))
                        If meta <> "" Then y = DrawCentered(g, meta, fSmall, muted, area, y)
                        For Each p In doc.PartyLines
                            y = DrawWrapped(g, p, fSmall, ink, area.Left, y, area.Width)
                        Next
                        y += 4
                    Else
                        Dim leftW = area.Width * 0.62F
                        Dim x = area.Left
                        Dim logo = AppSettings.LogoFile
                        Dim topY = y
                        If logo <> "" Then
                            Try
                                Using img = Image.FromFile(logo)
                                    Dim maxH = 60.0F * scale, maxW = 150.0F * scale
                                    Dim ratio = Math.Min(maxW / img.Width, maxH / img.Height)
                                    g.DrawImage(img, x, y, img.Width * ratio, img.Height * ratio)
                                    y += img.Height * ratio + 4
                                End Using
                            Catch
                            End Try
                        End If
                        y = DrawWrapped(g, AppSettings.ShopName, fShop, ink, x, y, leftW)
                        For Each line In ShopLines()
                            y = DrawWrapped(g, line, fSmall, muted, x, y, leftW)
                        Next
                        ' right side: title / number / date
                        Dim ry = topY
                        Dim rr As New RectangleF(area.Left + leftW, 0, area.Width - leftW, 100)
                        ry = DrawRight(g, doc.Title, fTitle, ink, rr, ry)
                        If doc.DocNumber <> "" Then ry = DrawRight(g, "No: " & doc.DocNumber, fBody, muted, rr, ry)
                        If doc.DocDate <> "" Then ry = DrawRight(g, "Date: " & doc.DocDate, fBody, muted, rr, ry)
                        y = Math.Max(y, ry) + 8
                        g.DrawLine(darkPen, area.Left, y, area.Right, y)
                        y += 8
                        For Each p In doc.PartyLines
                            y = DrawWrapped(g, p, fBody, ink, area.Left, y, area.Width)
                        Next
                        If doc.PartyLines.Count > 0 Then y += 8
                    End If
                Else
                    y = DrawWrapped(g, AppSettings.ShopName & "  -  " & doc.Title & " " & doc.DocNumber & "  (page " & page & ")", fSmall, muted, area.Left, y, area.Width) + 6
                End If

                ' ---- table ----
                Dim cols = doc.Columns.Select(Function(c, i) New With {.Col = c, .Index = i}).Where(Function(c) Not (thermal AndAlso c.Col.HideOnThermal)).ToList()
                Dim totalWeight = cols.Sum(Function(c) c.Col.Weight)
                Dim widths = cols.Select(Function(c) area.Width * c.Col.Weight / totalWeight).ToList()
                Dim rowPad = 3.0F * scale

                If cols.Count > 0 Then
                    ' header row
                    Dim hh = fBold.GetHeight(g) + rowPad * 2
                    If Not thermal Then g.FillRectangle(New SolidBrush(Color.FromArgb(238, 240, 243)), area.Left, y, area.Width, hh)
                    Dim cx = area.Left
                    For i = 0 To cols.Count - 1
                        DrawCell(g, cols(i).Col.Header, fBold, ink, New RectangleF(cx, y + rowPad, widths(i), hh), cols(i).Col.Align)
                        cx += widths(i)
                    Next
                    y += hh
                    If thermal Then g.DrawLine(linePen, area.Left, y, area.Right, y)
                End If

                Dim footerReserve = If(thermal, 0.0F, fSmall.GetHeight(g) * 2 + 6)
                Dim r As Integer = startRow
                While r < doc.Rows.Count
                    Dim row = doc.Rows(r)
                    ' Row height = tallest wrapped cell.
                    Dim rh = fBody.GetHeight(g)
                    For i = 0 To cols.Count - 1
                        Dim text = If(cols(i).Index < row.Length, row(cols(i).Index), "")
                        Dim sz = g.MeasureString(text, fBody, CInt(Math.Max(1, widths(i) - 6)))
                        rh = Math.Max(rh, sz.Height)
                    Next
                    rh += rowPad * 2
                    If y + rh > area.Bottom - footerReserve AndAlso r > startRow Then Exit While
                    Dim cx = area.Left
                    For i = 0 To cols.Count - 1
                        Dim text = If(cols(i).Index < row.Length, row(cols(i).Index), "")
                        DrawCell(g, text, fBody, ink, New RectangleF(cx, y + rowPad, widths(i), rh - rowPad), cols(i).Col.Align)
                        cx += widths(i)
                    Next
                    y += rh
                    g.DrawLine(linePen, area.Left, y, area.Right, y)
                    r += 1
                End While

                ' ---- totals / notes / footer on the last page ----
                If r >= doc.Rows.Count Then
                    y += 6
                    Dim grand = If(doc.GrandTotalIndex < 0, doc.Totals.Count - 1, doc.GrandTotalIndex)
                    Dim labelW = If(thermal, area.Width * 0.55F, area.Width * 0.7F)
                    For i = 0 To doc.Totals.Count - 1
                        Dim t = doc.Totals(i)
                        Dim f = If(i = grand, fGrand, fBody)
                        Dim h = f.GetHeight(g) + 2
                        If i = grand Then g.DrawLine(darkPen, area.Left + labelW * 0.6F, y, area.Right, y) : y += 2
                        DrawCell(g, t.Key, If(i = grand, fGrand, fBold), ink, New RectangleF(area.Left, y, labelW, h), StringAlignment.Far)
                        DrawCell(g, t.Value, f, ink, New RectangleF(area.Left + labelW, y, area.Width - labelW, h), StringAlignment.Far)
                        y += h
                    Next
                    If doc.Notes <> "" Then
                        y += 10
                        y = DrawWrapped(g, "Notes: " & doc.Notes, fSmall, muted, area.Left, y, area.Width)
                    End If
                    Dim footer = If(doc.Footer <> "", doc.Footer, AppSettings.InvoiceFooter)
                    If footer <> "" Then
                        y += 14
                        If thermal Then
                            y = DrawCentered(g, footer, fSmall, muted, area, y)
                        Else
                            y = DrawWrapped(g, footer, fSmall, muted, area.Left, y, area.Width)
                        End If
                    End If
                End If

                If Not thermal Then
                    Dim stamp = "Printed " & Date.Now.ToString("dd MMM yyyy hh:mm tt", Fmt.Inv) & "   |   Page " & page
                    DrawCell(g, stamp, fSmall, muted, New RectangleF(area.Left, area.Bottom - fSmall.GetHeight(g), area.Width, fSmall.GetHeight(g) + 2), StringAlignment.Far)
                End If
                Return r
            Finally
                fBody.Dispose() : fBold.Dispose() : fSmall.Dispose() : fShop.Dispose() : fTitle.Dispose() : fGrand.Dispose()
                muted.Dispose() : linePen.Dispose() : darkPen.Dispose()
            End Try
        End Function

        Private Function ShopLines() As List(Of String)
            Dim lines As New List(Of String)
            If AppSettings.Slogan <> "" Then lines.Add(AppSettings.Slogan)
            If AppSettings.Address <> "" Then lines.Add(AppSettings.Address)
            Dim contact = String.Join("  |  ", {AppSettings.Phone, AppSettings.Email}.Where(Function(x) x <> ""))
            If contact <> "" Then lines.Add(contact)
            lines.AddRange(AppSettings.ContactLines())
            Return lines
        End Function

        Private Sub DrawCell(g As Graphics, text As String, f As Font, b As Brush, rect As RectangleF, align As StringAlignment)
            Using sf As New StringFormat With {.Alignment = align, .Trimming = StringTrimming.Word}
                Dim inner As New RectangleF(rect.X + 3, rect.Y, Math.Max(1, rect.Width - 6), rect.Height)
                g.DrawString(If(text, ""), f, b, inner, sf)
            End Using
        End Sub

        Private Function DrawWrapped(g As Graphics, text As String, f As Font, b As Brush, x As Single, y As Single, width As Single) As Single
            Dim sz = g.MeasureString(text, f, CInt(Math.Max(1, width)))
            g.DrawString(text, f, b, New RectangleF(x, y, width, sz.Height + 2))
            Return y + sz.Height
        End Function

        Private Function DrawCentered(g As Graphics, text As String, f As Font, b As Brush, area As RectangleF, y As Single) As Single
            Dim sz = g.MeasureString(text, f, CInt(area.Width))
            Using sf As New StringFormat With {.Alignment = StringAlignment.Center}
                g.DrawString(text, f, b, New RectangleF(area.Left, y, area.Width, sz.Height + 2), sf)
            End Using
            Return y + sz.Height
        End Function

        Private Function DrawRight(g As Graphics, text As String, f As Font, b As Brush, col As RectangleF, y As Single) As Single
            Dim sz = g.MeasureString(text, f, CInt(col.Width))
            Using sf As New StringFormat With {.Alignment = StringAlignment.Far}
                g.DrawString(text, f, b, New RectangleF(col.Left, y, col.Width, sz.Height + 2), sf)
            End Using
            Return y + sz.Height
        End Function

    End Module

End Namespace
