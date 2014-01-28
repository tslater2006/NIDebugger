Imports System.Runtime.InteropServices

Public Class ARImpRec
    <DllImport("ARImpRec.dll", CallingConvention:=CallingConvention.StdCall, EntryPoint:="SearchAndRebuildImports@28", CharSet:=CharSet.Ansi)> _
    Public Shared Function SearchAndRebuildImports(IRProcessId As UInteger, IRNameOfDumped As String, IROEP As UInt32, IRSaveOEPToFile As UInt32, ByRef IRIATRVA As UInt32, ByRef IRIATSize As UInt32, _
    IRWarning As IntPtr) As UInteger
    End Function

    Public Shared SavedTo As String

    Sub Initilize(ByVal MyPath As String)
        If FileIO.FileSystem.FileExists(MyPath & "ARImpRec.dll") Then
        Else
            FileIO.FileSystem.WriteAllBytes(MyPath & "ARImpRec.dll", My.Resources.ARImpRec, False)
        End If
    End Sub

    Function GetSavePath()
        Return SavedTo
    End Function

    Function FixImports(ByVal ProcID, ByVal DumpPath, ByVal IROEP)
        Dim iatStart As UInt32 = 0
        Dim iatSize As UInt32 = 0

        Dim errorPtr As IntPtr = Marshal.AllocHGlobal(1000)

        Try
            ' Dim IROEP As UInteger = newEP + Debugger.ProcessImageBase
            Dim result As Integer = SearchAndRebuildImports(ProcID, DumpPath, IROEP, 0, iatStart, iatSize, errorPtr)
            Dim errorMessage As String = Marshal.PtrToStringAnsi(errorPtr)
            Marshal.FreeHGlobal(errorPtr)
        Catch ex As Exception
            Return False
        End Try

        Dim Npath As String = Strings.Left(DumpPath, DumpPath.Length - 4) & "_.exe"

        If FileIO.FileSystem.FileExists(Npath) Then
            FileIO.FileSystem.DeleteFile(DumpPath)
            Try
                FileIO.FileSystem.DeleteFile(Strings.Left(Npath, Npath.LastIndexOf("\")) & "\Unpacked.exe")
            Catch ex As Exception

            End Try
            FileIO.FileSystem.CopyFile(Npath, Strings.Left(Npath, Npath.LastIndexOf("\")) & "\Unpacked.exe")
            FileIO.FileSystem.DeleteFile(Npath)
            SavedTo = Strings.Left(Npath, Npath.LastIndexOf("\")) & "\Unpacked.exe"
            Return True
            '    MsgBox("Unpacked!" & vbCrLf & "Saved to: " & Strings.Left(Npath, Npath.LastIndexOf("\")) & "\Unpacked.exe")
        Else
            Return False
            'MsgBox("Auto import reconstruction failed!, Manually rebuilt now!")
        End If

        Return True
    End Function

End Class
