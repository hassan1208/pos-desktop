Imports System.Data
Imports System.Globalization
Imports System.Security.Cryptography

Namespace Core

    ''' <summary>A validation / business-rule failure whose message is safe to show the user.</summary>
    Public Class BusinessException
        Inherits Exception
        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    Public Module Security

        Private Const Iterations As Integer = 100000

        ''' <summary>PBKDF2-SHA256 hash stored as "iterations.salt.hash" (base64).</summary>
        Public Function HashPassword(password As String) As String
            Dim salt = RandomNumberGenerator.GetBytes(16)
            Dim hash = Pbkdf2(password, salt, Iterations, 32)
            Return Iterations & "." & Convert.ToBase64String(salt) & "." & Convert.ToBase64String(hash)
        End Function

        Public Function VerifyPassword(password As String, stored As String) As Boolean
            If String.IsNullOrEmpty(stored) Then Return False
            Dim parts = stored.Split("."c)
            If parts.Length <> 3 Then Return False
            Dim iter As Integer
            If Not Integer.TryParse(parts(0), iter) Then Return False
            Dim salt = Convert.FromBase64String(parts(1))
            Dim expected = Convert.FromBase64String(parts(2))
            Dim actual = Pbkdf2(password, salt, iter, expected.Length)
            Return CryptographicOperations.FixedTimeEquals(actual, expected)
        End Function

        ''' <summary>
        ''' PBKDF2-HMAC-SHA256 (RFC 8018) built on HMACSHA256, so it does not depend on the
        ''' OS key-derivation API (missing on some older / emulated Windows installs).
        ''' </summary>
        Private Function Pbkdf2(password As String, salt As Byte(), iterations As Integer, length As Integer) As Byte()
            Dim result(length - 1) As Byte
            Using hmac As New HMACSHA256(Text.Encoding.UTF8.GetBytes(password))
                Dim blocks = (length + 31) \ 32
                For block = 1 To blocks
                    Dim input(salt.Length + 3) As Byte
                    Buffer.BlockCopy(salt, 0, input, 0, salt.Length)
                    input(salt.Length) = CByte((block >> 24) And &HFF)
                    input(salt.Length + 1) = CByte((block >> 16) And &HFF)
                    input(salt.Length + 2) = CByte((block >> 8) And &HFF)
                    input(salt.Length + 3) = CByte(block And &HFF)
                    Dim u = hmac.ComputeHash(input)
                    Dim t = CType(u.Clone(), Byte())
                    For i = 2 To iterations
                        u = hmac.ComputeHash(u)
                        For j = 0 To t.Length - 1
                            t(j) = t(j) Xor u(j)
                        Next
                    Next
                    Dim offset = (block - 1) * 32
                    Buffer.BlockCopy(t, 0, result, offset, Math.Min(32, length - offset))
                Next
            End Using
            Return result
        End Function

    End Module

    ''' <summary>Formatting / parsing helpers shared by the UI and printing.</summary>
    Public Module Fmt

        Public ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

        Public Function Money(value As Decimal) As String
            Return AppSettings.CurrencySymbol & " " & value.ToString("#,##0.00", Inv)
        End Function

        Public Function Num(value As Decimal) As String
            Return value.ToString("#,##0.00", Inv)
        End Function

        Public Function DbDate(d As Date) As String
            Return d.ToString("yyyy-MM-dd", Inv)
        End Function

        Public Function ParseDbDate(s As String) As Date
            Dim d As Date
            If Date.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, d) Then Return d
            If Date.TryParse(s, Inv, DateTimeStyles.None, d) Then Return d
            Return Date.Today
        End Function

        Public Function ShowDate(s As String) As String
            If String.IsNullOrEmpty(s) Then Return ""
            Return ParseDbDate(s).ToString("dd MMM yyyy", Inv)
        End Function

        Public Function ParseDec(text As String) As Decimal
            Dim v As Decimal
            If Decimal.TryParse(If(text, "").Replace(",", "").Trim(), NumberStyles.Number, Inv, v) Then Return v
            Return 0D
        End Function

        Public Function ParseInt(text As String) As Integer
            Dim v As Integer
            If Integer.TryParse(If(text, "").Replace(",", "").Trim(), NumberStyles.Integer, Inv, v) Then Return v
            Return 0
        End Function

        Public Function Round2(v As Decimal) As Decimal
            Return Math.Round(v, 2, MidpointRounding.AwayFromZero)
        End Function

        Public Function DocNo(prefix As String, id As Long) As String
            Return prefix & id.ToString("000000", Inv)
        End Function

        ''' <summary>Null-safe field readers for DataRows.</summary>
        Public Function GetStr(row As DataRow, col As String) As String
            Dim v = row(col)
            Return If(v Is DBNull.Value, "", Convert.ToString(v, Inv))
        End Function

        Public Function GetDec(row As DataRow, col As String) As Decimal
            Dim v = row(col)
            Return If(v Is DBNull.Value, 0D, Convert.ToDecimal(v, Inv))
        End Function

        Public Function GetLng(row As DataRow, col As String) As Long
            Dim v = row(col)
            Return If(v Is DBNull.Value, 0L, Convert.ToInt64(v, Inv))
        End Function

        Public Function GetInt(row As DataRow, col As String) As Integer
            Return CInt(GetLng(row, col))
        End Function

        Public Function NullIfZero(id As Long) As Object
            Return If(id > 0, CObj(id), Nothing)
        End Function

        Public Function NullIfEmpty(text As String) As Object
            Return If(String.IsNullOrWhiteSpace(text), Nothing, CObj(text.Trim()))
        End Function

    End Module

    ''' <summary>The signed-in user.</summary>
    Public Module Session
        Public Property UserId As Long
        Public Property UserName As String = ""
        Public Property FullName As String = ""
        Public Property Role As String = "admin"

        Public ReadOnly Property IsAdmin As Boolean
            Get
                Return Role = "admin"
            End Get
        End Property
    End Module

End Namespace
