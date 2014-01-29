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
    Private Sub FixFileAllignment(ByRef TheFile As String)
        Dim FileArray() As Byte = FileIO.FileSystem.ReadAllBytes(TheFile)
        Dim AddrToEP As Long = FileArray(59) & FileArray(60)
        Dim AddrToAllignment As Long = AddrToEP + 59
        Dim Allignment As String = ""
        Dim AllignmentVal As Long
        Dim SectionAlignment As String = ""
        Dim SectionAlignmentVal As Long

        Dim i As Integer = 4
        Do Until i = 0
            Dim tmp As String = Hex(FileArray(AddrToAllignment + i))
            If tmp.Length = 1 Then tmp = "0" & tmp
            Allignment = Allignment & tmp
            i -= 1
        Loop

        i = 4
        Do Until i = 0
            Dim tmp As String = Hex(FileArray((AddrToAllignment - 4) + i))
            If tmp.Length = 1 Then tmp = "0" & tmp
            SectionAlignment = SectionAlignment & tmp
            i -= 1
        Loop
        SectionAlignmentVal = SectionAlignment
        AllignmentVal = Allignment
        If AllignmentVal < SectionAlignmentVal Then
            Dim ff() As Char = SectionAlignment
            i = 1
            Dim b As Integer = ff.Length - 2
            Do Until i = 5
                Dim TmpVar As Long = ff(b) & ff(b + 1)
                FileArray(AddrToAllignment + i) = "&h" & TmpVar
                i += 1
                b -= 2
            Loop
            FileIO.FileSystem.WriteAllBytes(TheFile, FileArray, False)
        End If
    End Sub
    Sub Initilize(ByVal MyPath As String)
        If FileIO.FileSystem.FileExists(MyPath & "ARImpRec.dll") Then
        Else
            FileIO.FileSystem.WriteAllBytes(MyPath & "ARImpRec.dll", My.Resources.ARImpRec, False)
        End If
    End Sub
    Function GetSavePath()
        Return SavedTo
    End Function
    Function FixImports(ByVal ProcID As UInteger, ByVal DumpPath As String, ByVal IROEP As UInteger, Optional FixFileAlignment As Boolean = True)
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
            CleanupFiles(Npath, FixFileAlignment)
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
    Private Sub CleanupFiles(ByVal NewPath As String, ByRef FixAlignment As Boolean)
        Try
            FileIO.FileSystem.DeleteFile(Strings.Left(NewPath, NewPath.LastIndexOf("\")) & "\Unpacked.exe")
        Catch ex As Exception
        End Try

        FileIO.FileSystem.CopyFile(NewPath, Strings.Left(NewPath, NewPath.LastIndexOf("\")) & "\Unpacked.exe")
        FileIO.FileSystem.DeleteFile(NewPath)
        SavedTo = Strings.Left(NewPath, NewPath.LastIndexOf("\")) & "\Unpacked.exe"
        If FixAlignment = True Then FixFileAllignment(SavedTo)
    End Sub
End Class
