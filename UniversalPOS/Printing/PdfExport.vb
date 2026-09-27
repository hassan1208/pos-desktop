Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

Namespace Printing

    ''' <summary>
    ''' Minimal PDF writer: each rendered page becomes a JPEG image on its own PDF page.
    ''' No printer or external library needed, so invoices can be saved and shared anywhere.
    ''' </summary>
    Public Module PdfExport

        Public Sub Save(pages As List(Of Bitmap), pageInches As SizeF, file As String)
            Dim wPt = pageInches.Width * 72.0F
            Dim hPt = pageInches.Height * 72.0F
            Dim inv = Globalization.CultureInfo.InvariantCulture
            Using fs As New FileStream(file, FileMode.Create, FileAccess.Write)
                Dim offsets As New List(Of Long)
                Dim write = Sub(s As String)
                                Dim b = Encoding.ASCII.GetBytes(s)
                                fs.Write(b, 0, b.Length)
                            End Sub
                Dim beginObj = Sub(n As Integer)
                                   While offsets.Count < n
                                       offsets.Add(0)
                                   End While
                                   offsets(n - 1) = fs.Position
                                   write(n & " 0 obj" & vbLf)
                               End Sub

                write("%PDF-1.4" & vbLf)
                ' 1 = catalog, 2 = pages, then 3 objects per page: page, content, image
                Dim kids As New StringBuilder()
                For i = 0 To pages.Count - 1
                    kids.Append((3 + i * 3).ToString(inv) & " 0 R ")
                Next
                beginObj(1)
                write("<< /Type /Catalog /Pages 2 0 R >>" & vbLf & "endobj" & vbLf)
                beginObj(2)
                write("<< /Type /Pages /Kids [" & kids.ToString() & "] /Count " & pages.Count & " >>" & vbLf & "endobj" & vbLf)

                For i = 0 To pages.Count - 1
                    Dim pageObj = 3 + i * 3
                    Dim contentObj = pageObj + 1
                    Dim imageObj = pageObj + 2
                    Dim jpeg As Byte()
                    Using ms As New MemoryStream()
                        Dim enc = ImageCodecInfo.GetImageEncoders().First(Function(c) c.FormatID = ImageFormat.Jpeg.Guid)
                        Using ep As New EncoderParameters(1)
                            ep.Param(0) = New EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L)
                            pages(i).Save(ms, enc, ep)
                        End Using
                        jpeg = ms.ToArray()
                    End Using
                    Dim size = String.Format(inv, "{0:0.##} {1:0.##}", wPt, hPt)

                    beginObj(pageObj)
                    write("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " & size & "] /Resources << /XObject << /Im" & i & " " & imageObj & " 0 R >> >> /Contents " & contentObj & " 0 R >>" & vbLf & "endobj" & vbLf)

                    Dim content = String.Format(inv, "q {0:0.##} 0 0 {1:0.##} 0 0 cm /Im{2} Do Q", wPt, hPt, i)
                    beginObj(contentObj)
                    write("<< /Length " & content.Length & " >>" & vbLf & "stream" & vbLf & content & vbLf & "endstream" & vbLf & "endobj" & vbLf)

                    beginObj(imageObj)
                    write("<< /Type /XObject /Subtype /Image /Width " & pages(i).Width & " /Height " & pages(i).Height &
                          " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " & jpeg.Length & " >>" & vbLf & "stream" & vbLf)
                    fs.Write(jpeg, 0, jpeg.Length)
                    write(vbLf & "endstream" & vbLf & "endobj" & vbLf)
                Next

                Dim xref = fs.Position
                write("xref" & vbLf & "0 " & (offsets.Count + 1) & vbLf & "0000000000 65535 f " & vbLf)
                For Each o In offsets
                    write(o.ToString("0000000000", inv) & " 00000 n " & vbLf)
                Next
                write("trailer" & vbLf & "<< /Size " & (offsets.Count + 1) & " /Root 1 0 R >>" & vbLf & "startxref" & vbLf & xref & vbLf & "%%EOF" & vbLf)
            End Using
            For Each p In pages
                p.Dispose()
            Next
        End Sub

    End Module

    ''' <summary>Preview window used when no printer is installed: shows the pages and can save a PDF.</summary>
    Public Class ImagePreviewForm
        Inherits Form

        Private ReadOnly _pages As List(Of Bitmap)

        Public Sub New(doc As PrintDoc)
            Text = doc.Title & " " & doc.DocNumber & "  (no printer installed - preview)"
            WindowState = FormWindowState.Maximized
            StartPosition = FormStartPosition.CenterScreen
            BackColor = Color.FromArgb(120, 120, 120)
            _pages = DocPrinter.RenderPages(doc, 110)

            Dim bar As New ToolStrip With {.GripStyle = ToolStripGripStyle.Hidden}
            bar.Items.Add(New ToolStripButton("Save PDF", Nothing, Sub() DocPrinter.SavePdfWithDialog(doc, Me)))
            bar.Items.Add(New ToolStripButton("Close", Nothing, Sub() Close()))
            bar.Items.Add(New ToolStripLabel("   Tip: install a printer (or ""Microsoft Print to PDF"") to print directly."))

            Dim flow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Padding = New Padding(20)}
            For Each p In _pages
                flow.Controls.Add(New PictureBox With {.Image = p, .Size = p.Size, .Margin = New Padding(0, 0, 0, 20), .BackColor = Color.White})
            Next
            AddHandler flow.Resize, Sub()
                                        For Each c As Control In flow.Controls
                                            c.Margin = New Padding(Math.Max(0, (flow.ClientSize.Width - c.Width) \ 2 - 20), 0, 0, 20)
                                        Next
                                    End Sub
            Controls.Add(flow)
            Controls.Add(bar)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                For Each p In _pages
                    p.Dispose()
                Next
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
