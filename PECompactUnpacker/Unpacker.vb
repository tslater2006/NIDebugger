Public Class Unpacker

    Dim debugger As New NonIntrusive.NIDebugger()
    Public Sub UnpackePE(path As String)
        Dim opts As New NonIntrusive.NIStartupOptions()
        opts.executable = path
        opts.resumeOnCreate = False

        Dim y As UInteger
        Dim opCode(2) As Byte
        opCode(1) = &HFF
        opCode(2) = &HE0

        debugger.StepIntoCalls = False

        Dim dumpOpts As New NonIntrusive.NIDumpOptions()
        dumpOpts.ChangeEP = True
        dumpOpts.OutputPath = path + "_dump.exe"
        dumpOpts.PerformDumpFix = True

        Dim newEP As UInteger

        debugger.Execute(opts) _
            .ReadDWORD(debugger.Context.Eip + 1, y) _
            .SetBreakpoint(y) _
            .Continue() _
            .SingleStep(3) _
            .SetBreakpoint(debugger.Context.Ecx) _
            .Continue() _
            .Until(AddressOf FoundJMP, AddressOf debugger.SingleStep) _
            .SingleStep()

        newEP = debugger.Context.Eip - debugger.Process.MainModule.BaseAddress

        dumpOpts.EntryPoint = newEP

        debugger.DumpProcess(dumpOpts)

        Clipboard.Clear()
        'set clipboard OEP/RVA
        Clipboard.SetText(Hex(newEP))

        MsgBox("Fix Imports!" & vbCrLf & "OEP/RVA Saved To Clipboard!")

        debugger.Detach().Terminate()

    End Sub

    Public Function FoundJMP()
        Dim data() As Byte = debugger.GetInstrOpcodes()
        Return (data(0) = &HFF And data(1) = &HE0)
    End Function


End Class
