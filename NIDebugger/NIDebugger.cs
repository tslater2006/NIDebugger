using LDASM_Sharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace NonIntrusive
{
    public class NIDebugger
    {
        #region Properties
        public bool AutoClearBP = true;
        public bool StepIntoCalls = true;
        public Process Process
        {
            get
            {
                return debuggedProcess;
            }
        }

        public NIContext Context
        {
            get
            {

                return contexts[getCurrentThreadId()];
            }
        }

        #endregion

        #region Private Variables

        Dictionary<uint, NIBreakPoint> breakpoints = new Dictionary<uint, NIBreakPoint>();
        Dictionary<int, IntPtr> threadHandles = new Dictionary<int, IntPtr>();
        public Dictionary<int, NIContext> contexts = new Dictionary<int, NIContext>();
        private static ManualResetEvent mre = new ManualResetEvent(false);
        BackgroundWorker bwContinue;
        Win32.PROCESS_INFORMATION debuggedProcessInfo;

        NIBreakPoint lastBreakpoint;

        Process debuggedProcess;
        LDASM lde = new LDASM();

        private byte[] BREAKPOINT = new byte[] { 0xEB, 0xFE };

        #endregion

        #region Public Methods

        #region Memory Methods
        public NIDebugger ReadWORD(uint address, out UInt16 value)
        {
            byte[] data;
            ReadData(address, 2, out data);
            value = BitConverter.ToUInt16(data, 0);
            return this;
        }
        public NIDebugger ReadData(uint address, int length, out byte[] output)
        {
            int numRead = 0;
            byte[] data = new byte[length];
            Win32.ReadProcessMemory(debuggedProcessInfo.hProcess, (int)address, data, length, ref numRead);

            output = data;

            return this;
        }

        public NIDebugger WriteData(uint address, byte[] data)
        {
            Win32.MEMORY_BASIC_INFORMATION mbi = new Win32.MEMORY_BASIC_INFORMATION();

            Win32.VirtualQueryEx(debuggedProcessInfo.hProcess, (int)address, ref mbi, Marshal.SizeOf(mbi));
            uint oldProtect = 0;

            Win32.VirtualProtectEx((IntPtr)debuggedProcessInfo.hProcess, (IntPtr)mbi.BaseAddress, (UIntPtr)mbi.RegionSize, (uint)Win32.AllocationProtectEnum.PAGE_EXECUTE_READWRITE, out oldProtect);

            int numWritten = 0;
            Win32.WriteProcessMemory(debuggedProcessInfo.hProcess, (int)address, data, data.Length, ref numWritten);

            Win32.VirtualProtectEx((IntPtr)debuggedProcessInfo.hProcess, (IntPtr)mbi.BaseAddress, (UIntPtr)mbi.RegionSize, oldProtect, out oldProtect);

            return this;
        }

        public NIDebugger WriteString(uint address, String str, Encoding encode)
        {
            return WriteData(address, encode.GetBytes(str));
        }

        public NIDebugger ReadString(uint address, int maxLength, Encoding encode, out String value)
        {
            byte[] data;
            ReadData(address, maxLength, out data);
            value = "";
            if (encode.IsSingleByte)
            {
                for (int x = 0; x < data.Length - 1; x++)
                {
                    if (data[x] == 0)
                    {
                        value = encode.GetString(data, 0, x + 1);
                        break;
                    }
                }
            }
            else
            {
                for (int x = 0; x < data.Length - 2; x++)
                {
                    if (data[x] + data[x + 1] == 0)
                    {
                        value = encode.GetString(data, 0, x + 1);
                        break;
                    }
                }
            }
            return this;
        }
        public NIDebugger ReadDWORD(uint address, out uint value)
        {
            byte[] data;
            ReadData(address, 4, out data);
            value = BitConverter.ToUInt32(data, 0);
            return this;
        }

        public NIDebugger WriteDWORD(uint address, uint value)
        {
            byte[] data = BitConverter.GetBytes(value);
            return WriteData(address, data);
        }

        public NIDebugger ReadStackValue(uint espOffset, out uint value)
        {
            ReadDWORD(Context.Esp + espOffset, out value);
            return this;
        }

        public NIDebugger WriteStackValue(uint espOffset, uint value)
        {
            return WriteDWORD(Context.Esp + espOffset, value);
        }
        public NIDebugger DumpProcess(DumpOptions opts)
        {
            try
            {
                FileStream fs = File.Create(opts.OutputPath);

                // get module base
                var baseAddr = (uint)debuggedProcess.Modules[0].BaseAddress;
                uint peHeader;
                ReadDWORD(baseAddr + 0x3C, out peHeader);
                peHeader += baseAddr;

                // update EP in Memory if needed
                if (opts.ChangeEP)
                {
                    WriteDWORD(peHeader + 0x28, opts.EntryPoint);
                }
                ushort numSections;
                ReadWORD(peHeader + 0x06, out numSections);

                var sectionStartOffset = 0xF8;

                if (opts.PerformDumpFix)
                {
                    for (var x = 0; x < numSections; x++)
                    {
                        var sectionAddr = peHeader + (uint)(sectionStartOffset + (x * 0x28));
                        uint virtualSize, virtualAddr;

                        ReadDWORD(sectionAddr + 0x08, out virtualSize);
                        ReadDWORD(sectionAddr + 0x0c, out virtualAddr);

                        // update raw values
                        WriteDWORD(sectionAddr + 0x10, virtualSize);
                        WriteDWORD(sectionAddr + 0x14, virtualAddr);
                    }
                }
                uint pePointer, imageEnd;
                ReadDWORD(baseAddr + 0x3c, out pePointer);

                ReadDWORD(baseAddr + pePointer + 0x50, out imageEnd);
                imageEnd += baseAddr;

                byte[] imageData;
                ReadData(baseAddr, (int)(imageEnd - baseAddr), out imageData);

                fs.Write(imageData, 0, imageData.Length);
                fs.Flush();
                fs.Close();


            }
            catch (Exception e) { }
            return this;
        }
        public NIDebugger InsertHook(uint address, uint destination, out byte[] overwrittenOpcodes)
        {
            byte[] data;
            ReadData(address, 16, out data);
            uint x = 0;
            LDASM ldasm = new LDASM();
            while (x <= 5)
            {
                uint curAddress = address + x;
                if (breakpoints.ContainsKey(curAddress) == true)
                {
                    Array.Copy(breakpoints[curAddress].originalBytes,0, data,x, 2);
                }

                uint curSize = ldasm.ldasm(data, (int)x, false).size;
                x += curSize;
            }
            overwrittenOpcodes = new byte[x];
            byte[] nopField = new byte[overwrittenOpcodes.Length];
            for (int y = 0; y < nopField.Length; y++)
            {
                nopField[y] = 0x90;
            }

            WriteData(address,nopField);

            Array.Copy(data, overwrittenOpcodes, x);

            int jumpDistance = (int)destination - (int)address - 5;

            byte[] hookData = new byte[5];
            hookData[0] = 0xE9;

            Array.Copy(BitConverter.GetBytes(jumpDistance),0,hookData,1,4);

            WriteData(address,hookData);

            return this;
        }
        public NIDebugger AllocateMemory(uint size, out uint address)
        {
            IntPtr memLocation = Win32.VirtualAllocEx((IntPtr)debuggedProcessInfo.hProcess, new IntPtr(), size, (uint)Win32.StateEnum.MEM_RESERVE | (uint)Win32.StateEnum.MEM_COMMIT, (uint)Win32.AllocationProtectEnum.PAGE_EXECUTE_READWRITE);

            address = (uint) memLocation;
            return this;
        }
        public uint FindProcAddress(String modName, String method)
        {
            Win32.MODULEENTRY32 module = getModule(modName);

            if (module.dwSize == 0)
            {
                Console.WriteLine("Failed to find module");
                throw new Exception("Target doesn't have module: " + modName + " loaded.");
            }
            uint modBase = (uint)module.modBaseAddr;

            uint peAddress,exportTableAddress,exportTableSize;
            byte[] exportTable;

            ReadDWORD(modBase + 0x3c, out peAddress);

            ReadDWORD(modBase + peAddress + 0x78,out exportTableAddress);
            ReadDWORD(modBase + peAddress + 0x7C,out exportTableSize);

            ReadData(modBase + exportTableAddress, (int)exportTableSize, out exportTable);

            uint exportEnd = modBase + exportTableAddress + exportTableSize;


            uint numberOfFunctions = BitConverter.ToUInt32(exportTable, 0x14);
            uint numberOfNames = BitConverter.ToUInt32(exportTable, 0x18);

            uint functionAddressBase = BitConverter.ToUInt32(exportTable, 0x1c);
            uint nameAddressBase = BitConverter.ToUInt32(exportTable, 0x20);
            uint ordinalAddressBase = BitConverter.ToUInt32(exportTable, 0x24);

            StringBuilder sb = new StringBuilder();
            for (int x = 0; x < numberOfNames; x++)
            {
                sb.Clear();
                uint namePtr = BitConverter.ToUInt32(exportTable, (int)(nameAddressBase - exportTableAddress) + (x * 4)) - exportTableAddress;

                while (exportTable[namePtr] != 0)
                {
                    sb.Append((char)exportTable[namePtr]);
                    namePtr++;
                }

                ushort funcOrdinal = BitConverter.ToUInt16(exportTable, (int)(ordinalAddressBase - exportTableAddress) + (x * 2));


                uint funcAddress = BitConverter.ToUInt32(exportTable, (int)(functionAddressBase - exportTableAddress) + (funcOrdinal * 4));
                funcAddress += modBase;

                if (sb.ToString().Equals(method))
                {
                    return funcAddress;
                }
                //  functions.Add(new ExportedFunction(){name = sb.ToString(), address = funcAddress});

            }
            return 0;


        }
        #endregion

        #region Control Methods

        public NIDebugger Execute(NIStartupOptions opts)
        {
            Win32.SECURITY_ATTRIBUTES sa1 = new Win32.SECURITY_ATTRIBUTES();
            sa1.nLength = Marshal.SizeOf(sa1);
            Win32.SECURITY_ATTRIBUTES sa2 = new Win32.SECURITY_ATTRIBUTES();
            sa2.nLength = Marshal.SizeOf(sa2);
            Win32.STARTUPINFO si = new Win32.STARTUPINFO();
            debuggedProcessInfo = new Win32.PROCESS_INFORMATION();
            int ret = Win32.CreateProcess(opts.executable, opts.commandLine, ref sa1, ref sa2, 0, 0x00000200 | Win32.CREATE_SUSPENDED, 0, null, ref si, ref debuggedProcessInfo);

            debuggedProcess = Process.GetProcessById(debuggedProcessInfo.dwProcessId);
            threadHandles.Add(debuggedProcessInfo.dwThreadId, new IntPtr(debuggedProcessInfo.hThread));
            if (opts.resumeOnCreate)
            {
                Win32.ResumeThread((IntPtr)debuggedProcessInfo.hThread);
            }
            else
            {
                getContexts();
                uint OEP = contexts[debuggedProcessInfo.dwThreadId].Eax;
                SetBreakpoint(OEP);
                Continue();
                ClearBreakpoint(OEP);

                Console.WriteLine("We should be at OEP");

            }

            if (opts.patchTickCount && opts.incrementTickCount == false)
            {
                byte[] patchData = new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 };
                WriteData(FindProcAddress("kernel32.dll", "GetTickCount"), patchData);
            } else if (opts.patchTickCount && opts.incrementTickCount)
            {
                byte[] patchData = new byte[] { 0x51, 0xB8, 0x01, 0x00, 0x00, 0x00, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x59, 0x83, 0xE9, 0x09, 0xFF, 0x01, 0x59, 0xC3 };
                uint memoryCave;
                byte[] opcodes;

                uint hookAddr = FindProcAddress("kernelbase.dll", "GetTickCount");
                if (hookAddr == 0)
                {
                    hookAddr = FindProcAddress("kernel32.dll", "GetTickCount");
                }

                // work
                    AllocateMemory(100, out memoryCave);
                    WriteData(memoryCave, patchData);
                    InsertHook(hookAddr, memoryCave, out opcodes);
            }

            return this;

        }


        public NIDebugger Continue()
        {
            updateContexts();

            bwContinue = new BackgroundWorker();
            bwContinue.DoWork += bw_Continue;

            mre.Reset();
            bwContinue.RunWorkerAsync();
            mre.WaitOne();

            if (AutoClearBP)
            {
                ClearBreakpoint(lastBreakpoint.bpAddress);
            }
            return this;
        }

        public void Terminate()
        {
            Continue();
            debuggedProcess.Kill();
        }

        public NIDebugger Detach()
        {
            pauseAllThreads();
            List<uint> bpAddresses = breakpoints.Keys.ToList();
            foreach (uint addr in bpAddresses)
            {
                ClearBreakpoint(addr);
            }
            updateContexts();
            resumeAllThreads();
            return this;
        }

        public NIDebugger SetBreakpoint(uint address)
        {
            if (breakpoints.Keys.Contains(address) == false)
            {
                NIBreakPoint bp = new NIBreakPoint() { bpAddress = address};
                byte[] origBytes;
                ReadData(address, 2, out origBytes);
                bp.originalBytes = origBytes;

                breakpoints.Add(address, bp);
                WriteData(address, BREAKPOINT);
            }
            return this;
        }

        public NIDebugger ClearBreakpoint(uint address)
        {
            if (breakpoints.Keys.Contains(address))
            {

                WriteData(address, breakpoints[address].originalBytes);
                breakpoints.Remove(address);
            }
            return this;
        }

        public uint GetInstrLength()
        {

            uint address = contexts[getCurrentThreadId()].Eip;

            byte[] data;
            ReadData(address, 16,out data);
            if (breakpoints.ContainsKey(address) == true)
            {
                Array.Copy(breakpoints[address].originalBytes, data, 2);
            }

            return lde.ldasm(data, 0, false).size;
        }

        public byte[] GetInstrOpcodes()
        {
            uint address = contexts[getCurrentThreadId()].Eip;
            byte[] data;
            ReadData(address, 16, out data);

            if (breakpoints.ContainsKey(address) == true)
            {
                Array.Copy(breakpoints[address].originalBytes, data, 2);
            }

            uint size = lde.ldasm(data, 0, false).size;

            return data;
        }

        public NIDebugger SingleStep(int number)
        {
            for (int x = 0; x < number; x++)
            {
                SingleStep();
            }
            return this;
        }

        public NIDebugger SingleStep()
        {
            updateContexts();
            uint address = Context.Eip;
            byte[] data;
            ReadData(address, 16, out data);

            if (breakpoints.ContainsKey(address) == true)
            {
                Array.Copy(breakpoints[address].originalBytes, data, 2);
                ClearBreakpoint(address);
            }

            ldasm_data ldata = lde.ldasm(data, 0, false);

            uint size = ldata.size;
            uint nextAddress = Context.Eip + size;

            if (ldata.opcd_size == 1 && (data[ldata.opcd_offset] == 0xEB))
            {
                // we have a 1 byte JMP here
                sbyte offset = (sbyte)data[ldata.imm_offset];
                nextAddress = (uint)(Context.Eip + offset) + ldata.size;
            }

            if (ldata.opcd_size == 1 && ((data[ldata.opcd_offset] == 0xE9) || (data[ldata.opcd_offset] == 0xE8)))
            {
                // we have a long JMP or CALL here
                int offset = BitConverter.ToInt32(data, ldata.imm_offset);
                if ((data[ldata.opcd_offset] == 0xE9) || (StepIntoCalls && (data[ldata.opcd_offset] == 0xE8)))
                {
                    nextAddress = (uint)(Context.Eip + offset) + ldata.size;
                }

            }

            if (ldata.opcd_size == 1 && ((data[ldata.opcd_offset] >= 0x70 && (data[ldata.opcd_offset] <= 0x79)) || (data[ldata.opcd_offset] == 0xE3)))
            {
                // we have a 1byte jcc here
                bool willJump = evalJcc(data[ldata.opcd_offset]);

                if (willJump)
                {
                    sbyte offset = (sbyte)data[ldata.imm_offset];
                    nextAddress = (uint)(Context.Eip + offset) + ldata.size;
                }
            }

            if (ldata.opcd_size == 2 && ((data[ldata.opcd_offset] == 0x0F) || (data[ldata.opcd_offset + 1] == 0x80)))
            {
                // we have a 2 byte jcc here

                bool willJump = evalJcc(data[ldata.opcd_offset + 1]);

                if (willJump)
                {
                    int offset = BitConverter.ToInt32(data, ldata.imm_offset);
                    nextAddress = (uint)((Context.Eip + offset) + ldata.size);
                }
            }

            if (data[ldata.opcd_offset] == 0xC3 || data[ldata.opcd_offset] == 0xC2)
            {
                ReadStackValue(0, out nextAddress);
            }

            if (data[ldata.opcd_offset] == 0xFF && ldata.opcd_size == 1 && ldata.modrm != 0x00)
            {
                if (ldata.modrm >= 0xD0 && ldata.modrm <= 0xD7 && StepIntoCalls == true)
                {
                    // we have a CALL REGISTER
                    switch (ldata.modrm)
                    {
                        case 0xD0:
                            nextAddress = Context.Eax;
                            break;
                        case 0xD1:
                            nextAddress = Context.Ecx;
                            break;
                        case 0xD2:
                            nextAddress = Context.Edx;
                            break;
                        case 0xD3:
                            nextAddress = Context.Ebx;
                            break;
                        case 0xD4:
                            nextAddress = Context.Esp;
                            break;
                        case 0xD5:
                            nextAddress = Context.Ebp;
                            break;
                        case 0xD6:
                            nextAddress = Context.Esi;
                            break;
                        case 0xD7:
                            nextAddress = Context.Edi;
                            break;
                    }
                }

                if (ldata.modrm >= 0xE0 && ldata.modrm <= 0xE7)
                {
                    // we have a JMP REGISTER
                    switch (ldata.modrm)
                    {
                        case 0xE0:
                            nextAddress = Context.Eax;
                            break;
                        case 0xE1:
                            nextAddress = Context.Ecx;
                            break;
                        case 0xE2:
                            nextAddress = Context.Edx;
                            break;
                        case 0xE3:
                            nextAddress = Context.Ebx;
                            break;
                        case 0xE4:
                            nextAddress = Context.Esp;
                            break;
                        case 0xE5:
                            nextAddress = Context.Ebp;
                            break;
                        case 0xE6:
                            nextAddress = Context.Esi;
                            break;
                        case 0xE7:
                            nextAddress = Context.Edi;
                            break;
                    }
                }
            }

            updateContexts();
            SetBreakpoint(nextAddress);

            Continue();

            ClearBreakpoint(nextAddress);

            return this;

        }

        public NIDebugger SetProcBP(String module, String method)
        {
            SetBreakpoint(FindProcAddress(module, method));
            return this;
        }

        public NIDebugger ClearProcBP(String module, String method)
        {
            ClearBreakpoint(FindProcAddress(module, method));
            return this;
        }

        #endregion

        #region Delegate Methods 
        public NIDebugger While(Func<bool> condition, Action action)
        {
            while (condition())
            {
                action();
            }

            return this;
        }

        public NIDebugger Until(Func<bool> condition, Action action)
        {
            while (condition() != true)
            {
                action();
            }

            return this;
        }

        public NIDebugger Times(uint count, Action action)
        {
            for (uint x = 0; x < count; x++)
            {
                action();
            }
            return this;
        }

        public NIDebugger If(Func<bool> condition, Action action)
        {
            if (condition())
            {
                action();
            }

            return this;
        }
        #endregion

        #endregion



        #region Private Methods

        private void getContexts()
        {
            foreach (ProcessThread currThread in debuggedProcess.Threads)
            {
                NIContext ctx = getContext(currThread.Id);
                if (contexts.ContainsKey(currThread.Id))
                {
                    contexts[currThread.Id] = ctx;
                }
                else
                {
                    contexts.Add(currThread.Id, ctx);
                }
            }
        }

        private void updateContexts()
        {
            var keys = contexts.Keys.ToList<int>();
            for (var x = 0; x < keys.Count; x++ )
            {
                updateContext(keys[x]);
            }
        }

        private void updateContext(int threadId)
        {
            IntPtr hThread = getThreadHandle(threadId);
            Win32.CONTEXT ctx = contexts[threadId].ToWin32Context();
            Win32.SetThreadContext(hThread,ref ctx);
        }

        private NIContext getContext(int threadId)
        {

            IntPtr hThread = getThreadHandle(threadId);

            Win32.CONTEXT ctx = new Win32.CONTEXT();
            ctx.ContextFlags = (uint)Win32.CONTEXT_FLAGS.CONTEXT_ALL;
            Win32.GetThreadContext(hThread, ref ctx);

            return new NIContext(ctx);

        }

        private IntPtr getThreadHandle(int threadId)
        {
            IntPtr handle = threadHandles.ContainsKey(threadId) ? threadHandles[threadId] : new IntPtr(-1);
            if (handle.Equals(-1))
            {
                handle = Win32.OpenThread(Win32.GET_CONTEXT | Win32.SET_CONTEXT, false, (uint)threadId);
                threadHandles.Add(threadId, handle);
            }
            return handle;
        }

        
        private void bw_Continue(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            while (1==1)
            {
                if (debuggedProcess.HasExited)
                {
                    return;
                }
                pauseAllThreads();
                //Console.WriteLine("threads paused");
                foreach (uint address in breakpoints.Keys)
                {
                    foreach (ProcessThread pThread in debuggedProcess.Threads)
                    {
                        if (getContext(pThread.Id).Eip == address)
                        {
                            Console.WriteLine("We hit a breakpoint: " + address.ToString("X"));
                            lastBreakpoint = breakpoints[address];
                            lastBreakpoint.threadId = (uint)pThread.Id;

                            getContexts();

                            e.Cancel = true;
                            mre.Set();
                            return;
                        }
                    }
                }
                resumeAllThreads();
                //Console.WriteLine("threads resumed");
            }
        }

        private void pauseAllThreads()
        {
            foreach (ProcessThread t in debuggedProcess.Threads)
            {
                IntPtr hThread = getThreadHandle(t.Id);
                Win32.SuspendThread(hThread);
            }
        }

        private void resumeAllThreads()
        {
            foreach (ProcessThread t in debuggedProcess.Threads)
            {
                IntPtr hThread = getThreadHandle(t.Id);
                int result = Win32.ResumeThread(hThread);
                while (result > 1)
                {
                    result = Win32.ResumeThread(hThread);
                }
            }
        }
        
        private Win32.MODULEENTRY32 getModule(String modName)
        {
            IntPtr hSnap = Win32.CreateToolhelp32Snapshot(Win32.SnapshotFlags.NoHeaps | Win32.SnapshotFlags.Module, (uint) debuggedProcessInfo.dwProcessId);
            Win32.MODULEENTRY32 module = new Win32.MODULEENTRY32();
            module.dwSize = (uint)Marshal.SizeOf(module);
            Win32.Module32First(hSnap, ref module);

            if (module.szModule.Equals(modName,StringComparison.CurrentCultureIgnoreCase))
            {
                return module;
            }

            while (Win32.Module32Next(hSnap,ref module))
            {
                if (module.szModule.Equals(modName, StringComparison.CurrentCultureIgnoreCase))
                {
                    return module;
                }
            }
            module = new Win32.MODULEENTRY32();
            Win32.CloseHandle(hSnap);
            return module;
        }

        

        private int getCurrentThreadId()
        {
            int thread;
            // determine the thread
            if (lastBreakpoint != null)
            {
                thread = (int)lastBreakpoint.threadId;
            }
            else
            {
                thread = debuggedProcessInfo.dwThreadId;
            }
            return thread;
        }

       

        private bool evalJcc(byte b)
        {
            if ((b & 0x80) == 0x80) { b -= 0x80;}
            if ((b & 0x70) == 0x70) { b -= 0x70;}

            bool willJump = false;
            // determine if we will jump
            switch(b)
            {
                case 0:
                    willJump = Context.GetFlag(ContextFlag.OVERFLOW);
                    break;
                case 1:
                    willJump = !Context.GetFlag(ContextFlag.OVERFLOW);
                    break;
                case 2:
                    willJump = Context.GetFlag(ContextFlag.CARRY);
                    break;
                case 3:
                    willJump = !Context.GetFlag(ContextFlag.CARRY);
                    break;
                case 4:
                    willJump = Context.GetFlag(ContextFlag.ZERO);
                    break;
                case 5:
                    willJump = !Context.GetFlag(ContextFlag.ZERO);
                    break;
                case 6:
                    willJump = Context.GetFlag(ContextFlag.CARRY) || Context.GetFlag(ContextFlag.ZERO);
                    break;
                case 7:
                    willJump = (!Context.GetFlag(ContextFlag.CARRY)) && (!Context.GetFlag(ContextFlag.ZERO));
                    break;
                case 8:
                    willJump = Context.GetFlag(ContextFlag.SIGN);
                    break;
                case 9:
                    willJump = !Context.GetFlag(ContextFlag.SIGN);
                    break;
                case 0x0a:
                    willJump = Context.GetFlag(ContextFlag.PARITY);
                    break;
                case 0x0b:
                    willJump = !Context.GetFlag(ContextFlag.PARITY);
                    break;
                case 0x0c:
                    willJump = Context.GetFlag(ContextFlag.SIGN) != Context.GetFlag(ContextFlag.OVERFLOW);
                    break;
                case 0x0d:
                    willJump = Context.GetFlag(ContextFlag.SIGN) == Context.GetFlag(ContextFlag.OVERFLOW);
                    break;
                case 0x0e:
                    willJump = Context.GetFlag(ContextFlag.ZERO) || (Context.GetFlag(ContextFlag.SIGN) != Context.GetFlag(ContextFlag.OVERFLOW));
                    break;
                case 0x0f:
                    willJump = !Context.GetFlag(ContextFlag.ZERO) && (Context.GetFlag(ContextFlag.SIGN) == Context.GetFlag(ContextFlag.OVERFLOW));
                    break;
                case 0xE3:
                    willJump = Context.Ecx == 0;
                    break;
            }
            return willJump;
        }

        #endregion

    }

    public class DumpOptions
    {
        public bool ChangeEP = false;
        public uint EntryPoint = 0;
        public bool PerformDumpFix = true;
        public String OutputPath = "";
    }
    public enum ContextFlag : uint
    {
        CARRY = 0x01,
        PARITY = 0x04,
        ADJUST = 0x10,
        ZERO = 0x40,
        SIGN = 0x80,
        DIRECTION = 0x400,
        OVERFLOW = 0x800
    }
    public class NIContext
    {
        public uint ContextFlags; //set this to an appropriate value 
        // Retrieved by CONTEXT_DEBUG_REGISTERS
        protected uint Dr0;
        protected uint Dr1;
        protected uint Dr2;
        protected uint Dr3;
        protected uint Dr6;
        protected uint Dr7;
        // Retrieved by CONTEXT_FLOATING_POINT
        protected Win32.FLOATING_SAVE_AREA FloatSave;
        // Retrieved by CONTEXT_SEGMENTS
        protected uint SegGs;
        protected uint SegFs;
        protected uint SegEs;
        protected uint SegDs;
        // Retrieved by CONTEXT_INTEGER
        public uint Edi;
        public uint Esi;
        public uint Ebx;
        public uint Edx;
        public uint Ecx;
        public uint Eax;
        // Retrieved by CONTEXT_CONTROL
        public uint Ebp;
        public uint Eip;
        protected uint SegCs;
        private uint EFlags;
        public uint Esp;
        protected uint SegSs;
        // Retrieved by CONTEXT_EXTENDED_REGISTERS
        public byte[] ExtendedRegisters;


        public bool GetFlag(ContextFlag i)
        {
            return (this.EFlags & (uint)i) == (uint)i;
        }

        public void SetFlag(ContextFlag i, bool value)
        {
            this.EFlags -= GetFlag(i) ? (uint)i : 0;

            this.EFlags ^= (value) ? (uint)i : 0;

        }

        public Win32.CONTEXT ToWin32Context()
        {
            Win32.CONTEXT ctx = new Win32.CONTEXT();
            ctx.ContextFlags = ContextFlags;
            ctx.Dr0 = Dr0;
            ctx.Dr1 = Dr1;
            ctx.Dr2 = Dr2;
            ctx.Dr3 = Dr3;
            ctx.Dr6 = Dr6;
            ctx.Dr7 = Dr7;

            ctx.FloatSave = FloatSave;
            ctx.SegGs = SegGs;
            ctx.SegFs = SegFs;
            ctx.SegEs = SegEs;
            ctx.SegDs = SegDs;
            ctx.Edi = Edi;
            ctx.Esi = Esi;
            ctx.Ebx = Ebx;
            ctx.Edx = Edx;
            ctx.Ecx = Ecx;
            ctx.Eax = Eax;
            ctx.Ebp = Ebp;
            ctx.Eip = Eip;
            ctx.SegCs = SegCs;
            ctx.EFlags = EFlags;
            ctx.Esp = Esp;
            ctx.SegSs = SegSs;
            ctx.ExtendedRegisters = ExtendedRegisters;

            return ctx;
        }

        public NIContext(Win32.CONTEXT ctx)
        {
            ContextFlags = ctx.ContextFlags;
            Dr0 = ctx.Dr0;
            Dr1 = ctx.Dr1;
            Dr2 = ctx.Dr2;
            Dr3 = ctx.Dr3;
            Dr6 = ctx.Dr6;
            Dr7 = ctx.Dr7;
            FloatSave = ctx.FloatSave;
            SegGs = ctx.SegGs;
            SegFs = ctx.SegFs;
            SegEs = ctx.SegEs;
            SegDs = ctx.SegDs;
            Edi = ctx.Edi;
            Esi = ctx.Esi;
            Ebx = ctx.Ebx;
            Edx = ctx.Edx;
            Ecx = ctx.Ecx;
            Eax = ctx.Eax;
            Ebp = ctx.Ebp;
            Eip = ctx.Eip;
            SegCs = ctx.SegCs;
            EFlags = ctx.EFlags;
            Esp = ctx.Esp;
            SegSs = ctx.SegSs;
            ExtendedRegisters = ctx.ExtendedRegisters;
        }
    }

    public class NIStartupOptions
    {
        public string executable { get; set; }
        public string commandLine { get; set; }
        public bool resumeOnCreate { get; set; }

        public bool patchTickCount { get; set; }
        public bool incrementTickCount { get; set; }
    }

    public class NIBreakPoint
    {
        public uint bpAddress { get; set; }
        public byte[] originalBytes {get; set;}

        public uint threadId { get; set; }
    }


}
