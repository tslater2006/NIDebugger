Imports System.Runtime.InteropServices

Public Class ARImpRec
    <DllImport("ARImpRec.dll", CallingConvention:=CallingConvention.StdCall, EntryPoint:="SearchAndRebuildImports@28", CharSet:=CharSet.Ansi)> _
    Public Shared Function SearchAndRebuildImports(IRProcessId As UInteger, IRNameOfDumped As String, IROEP As UInt32, IRSaveOEPToFile As UInt32, ByRef IRIATRVA As UInt32, ByRef IRIATSize As UInt32, _
    IRWarning As IntPtr) As UInteger
    End Function

    <DllImport("ARImpRec.dll", CallingConvention:=CallingConvention.StdCall, EntryPoint:="SearchAndRebuildImportsIATOptimized@28", CharSet:=CharSet.Ansi)> _
    Public Shared Function SearchAndRebuildImportsIATOptimized(IRProcessId As UInteger, IRNameOfDumped As String, IROEP As UInt32, IRSaveOEPToFile As UInt32, ByRef IRIATRVA As UInt32, ByRef IRIATSize As UInt32, _
IRWarning As IntPtr) As UInteger
    End Function

    <DllImport("ARImpRec.dll", CallingConvention:=CallingConvention.StdCall, EntryPoint:="SearchAndRebuildImportsNoNewSection@28", CharSet:=CharSet.Ansi)> _
    Public Shared Function SearchAndRebuildImportsNoNewSection(IRProcessId As UInteger, IRNameOfDumped As String, IROEP As UInt32, IRSaveOEPToFile As UInt32, ByRef IRIATRVA As UInt32, ByRef IRIATSize As UInt32, _
IRWarning As IntPtr) As UInteger
    End Function

    Private Shared SavedTo As String
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
        If SearchAndRebuildImports(ProcID, DumpPath, IROEP) = False Then
            If SearchAndRebuildImportsNoNewSection(ProcID, DumpPath, IROEP) = False Then
                If SearchAndRebuildImportsIATOptimized(ProcID, DumpPath, IROEP) = False Then
                    Return False
                End If
            End If
        End If

        Dim Npath As String = Strings.Left(DumpPath, DumpPath.Length - 4) & "_.exe"
        Dim ReCheckCount As Integer = 0
ReCheck:

        If FileIO.FileSystem.FileExists(Npath) Then
            FileIO.FileSystem.DeleteFile(DumpPath)
            CleanupFiles(Npath)
        Else

            If ReCheckCount >= 2 Then
                Return False
            End If

            If ReCheckCount = 0 Then
                If SearchAndRebuildImportsNoNewSection(ProcID, DumpPath, IROEP) = True Then
                    ReCheckCount += 1
                    GoTo ReCheck
                Else
                    ReCheckCount += 1
                    If SearchAndRebuildImportsIATOptimized(ProcID, DumpPath, IROEP) = True Then
                        ReCheckCount += 1
                        GoTo ReCheck
                    Else
                        Return False
                    End If
                End If
            Else
                If SearchAndRebuildImportsIATOptimized(ProcID, DumpPath, IROEP) = True Then
                    ReCheckCount += 1
                    GoTo ReCheck
                Else
                    Return False
                End If
            End If

            Return False
            'MsgBox("Auto import reconstruction failed!, Manually rebuilt now!")
        End If
        Return True
    End Function
    Private Function SearchAndRebuildImports(ByRef ProcID, ByRef DumpPath, ByRef IROEP)
        Dim iatStart As UInt32 = 0
        Dim iatSize As UInt32 = 0
        Dim errorPtr As IntPtr = GetErrorPtr()

        Try
            Dim result As Integer = SearchAndRebuildImports(ProcID, DumpPath, IROEP, 0, iatStart, iatSize, errorPtr)
            Dim errorMessage As String = Marshal.PtrToStringAnsi(errorPtr)
            Marshal.FreeHGlobal(errorPtr)
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function
    Private Function SearchAndRebuildImportsNoNewSection(ByRef ProcID, ByRef DumpPath, ByRef IROEP)
        Dim errorPtr As IntPtr
        Dim iatSize As UInteger
        Dim iatStart As UInteger
        Try
            iatStart = 0
            iatSize = 0
            errorPtr = GetErrorPtr()
            Dim result As Integer = SearchAndRebuildImportsNoNewSection(ProcID, DumpPath, IROEP, 0, iatStart, iatSize, errorPtr)
            Dim errorMessage As String = Marshal.PtrToStringAnsi(errorPtr)
            Marshal.FreeHGlobal(errorPtr)
            Return True
        Catch exx As Exception
            Return False
        End Try
    End Function
    Private Function GetErrorPtr()
        Return Marshal.AllocHGlobal(1000)
    End Function
    Private Function SearchAndRebuildImportsIATOptimized(ByRef ProcID, ByRef DumpPath, ByRef IROEP)
        Dim errorPtr As IntPtr
        Dim iatSize As UInteger
        Dim iatStart As UInteger
        Try
            iatSize = 0
            iatStart = 0
            errorPtr = GetErrorPtr()
            Dim result As Integer = SearchAndRebuildImportsIATOptimized(ProcID, DumpPath, IROEP, 0, iatStart, iatSize, errorPtr)
            Dim errorMessage As String = Marshal.PtrToStringAnsi(errorPtr)
            Marshal.FreeHGlobal(errorPtr)
            ' ReCheckCount += 1
            'GoTo ReCheck
        Catch exxx As Exception
            Return False
        End Try
        Return True
    End Function
    Private Sub CleanupFiles(ByVal NewPath As String)
        Try
            FileIO.FileSystem.DeleteFile(Strings.Left(NewPath, NewPath.LastIndexOf("\")) & "\Unpacked.exe")
        Catch ex As Exception
        End Try

        FileIO.FileSystem.CopyFile(NewPath, Strings.Left(NewPath, NewPath.LastIndexOf("\")) & "\Unpacked.exe")
        FileIO.FileSystem.DeleteFile(NewPath)
        SavedTo = Strings.Left(NewPath, NewPath.LastIndexOf("\")) & "\Unpacked.exe"
    End Sub
End Class
