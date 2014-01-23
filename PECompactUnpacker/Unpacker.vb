Imports System.Runtime.InteropServices

Public Class Unpacker

    Dim debugger As New NonIntrusive.NIDebugger()

    <DllImport("ARImpRec.dll", CallingConvention:=CallingConvention.StdCall, EntryPoint:="SearchAndRebuildImports@28", CharSet:=CharSet.Ansi)> _
    Public Shared Function SearchAndRebuildImports(IRProcessId As UInteger, IRNameOfDumped As String, IROEP As UInt32, IRSaveOEPToFile As UInt32, ByRef IRIATRVA As UInt32, ByRef IRIATSize As UInt32, _
    IRWarning As IntPtr) As UInteger
    End Function

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

        Dim iatStart As UInt32 = 0
        Dim iatSize As UInt32 = 0

        Dim errorPtr As IntPtr = Marshal.AllocHGlobal(1000)

        Try
            Dim result As Integer = SearchAndRebuildImports(debugger.Process.Id, dumpOpts.OutputPath, newEP + debugger.ProcessImageBase, 0, iatStart, iatSize, errorPtr)
            Dim errorMessage As String = Marshal.PtrToStringAnsi(errorPtr)
            Marshal.FreeHGlobal(errorPtr)
        Catch ex As Exception

        End Try


        Dim Npath As String = Strings.Left(path, path.Length - 4) & "_.exe"

        If FileIO.FileSystem.FileExists(Npath) Then
            FileIO.FileSystem.DeleteFile(dumpOpts.OutputPath)
            FileIO.FileSystem.CopyFile(Npath, Strings.Left(Npath, Npath.LastIndexOf("\")) & "\Unpacked.exe")
            FileIO.FileSystem.DeleteFile(Npath)
            MsgBox("Unpacked!" & vbCrLf & "Saved to: " & Strings.Left(Npath, Npath.LastIndexOf("\")) & "\Unpacked.exe")
        Else
            MsgBox("Auto import reconstruction failed!, Manually rebuilt now!")
        End If

        debugger.Detach().Terminate()

    End Sub

    Public Function FoundJMP()
        If debugger.Context.Eip > &H405240 Then
            Dim i As Integer = 0
        End If
        Dim data() As Byte = debugger.GetInstrOpcodes()
        If (data.Length < 2) Then
            Return False
        End If
        If (data(0) = &HFF And (data(1) = &HE0)) Then
            Return True
        Else
            Return False
        End If
    End Function


End Class
