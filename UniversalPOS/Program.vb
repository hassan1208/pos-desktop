Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports UniversalPOS.Core
Imports UniversalPOS.UI

Public Module Program

    <STAThread>
    Public Sub Main()
        Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, Sub(s, e) ShowCrash(e.Exception)
        AddHandler AppDomain.CurrentDomain.UnhandledException, Sub(s, e) ShowCrash(TryCast(e.ExceptionObject, Exception))

        Dim created As Boolean
        Using mutex As New Mutex(True, "UniversalPOS_SingleInstance", created)
            If Not created Then
                MessageBox.Show("Universal POS is already running.", "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Try
                Data.Db.Initialize(AppPaths.DatabaseFile)
                AppSettings.Reload()
            Catch ex As Exception
                MessageBox.Show("Could not open the database:" & Environment.NewLine & AppPaths.DatabaseFile & Environment.NewLine & Environment.NewLine & ex.Message,
                                "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try
            Theme.SetScale(AppSettings.FontScale)
            AutoBackup()

            If Not AppSettings.IsSetupDone OrElse Not Services.UserService.HasUsers() Then
                Using setup As New SetupForm()
                    If setup.ShowDialog() <> DialogResult.OK Then Return
                End Using
            End If

            ' Login -> main window; "Log out" returns to the login screen.
            Do
                Using login As New LoginForm()
                    If login.ShowDialog() <> DialogResult.OK Then Exit Do
                End Using
                Dim main As New MainForm()
                Application.Run(main)
                If Not main.LoggedOut Then Exit Do
            Loop
        End Using
    End Sub

    ''' <summary>Keeps one automatic backup per day (last 30 days) in the data folder.</summary>
    Private Sub AutoBackup()
        Try
            Dim target = Path.Combine(AppPaths.BackupsDir, "auto_" & Date.Today.ToString("yyyy-MM-dd") & ".db")
            If Not File.Exists(target) Then Data.Db.BackupTo(target)
            Dim old = New DirectoryInfo(AppPaths.BackupsDir).GetFiles("auto_*.db").OrderByDescending(Function(f) f.Name).Skip(30)
            For Each oldFile In old
                oldFile.Delete()
            Next
        Catch
            ' A failed auto-backup must never stop the shop from working.
        End Try
    End Sub

    Private Sub ShowCrash(ex As Exception)
        If ex Is Nothing Then Return
        Try
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "error.log"), Date.Now.ToString("s") & "  " & ex.ToString() & Environment.NewLine & Environment.NewLine)
        Catch
        End Try
        If TypeOf ex Is BusinessException Then
            MessageBox.Show(ex.Message, "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            MessageBox.Show("Unexpected error:" & Environment.NewLine & ex.Message & Environment.NewLine & Environment.NewLine & "Details were saved to error.log in the data folder.",
                            "Universal POS", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

End Module
