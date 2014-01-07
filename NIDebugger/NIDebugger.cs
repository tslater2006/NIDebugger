using LDASM_Sharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace NonIntrusive
{
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
        public  uint Esp;
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
    public class NIDebugger
    {
        public bool AutoClearBP = false;
        public bool StepIntoCalls = true;

        public NIContext Context
        {
            get
            {
                
                return contexts[getCurrentThreadId()];
            }
        }
        
        Dictionary<uint, NIBreakPoint> breakpoints = new Dictionary<uint, NIBreakPoint>();
        Dictionary<int, IntPtr> threadHandles = new Dictionary<int, IntPtr>();
        public Dictionary<int, NIContext> contexts = new Dictionary<int,NIContext>();
        private static ManualResetEvent mre = new ManualResetEvent(false);
        BackgroundWorker bwContinue;
        Win32.PROCESS_INFORMATION debuggedProcessInfo;

        NIBreakPoint lastBreakpoint; 

        Process debuggedProcess;
        LDASM lde = new LDASM();

        private byte[] BREAKPOINT = new byte[] { 0xEB, 0xFE };
        


        public NIDebugger()
        {
        }


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

        public NIContext getContext()
        {
            return contexts[debuggedProcessInfo.dwThreadId];
        }


        public void updateContexts()
        {
            var keys = contexts.Keys.ToList<int>();
            for (var x = 0; x < keys.Count; x++ )
            {
                updateContext(keys[x]);
            }
        }

        public void updateContext(int threadId)
        {
            IntPtr hThread = getThreadHandle(threadId);
            Win32.CONTEXT ctx = contexts[threadId].ToWin32Context();
            Win32.SetThreadContext(hThread,ref ctx);
        }

        public NIContext getContext(int threadId)
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

        public Process Execute(NIStartupOptions opts)
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
                NIContext ctx = getContext();
                uint OEP = ctx.Eax;
                NIBreakPoint bp = setBreakpoint(OEP);
                Continue();
                clearBreakpoint(bp);

                Console.WriteLine("We should be at OEP");

            }

            return debuggedProcess;

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

        public void Continue()
        {
            updateContexts();

            bwContinue = new BackgroundWorker();
            bwContinue.DoWork += bw_Continue;

            mre.Reset();
            bwContinue.RunWorkerAsync();
            mre.WaitOne();

            if (AutoClearBP)
            {
                clearBreakpoint(lastBreakpoint);
            }
        }

        public void Detach()
        {
            pauseAllThreads();
            List<uint> bpAddresses = breakpoints.Keys.ToList();
            foreach(uint addr in bpAddresses)
            {
                clearBreakpoint(addr);
            }
            updateContexts();
            resumeAllThreads();
        }

        public NIBreakPoint setBreakpoint(uint address)
        {
            if (breakpoints.Keys.Contains(address) == false)
            {
                NIBreakPoint bp = new NIBreakPoint() { bpAddress = address, originalBytes = getData(address, 2), threadId = 0 };
                breakpoints.Add(address, bp);
                setData(address, BREAKPOINT);

                return bp;
            }
            return null;
        }

        public bool clearBreakpoint(NIBreakPoint bp)
        {
            return clearBreakpoint(bp.bpAddress);
        }
        public bool clearBreakpoint(uint address)
        {
            if (breakpoints.Keys.Contains(address))
            {

                setData(address, breakpoints[address].originalBytes);
                breakpoints.Remove(address);
                return true;
            }
            else
            {
                return false;
            }
        }

        public byte[] getData(uint address, int length)
        {
            int numRead = 0;
            byte[] data = new byte[length];
            Win32.ReadProcessMemory(debuggedProcessInfo.hProcess, (int)address, data, length, ref numRead);

            if (numRead == length)
            {
                return data;
            }else
            {
                return null;
            }
        }

        public bool setData(uint address, byte[] data)
        {
            Win32.MEMORY_BASIC_INFORMATION mbi = new Win32.MEMORY_BASIC_INFORMATION();

            Win32.VirtualQueryEx(debuggedProcessInfo.hProcess, (int)address, ref mbi, Marshal.SizeOf(mbi));
            uint oldProtect = 0;

            Win32.VirtualProtectEx((IntPtr)debuggedProcessInfo.hProcess, (IntPtr)mbi.BaseAddress, (UIntPtr)mbi.RegionSize, (uint)Win32.AllocationProtectEnum.PAGE_EXECUTE_READWRITE, out oldProtect);

            int numWritten = 0;
            Win32.WriteProcessMemory(debuggedProcessInfo.hProcess, (int)address, data, data.Length, ref numWritten);

            Win32.VirtualProtectEx((IntPtr)debuggedProcessInfo.hProcess, (IntPtr)mbi.BaseAddress, (UIntPtr)mbi.RegionSize, oldProtect, out oldProtect);

            return numWritten == data.Length;
        }

        public bool writeString(uint address, String str, Encoding encode)
        {
            return setData(address, encode.GetBytes(str));
        }

        public String readString(uint address, int maxLength, Encoding encode)
        {
            byte[] data = getData(address, maxLength);

            if (encode.IsSingleByte)
            {
                for (int x = 0; x < data.Length - 1; x++)
                {
                    if (data[x] == 0)
                    {
                        return encode.GetString(data, 0, x + 1);
                    }
                }
            }
            else
            {
                for (int x = 0; x < data.Length - 2; x++)
                {
                    if (data[x] + data[x+1] == 0)
                    {
                        return encode.GetString(data, 0, x + 1);
                    }
                }
            }
            return encode.GetString(data);
        }

        public static void main()
        {
            new NIStartupOptions() { executable = "", commandLine = "", resumeOnCreate = true };
        }

        public uint allocateMemory(uint size)
        {
            IntPtr memLocation = Win32.VirtualAllocEx((IntPtr)debuggedProcessInfo.hProcess, new IntPtr(), size, (uint)Win32.StateEnum.MEM_RESERVE | (uint)Win32.StateEnum.MEM_COMMIT, (uint) Win32.AllocationProtectEnum.PAGE_EXECUTE_READWRITE);

            return (uint)memLocation;
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

        public uint getProcAddress (String modName, String method)
        {
            Win32.MODULEENTRY32 module = getModule(modName);

            if (module.dwSize == 0)
            {
                Console.WriteLine("Failed to find module");
                throw new Exception("Target doesn't have module: " + modName + " loaded.");
            }
            uint modBase = (uint)module.modBaseAddr;

            uint peAddress = getDword(modBase + 0x3c);

            uint exportTableAddress = getDword(modBase + peAddress + 0x78);
            uint exportTableSize = getDword(modBase + peAddress + 0x7C);

            byte[] exportTable = getData(modBase + exportTableAddress, (int)exportTableSize);
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

        public uint getInstrLength()
        {
            
            uint address = contexts[getCurrentThreadId()].Eip;

            byte[] data = getData(address, 16);
            if (breakpoints.ContainsKey(address) == true)
            {
                Array.Copy(breakpoints[address].originalBytes, data, 2);
            }

            return lde.ldasm(data, 0, false).size;
        }

        public String getInstrOpcodes()
        {
            uint address = contexts[getCurrentThreadId()].Eip;
            byte[] data = getData(address, 16);

            if (breakpoints.ContainsKey(address) == true)
            {
                Array.Copy(breakpoints[address].originalBytes, data, 2);
            }

            uint size = lde.ldasm(data, 0, false).size;

            return BitConverter.ToString(data, 0, (int)size).Replace("-", " ");
        }

        public void SingleStep(int number)
        {
            for (int x = 0; x < number; x++)
            {
                SingleStep();
            }
        }

        public void SingleStep()
        {
            updateContexts();
            uint address = Context.Eip;
            byte[] data = getData(address, 16);

            if (breakpoints.ContainsKey(address) == true)
            {
                Array.Copy(breakpoints[address].originalBytes, data, 2);
                clearBreakpoint(breakpoints[address]);
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
                int offset = BitConverter.ToInt32(data,ldata.imm_offset);
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

            if (ldata.opcd_size == 2 && ((data[ldata.opcd_offset] & 0x0F) == 0x70 || (data[ldata.opcd_offset + 1] & 0x80) == 0x80))
            {
                // we have a 2 byte jcc here

                bool willJump = evalJcc(data[ldata.opcd_offset + 1]);

                if (willJump)
                {
                    int offset = BitConverter.ToInt32(data, ldata.imm_offset);
                    nextAddress = (uint)((Context.Eip + offset) + ldata.size);
                }
            }


            updateContexts();
            NIBreakPoint stepBP = setBreakpoint(nextAddress);
            Continue();

            clearBreakpoint(stepBP);
            
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

        public uint getDword(uint address)
        {
            byte[] data = getData(address, 4);
            return BitConverter.ToUInt32(data, 0);
        }

        public void writeDword(uint address, uint value)
        {
            byte[] data = BitConverter.GetBytes(value);
            setData(address, data);
        }

        public uint getStackValue(uint espOffset)
        {
            return getDword(Context.Esp + espOffset);
        }

        public void setStackValue(uint espOffset, uint value)
        {
            writeDword(Context.Esp + espOffset, value);
        }

    }

    

    public class NIStartupOptions
    {
        public string executable { get; set; }
        public string commandLine { get; set; }
        public bool resumeOnCreate { get; set; }

    }

    public class NIBreakPoint
    {
        public uint bpAddress { get; set; }
        public byte[] originalBytes {get; set;}

        public uint threadId { get; set; }
    }

    public class NIBreakpPointEventArgs : EventArgs
    {
        public uint address { get; set; }
        public NIBreakPoint breakpoint {get;set;}

    }


}
