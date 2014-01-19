Public Class Form1

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim Debugger As New NonIntrusive.NIDebugger
        Dim StrtOpts As New NonIntrusive.NIStartupOptions
        With StrtOpts
            .resumeOnCreate = False
            .executable = "C:\Program Files\Nistune\Nistune.exe"
        End With

        With Debugger
            .AutoClearBP = True
            .Execute(StrtOpts)
            .SetBreakpoint(&H5FEC663E)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 0)
            .SetBreakpoint(&H5FEC6673)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 1)
            .SetBreakpoint(&H5FE94EB0)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 0)
            .SetBreakpoint(&H5FE94FF7)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 1)
            .SetBreakpoint(&H5FE9501E)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 0)
            .SetBreakpoint(&H5FE95207)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 1)
            .SetBreakpoint(&H5FE9527B)
            .Continue()
            .SetFlag(NonIntrusive.NIContextFlag.ZERO, 0)
            .Continue()
            .Detach()
        End With
        End
    End Sub
End Class
