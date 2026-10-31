Imports System
Imports System.Windows.Forms

Friend Module Program
    <STAThread()>
    Public Sub Main()
        If Array.Exists(Environment.GetCommandLineArgs(), Function(x) x = "--self-test") Then
            Environment.ExitCode = AppSelfTests.Run()
            Return
        End If
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module
