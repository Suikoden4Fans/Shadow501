Imports System
Imports System.Windows.Forms

Friend Module Program
    <STAThread>
    Public Sub Main()
        ApplicationConfiguration.Initialize()
        Application.Run(New MainForm())
    End Sub
End Module
