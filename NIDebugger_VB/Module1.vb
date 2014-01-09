Imports NonIntrusive

Module Module1
    Dim debug As New NIDebugger()
    Dim memoryCave As UInteger

    Sub Main()
        ChangeTitle()
        Console.WriteLine("Press any key to exit...")
        Console.ReadKey()
    End Sub

    Sub ChangeTitle()
        Dim opts As New NIStartupOptions()
        opts.executable = "c:\windows\system32\notepad.exe"
        opts.resumeOnCreate = False

        debug.Execute(opts) _
            .AllocateMemory(100, memoryCave) _
            .WriteString(memoryCave, "Welcome To NIDebugger", System.Text.Encoding.Unicode) _
            .While(AddressOf IsStillRunning, AddressOf OverwriteText) _
            .Detach()

    End Sub

    Function IsStillRunning() As Boolean
        Return debug.Process.HasExited = False
    End Function

    Sub OverwriteText()
        Dim oldValue As UInteger
        Dim oldString As String = ""

        debug.SingleStep() _
            .SetProcBP("user32.dll", "SetWindowTextW") _
            .Continue() _
            .ReadStackValue(8, oldValue) _
            .ReadString(oldValue, 100, System.Text.Encoding.Unicode, oldString) _
            .WriteStackValue(8, memoryCave)

        Console.WriteLine("Old value: " + oldString)

    End Sub

End Module
