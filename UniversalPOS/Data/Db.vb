Imports System.Data
Imports System.IO
Imports Microsoft.Data.Sqlite

Namespace Data

    ''' <summary>
    ''' One open connection (optionally inside a transaction) with small helpers for
    ''' parameterised queries. Parameters are passed positionally and bound as @p0, @p1, ...
    ''' </summary>
    Public NotInheritable Class DbSession
        Implements IDisposable

        Private ReadOnly _conn As SqliteConnection
        Private _tx As SqliteTransaction

        Friend Sub New(connectionString As String, useTransaction As Boolean)
            _conn = New SqliteConnection(connectionString)
            _conn.Open()
            Using cmd = _conn.CreateCommand()
                cmd.CommandText = "PRAGMA foreign_keys = ON;"
                cmd.ExecuteNonQuery()
            End Using
            If useTransaction Then _tx = _conn.BeginTransaction()
        End Sub

        Private Function MakeCommand(sql As String, args As Object()) As SqliteCommand
            Dim cmd = _conn.CreateCommand()
            cmd.CommandText = sql
            cmd.Transaction = _tx
            If args IsNot Nothing Then
                For i = 0 To args.Length - 1
                    Dim v = args(i)
                    If v Is Nothing Then
                        v = DBNull.Value
                    ElseIf TypeOf v Is Date Then
                        v = CDate(v).ToString("yyyy-MM-dd")
                    ElseIf TypeOf v Is Decimal Then
                        v = CDbl(CDec(v))
                    ElseIf TypeOf v Is Boolean Then
                        v = If(CBool(v), 1, 0)
                    End If
                    cmd.Parameters.AddWithValue("@p" & i, v)
                Next
            End If
            Return cmd
        End Function

        Public Function Exec(sql As String, ParamArray args As Object()) As Integer
            Using cmd = MakeCommand(sql, args)
                Return cmd.ExecuteNonQuery()
            End Using
        End Function

        ''' <summary>Runs an INSERT and returns the new row id.</summary>
        Public Function Insert(sql As String, ParamArray args As Object()) As Long
            Using cmd = MakeCommand(sql, args)
                cmd.ExecuteNonQuery()
            End Using
            Using cmd = MakeCommand("SELECT last_insert_rowid()", Nothing)
                Return Convert.ToInt64(cmd.ExecuteScalar())
            End Using
        End Function

        Public Function Scalar(sql As String, ParamArray args As Object()) As Object
            Using cmd = MakeCommand(sql, args)
                Dim v = cmd.ExecuteScalar()
                Return If(v Is DBNull.Value, Nothing, v)
            End Using
        End Function

        Public Function ScalarDec(sql As String, ParamArray args As Object()) As Decimal
            Dim v = Scalar(sql, args)
            Return If(v Is Nothing, 0D, Convert.ToDecimal(v))
        End Function

        Public Function ScalarLong(sql As String, ParamArray args As Object()) As Long
            Dim v = Scalar(sql, args)
            Return If(v Is Nothing, 0L, Convert.ToInt64(v))
        End Function

        ''' <summary>
        ''' Loads a result set into a DataTable. Column types are inferred from the values
        ''' (SQLite is dynamically typed): integers stay Long, anything with a fractional
        ''' value becomes Decimal, text becomes String. Keeps grid sorting sane.
        ''' </summary>
        Public Function Query(sql As String, ParamArray args As Object()) As DataTable
            Dim names As New List(Of String)
            Dim rows As New List(Of Object())
            Using cmd = MakeCommand(sql, args)
                Using r = cmd.ExecuteReader()
                    For i = 0 To r.FieldCount - 1
                        names.Add(r.GetName(i))
                    Next
                    While r.Read()
                        Dim vals(r.FieldCount - 1) As Object
                        r.GetValues(vals)
                        rows.Add(vals)
                    End While
                End Using
            End Using

            Dim dt As New DataTable()
            For c = 0 To names.Count - 1
                Dim kind As Type = Nothing
                For Each row In rows
                    Dim v = row(c)
                    If v Is Nothing OrElse v Is DBNull.Value Then Continue For
                    Dim t = v.GetType()
                    If t Is GetType(Double) Then t = GetType(Decimal)
                    If kind Is Nothing Then
                        kind = t
                    ElseIf kind IsNot t Then
                        If (kind Is GetType(Long) AndAlso t Is GetType(Decimal)) OrElse (kind Is GetType(Decimal) AndAlso t Is GetType(Long)) Then
                            kind = GetType(Decimal)
                        Else
                            kind = GetType(String)
                        End If
                    End If
                Next
                If kind Is Nothing Then kind = GetType(String)
                Dim colName = names(c)
                Dim n = 1
                While dt.Columns.Contains(colName)
                    n += 1
                    colName = names(c) & n
                End While
                dt.Columns.Add(colName, kind)
            Next

            dt.BeginLoadData()
            For Each row In rows
                Dim vals(names.Count - 1) As Object
                For c = 0 To names.Count - 1
                    Dim v = row(c)
                    If v Is Nothing OrElse v Is DBNull.Value Then
                        vals(c) = DBNull.Value
                    Else
                        Dim target = dt.Columns(c).DataType
                        If target Is GetType(Decimal) Then
                            vals(c) = Math.Round(Convert.ToDecimal(v), 4)
                        ElseIf target Is GetType(String) Then
                            vals(c) = Convert.ToString(v, Globalization.CultureInfo.InvariantCulture)
                        Else
                            vals(c) = v
                        End If
                    End If
                Next
                dt.Rows.Add(vals)
            Next
            dt.EndLoadData()
            Return dt
        End Function

        Public Function QueryRow(sql As String, ParamArray args As Object()) As DataRow
            Dim dt = Query(sql, args)
            Return If(dt.Rows.Count > 0, dt.Rows(0), Nothing)
        End Function

        Public Sub Commit()
            If _tx IsNot Nothing Then
                _tx.Commit()
                _tx.Dispose()
                _tx = Nothing
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If _tx IsNot Nothing Then
                Try
                    _tx.Rollback()
                Catch
                End Try
                _tx.Dispose()
                _tx = Nothing
            End If
            _conn.Dispose()
        End Sub
    End Class

    ''' <summary>Application-wide database access.</summary>
    Public Module Db

        Public Property DbPath As String

        Public ReadOnly Property ConnectionString As String
            Get
                Return New SqliteConnectionStringBuilder() With {
                    .DataSource = DbPath,
                    .Mode = SqliteOpenMode.ReadWriteCreate,
                    .Pooling = False
                }.ToString()
            End Get
        End Property

        Public Function Open() As DbSession
            Return New DbSession(ConnectionString, False)
        End Function

        ''' <summary>Runs work inside a transaction; rolls back automatically if it throws.</summary>
        Public Function Tx(Of T)(work As Func(Of DbSession, T)) As T
            Using s As New DbSession(ConnectionString, True)
                Dim result = work(s)
                s.Commit()
                Return result
            End Using
        End Function

        Public Sub Tx(work As Action(Of DbSession))
            Using s As New DbSession(ConnectionString, True)
                work(s)
                s.Commit()
            End Using
        End Sub

        Public Function Query(sql As String, ParamArray args As Object()) As DataTable
            Using s = Open()
                Return s.Query(sql, args)
            End Using
        End Function

        Public Function QueryRow(sql As String, ParamArray args As Object()) As DataRow
            Using s = Open()
                Return s.QueryRow(sql, args)
            End Using
        End Function

        Public Function Exec(sql As String, ParamArray args As Object()) As Integer
            Using s = Open()
                Return s.Exec(sql, args)
            End Using
        End Function

        Public Function Insert(sql As String, ParamArray args As Object()) As Long
            Using s = Open()
                Return s.Insert(sql, args)
            End Using
        End Function

        Public Function Scalar(sql As String, ParamArray args As Object()) As Object
            Using s = Open()
                Return s.Scalar(sql, args)
            End Using
        End Function

        Public Function ScalarDec(sql As String, ParamArray args As Object()) As Decimal
            Using s = Open()
                Return s.ScalarDec(sql, args)
            End Using
        End Function

        Public Function ScalarLong(sql As String, ParamArray args As Object()) As Long
            Using s = Open()
                Return s.ScalarLong(sql, args)
            End Using
        End Function

        ''' <summary>Creates the database file and schema if needed, and applies migrations.</summary>
        Public Sub Initialize(path As String)
            DbPath = path
            Dim dir = IO.Path.GetDirectoryName(path)
            If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
            Using s = Open()
                s.Exec("PRAGMA journal_mode = WAL;")
            End Using
            Schema.Apply()
        End Sub

        ''' <summary>Copies the live database to a single file (safe while the app is running).</summary>
        Public Sub BackupTo(destFile As String)
            If File.Exists(destFile) Then File.Delete(destFile)
            Using src As New SqliteConnection(ConnectionString)
                src.Open()
                Using dst As New SqliteConnection(New SqliteConnectionStringBuilder() With {.DataSource = destFile, .Pooling = False}.ToString())
                    dst.Open()
                    src.BackupDatabase(dst)
                End Using
            End Using
        End Sub

        ''' <summary>Replaces the live database with a backup file (after checking it is a POS database).</summary>
        Public Sub RestoreFrom(sourceFile As String)
            Using test As New SqliteConnection(New SqliteConnectionStringBuilder() With {.DataSource = sourceFile, .Mode = SqliteOpenMode.ReadOnly, .Pooling = False}.ToString())
                test.Open()
                Using cmd = test.CreateCommand()
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('products','sales','settings')"
                    If Convert.ToInt32(cmd.ExecuteScalar()) < 3 Then
                        Throw New Core.BusinessException("The selected file is not a Universal POS backup.")
                    End If
                End Using
            End Using
            Using src As New SqliteConnection(New SqliteConnectionStringBuilder() With {.DataSource = sourceFile, .Mode = SqliteOpenMode.ReadOnly, .Pooling = False}.ToString())
                src.Open()
                Using dst As New SqliteConnection(ConnectionString)
                    dst.Open()
                    src.BackupDatabase(dst)
                End Using
            End Using
            Schema.Apply()
        End Sub

    End Module

End Namespace
