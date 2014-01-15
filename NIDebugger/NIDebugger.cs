using LDASM_Sharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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

                return _context;
            }
        }

        #endregion

        #region Private Variables

        Dictionary<uint, NIBreakPoint> breakpoints = new Dictionary<uint, NIBreakPoint>();
        Dictionary<int, IntPtr> threadHandles = new Dictionary<int, IntPtr>();
        private NIContext _context;
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

        public NIDebugger GetFlag(NIContextFlag flag, out bool value)
        {
            value = _context.GetFlag(flag);
            return this;
        }
        public NIDebugger SetFlag(NIContextFlag flag, bool value)
        {
            _context.SetFlag(flag, value);
            return this;
        }

        public NIDebugger GetRegister(NIRegister reg, out uint value)
        {
            switch(reg)
            {
                case NIRegister.EAX:
                    value = Context.Eax;
                    break;
                case NIRegister.ECX:
                    value = Context.Ecx;
                    break;
                case NIRegister.EDX:
                    value = Context.Edx;
                    break;
                case NIRegister.EBX:
                    value = Context.Ebx;
                    break;
                case NIRegister.ESP:
                    value = Context.Esp;
                    break;
                case NIRegister.EBP:
                    value = Context.Ebp;
                    break;
                case NIRegister.ESI:
                    value = Context.Esi;
                    break;
                case NIRegister.EDI:
                    value = Context.Edi;
                    break;
                case NIRegister.EIP:
                    value = Context.Eip;
                    break;
                default:
                    value = 0;
                    break;
            }
            return this;
        }
        public NIDebugger SetRegister(NIRegister reg, uint value)
        {
            switch (reg)
            {
                case NIRegister.EAX:
                    Context.Eax = value;
                    break;
                case NIRegister.ECX:
                    Context.Ecx = value;
                    break;
                case NIRegister.EDX:
                    Context.Edx = value;
                    break;
                case NIRegister.EBX:
                    Context.Ebx = value;
                    break;
                case NIRegister.ESP:
                    Context.Esp = value;
                    break;
                case NIRegister.EBP:
                    Context.Ebp = value;
                    break;
                case NIRegister.ESI:
                    Context.Esi = value;
                    break;
                case NIRegister.EDI:
                    Context.Edi = value;
                    break;
                case NIRegister.EIP:
                    Context.Eip = value;
                    break;
                default:
                    value = 0;
                    break;
            }
            return this;
        }
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
        public NIDebugger InjectASM(uint address, String asmString)
        {
            return WriteHexString(address,asmString);
        }
        public NIDebugger WriteHexString(uint address, String hexString)
        {
            byte[] data = new byte[hexString.Length / 2];

            for (int x = 0; x < hexString.Length; x += 2)
            {
                    data[x / 2] = Byte.Parse(hexString.Substring(x, 2), NumberStyles.HexNumber);
            }

            return WriteData(address, data);
        }
        public NIDebugger SearchMemory(NISearchOptions opts , out uint[] results)
        {
            

            if (debuggedProcess == null || debuggedProcess.HasExited)
            {
                results = null;
                return this;
            }

            if (opts.SearchImage)
            {
                opts.StartAddress = (uint)debuggedProcess.Modules[0].BaseAddress;
                opts.EndAddress = opts.StartAddress + (uint)debuggedProcess.Modules[0].ModuleMemorySize;
            }

            //end &= -4096;
            Win32.MEMORY_BASIC_INFORMATION mbi = default(Win32.MEMORY_BASIC_INFORMATION);
            uint i = opts.StartAddress;
            List<uint> list = new List<uint>();
            while (i < opts.EndAddress)
            {
                Win32.VirtualQueryEx(debuggedProcessInfo.hProcess, (int)i, ref mbi, Marshal.SizeOf(mbi));
                if (mbi.State == Win32.StateEnum.MEM_RESERVE || mbi.State == Win32.StateEnum.MEM_FREE)
                {
                    i += mbi.RegionSize;
                }
                else
                {
                    byte[] array = new byte[mbi.RegionSize];
                    ReadData(i, (int)mbi.RegionSize,out array);

                    for (int j = 0; j < array.Length - opts.SearchBytes.Length; j++)
                    {
                        if (array[j] == opts.SearchBytes[0] || opts.ByteMask[0] == 1)
                        {
                            int num = 1;
                            while (num < opts.SearchBytes.Length && (array[j + num] == opts.SearchBytes[num] || opts.ByteMask[num] == 1))
                            {
                                if (num + 1 == opts.SearchBytes.Length)
                                {
                                    list.Add(i + (uint)j);
                                    if (list.Count == opts.MaxOccurs && opts.MaxOccurs != -1)
                                    { 
                                        results = list.ToArray();
                                        return this;
                                    }
                                }
                                num++;
                            }
                        }
                    }
                    i += mbi.RegionSize;
                }
            }
            results = list.ToArray();
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
        public NIDebugger DumpProcess(NIDumpOptions opts)
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
                getContext(getCurrentThreadId());

                uint OEP = _context.Eax;
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
            getContext(getCurrentThreadId());

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
            updateContext(getCurrentThreadId());
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

            uint address = _context.Eip;

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
            uint address = _context.Eip;
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
            getContext(getCurrentThreadId());
            uint address = Context.Eip;
            if (address == 0x77d2a578)
            {
                int i = 0;
            }
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

            if ((data[ldata.opcd_offset] == 0xE2))
            {
                // LOOP
                uint ecx = 0;
                GetRegister(NIRegister.ECX, out ecx);
                if (ecx == 1)
                {
                    // this instruction will make ECX 0, so we fall thru the jump now
                    nextAddress = (uint)(Context.Eip + ldata.size);

                }
                else if (ecx > 1)
                {
                    // this instruction will decrement ECX but it wont be 0 yet, so jump!
                    sbyte disp = (sbyte)data[1];
                    nextAddress = (uint)(Context.Eip + disp) + ldata.size;
                }


            }

            if ((data[ldata.opcd_offset] == 0xE0))
            {
                //LOOPNZ LOOPNE
                int i = 0;
            }

            if ((data[ldata.opcd_offset] == 0xE1))
            {
                //LOOPNZ LOOPNE
                int i = 0;
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

            if (ldata.opcd_size == 1 && ((data[ldata.opcd_offset] >= 0x70 && (data[ldata.opcd_offset] <= 0x7F)) || (data[ldata.opcd_offset] == 0xE3)))
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
                // let's parse ModRM!
                var reg2 = (ldata.modrm & 0x38) >> 3;
                var mod = (ldata.modrm & 0xC0) >> 6;
                var reg1 = (ldata.modrm & 0x7);
                bool addressSet = false;
                if (reg2 == 2)
                {
                    if (StepIntoCalls == false)
                    {
                        nextAddress = (uint)Context.Eip + ldata.size;
                        addressSet = true;
                    }

                    Console.Write( "RegOp tells me this is a CALL\r\n");
                }
                else if (reg2 == 4)
                {
                    Console.Write("RegOp tells me this is a JMP\r\n");
                }
                else
                {
                    nextAddress = (uint)Context.Eip + ldata.size;
                    addressSet = true;
                }

                if (addressSet == false)
                {
                    if (reg1 == 4)
                    {
                        //txtFacts.Text += "Reg1 is a 4 which means there is a SIB byte\r\n";
                        var ss = (ldata.sib & 0xC0) >> 6;
                        var index = (ldata.sib & 0x38) >> 3;
                        var Base = (ldata.sib & 0x07);


                        int scale = (int)Math.Pow(2, ss);
                        nextAddress = (uint)GetRegisterByNumber(index) * (uint)scale;
                        if (Base == 5)
                        {
                            if (mod == 0)
                            {
                                nextAddress = (uint)((int)nextAddress + BitConverter.ToInt32(data, ldata.disp_offset));
                            }
                            else if (mod == 1)
                            {
                                nextAddress += GetRegisterByNumber(Base);
                                nextAddress = (uint)((int)(nextAddress) + (sbyte)data[ldata.disp_offset]);
                            }
                            else if (mod == 2)
                            {
                                nextAddress += GetRegisterByNumber(Base);
                                nextAddress = (uint)((int)nextAddress + BitConverter.ToInt32(data, ldata.disp_offset));
                            }
                        }

                    }
                    else
                    {
                        if (mod == 0)
                        {
                            if (reg1 != 5)
                            {
                                nextAddress = GetRegisterByNumber(reg1);     
                            }
                            else
                            {
                                nextAddress = (uint)BitConverter.ToInt32(data, ldata.disp_offset);
                            }

                        }
                        else if (mod == 1)
                        {
                            nextAddress = GetRegisterByNumber(reg1);
                            nextAddress = (uint)((int)(nextAddress) + (sbyte)data[ldata.disp_offset]);

                        }
                        else if (mod == 2)
                        {
                            nextAddress = GetRegisterByNumber(reg1);
                            nextAddress = (uint)((int)nextAddress + BitConverter.ToInt32(data, ldata.disp_offset));
                        }
                    }
                    if (mod != 3)
                    {
                        ReadDWORD(nextAddress, out nextAddress);
                    }
                    
                    Console.WriteLine("Next Address: " + nextAddress.ToString("X8"));
                }
            }

            updateContext(getCurrentThreadId());
            SetBreakpoint(nextAddress);

            Continue();

            ClearBreakpoint(nextAddress);

            return this;

        }

        private uint GetRegisterByNumber(int val)
        {
            switch (val)
            {
                case 0:
                    return Context.Eax;
                case 1:
                    return Context.Ecx;
                case 2:
                    return Context.Edx;
                case 3:
                    return Context.Ebx;
                case 4:
                    return 0;
                case 5:
                    return Context.Ebp;
                case 6:
                    return Context.Esi;
                case 7:
                    return Context.Edi;
                default:
                    return 0;
            }
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

        private void updateContext(int threadId)
        {
            IntPtr hThread = getThreadHandle(threadId);
            Win32.CONTEXT ctx = _context.ToWin32Context();
            Win32.SetThreadContext(hThread,ref ctx);
        }

        private NIContext getContext(int threadId)
        {

            IntPtr hThread = getThreadHandle(threadId);

            Win32.CONTEXT ctx = new Win32.CONTEXT();
            ctx.ContextFlags = (uint)Win32.CONTEXT_FLAGS.CONTEXT_ALL;
            Win32.GetThreadContext(hThread, ref ctx);
            _context = new NIContext(ctx);
            return _context;

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

                            getContext(pThread.Id);

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
                    willJump = Context.GetFlag(NIContextFlag.OVERFLOW);
                    break;
                case 1:
                    willJump = !Context.GetFlag(NIContextFlag.OVERFLOW);
                    break;
                case 2:
                    willJump = Context.GetFlag(NIContextFlag.CARRY);
                    break;
                case 3:
                    willJump = !Context.GetFlag(NIContextFlag.CARRY);
                    break;
                case 4:
                    willJump = Context.GetFlag(NIContextFlag.ZERO);
                    break;
                case 5:
                    willJump = !Context.GetFlag(NIContextFlag.ZERO);
                    break;
                case 6:
                    willJump = Context.GetFlag(NIContextFlag.CARRY) || Context.GetFlag(NIContextFlag.ZERO);
                    break;
                case 7:
                    willJump = (!Context.GetFlag(NIContextFlag.CARRY)) && (!Context.GetFlag(NIContextFlag.ZERO));
                    break;
                case 8:
                    willJump = Context.GetFlag(NIContextFlag.SIGN);
                    break;
                case 9:
                    willJump = !Context.GetFlag(NIContextFlag.SIGN);
                    break;
                case 0x0a:
                    willJump = Context.GetFlag(NIContextFlag.PARITY);
                    break;
                case 0x0b:
                    willJump = !Context.GetFlag(NIContextFlag.PARITY);
                    break;
                case 0x0c:
                    willJump = Context.GetFlag(NIContextFlag.SIGN) != Context.GetFlag(NIContextFlag.OVERFLOW);
                    break;
                case 0x0d:
                    willJump = Context.GetFlag(NIContextFlag.SIGN) == Context.GetFlag(NIContextFlag.OVERFLOW);
                    break;
                case 0x0e:
                    willJump = Context.GetFlag(NIContextFlag.ZERO) || (Context.GetFlag(NIContextFlag.SIGN) != Context.GetFlag(NIContextFlag.OVERFLOW));
                    break;
                case 0x0f:
                    willJump = !Context.GetFlag(NIContextFlag.ZERO) && (Context.GetFlag(NIContextFlag.SIGN) == Context.GetFlag(NIContextFlag.OVERFLOW));
                    break;
                case 0xE3:
                    willJump = Context.Ecx == 0;
                    break;
            }
            return willJump;
        }

        #endregion

    }

    public class NIDumpOptions
    {
        public bool ChangeEP = false;
        public uint EntryPoint = 0;
        public bool PerformDumpFix = true;
        public String OutputPath = "";
    }
    public enum NIContextFlag : uint
    {
        CARRY = 0x01, PARITY = 0x04, ADJUST = 0x10, ZERO = 0x40, SIGN = 0x80, DIRECTION = 0x400, OVERFLOW = 0x800
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


        public bool GetFlag(NIContextFlag i)
        {
            return (this.EFlags & (uint)i) == (uint)i;
        }

        public void SetFlag(NIContextFlag i, bool value)
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

    public class NISearchOptions
    {
        public String SearchString
        {
            get
            {
                return _searchString;
            }
            set
            {
                _searchString = value.Replace(" ","");

                _searchBytes = new byte[_searchString.Length / 2];
                _maskBytes = new byte[_searchString.Length / 2];

                for (int x = 0; x < _searchString.Length; x += 2)
                {
                    if (_searchString.ElementAt(x) == '?' && _searchString.ElementAt(x + 1) == '?')
                    {
                        _maskBytes[x/2] = 1;
                    }
                    else
                    {
                        _searchBytes[x/2] = Byte.Parse(_searchString.Substring(x, 2), NumberStyles.HexNumber);
                    }
                }

            }
        }
        private String _searchString;
        public byte[] SearchBytes { get { return _searchBytes; } }
        public byte[] ByteMask { get { return _maskBytes; } }

        private byte[] _searchBytes;
        private byte[] _maskBytes;



        public uint StartAddress { get; set; }
        public uint EndAddress { get; set; }
        public int MaxOccurs { get; set; }
        public Boolean SearchImage { get; set; }
    }

    public enum NIRegister
    {
        EAX, ECX, EDX, EBX, ESP, EBP, ESI, EDI, EIP
    }
}
