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
    /// <summary>
    /// The base debugger class. This class is responsible for all debugging actions on an executable.
    /// </summary>
    public class NIDebugger
    {
        #region Properties

        /// <summary>
        /// Determines if a BreakPoint should be cleared automatically once it is hit. The default is True
        /// </summary>
        public bool AutoClearBP = true;

        /// <summary>
        /// Determines if SingleStep should step into a call or over it. The default is False, meaning calls should be stepped over.
        /// </summary>
        public bool StepIntoCalls = false;
        /// <summary>
        /// Gets the debugged process.
        /// </summary>
        /// <value>
        /// The Process object relating to the process being debugged.
        /// </value>
        public Process Process
        {
            get
            {
                return debuggedProcess;
            }
        }

        /// <summary>
        /// Gets the debugged process's ImageBase.
        /// </summary>
        /// <value>
        /// The ImageBase for the debugged process.
        /// </value>
        public uint ProcessImageBase
        {
            get
            {
                return (uint)debuggedProcess.MainModule.BaseAddress;
            }
        }

        /// <summary>
        /// Gets the context of the current thread. The current thread is determined by which thread hit the current BreakPoint
        /// </summary>
        /// <value>
        /// NIContext object used to read/write register values.
        /// </value>
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

        /// <summary>
        /// Gets the current value of the requested flag.
        /// </summary>
        /// <param name="flag">The flag.</param>
        /// <param name="value">Output variable that will contain the value of the flag.</param>
        /// <returns>Reference to the NIDebugger object</returns>
        public NIDebugger GetFlag(NIContextFlag flag, out bool value)
        {
            value = _context.GetFlag(flag);
            return this;
        }
        /// <summary>
        /// Sets the value of the requested flag.
        /// </summary>
        /// <param name="flag">The flag</param>
        /// <param name="value">What the new value of the flag should be.</param>
        /// <returns>Reference to the NIDebugger object</returns>
        public NIDebugger SetFlag(NIContextFlag flag, bool value)
        {
            _context.SetFlag(flag, value);
            return this;
        }

        /// <summary>
        /// Gets the requested register value from the current context.
        /// </summary>
        /// <param name="reg">The register.</param>
        /// <param name="value">Output variable that will contain the requested value.</param>
        /// <returns>Reference to the NIDebugger object</returns>
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
        /// <summary>
        /// Sets the requested register value for the current context.
        /// </summary>
        /// <param name="reg">The register.</param>
        /// <param name="value">The new register value.</param>
        /// <returns>Reference to the NIDebugger object</returns>
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
        /// <summary>
        /// Reads a WORD value from a given address in the debugged process.
        /// </summary>
        /// <param name="address">The address to begin reading the WORD</param>
        /// <param name="value">Output variable that will contain the requested value.</param>
        /// <returns>Reference to the NIDebugger object</returns>
        public NIDebugger ReadWORD(uint address, out UInt16 value)
        {
            byte[] data;
            ReadData(address, 2, out data);
            value = BitConverter.ToUInt16(data, 0);
            return this;
        }

        /// <summary>
        /// Reads binary data from the debugged process, starting at a given address and reading a given amount of bytes.
        /// </summary>
        /// <param name="address">The address to begin reading.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="output">The output variable that will contain the read data.</param>
        /// <returns></returns>
        public NIDebugger ReadData(uint address, int length, out byte[] output)
        {
            int numRead = 0;
            byte[] data = new byte[length];
            Win32.ReadProcessMemory(debuggedProcessInfo.hProcess, (int)address, data, length, ref numRead);

            output = data;

            return this;
        }
        /// <summary>
        /// Legacy method that wraps WriteHexString
        /// </summary>
        /// <param name="address">The address to begin writing the data.</param>
        /// <param name="asmString">The hexidecimal string representing the data to be written.</param>
        /// <returns></returns>
        public NIDebugger InjectASM(uint address, String asmString)
        {
            return WriteHexString(address,asmString);
        }

        /// <summary>
        /// Parses a hexidecimal string into its equivalent bytes and writes the data to a given address in the debugged process.
        /// </summary>
        /// <param name="address">The address to begin writing the data.</param>
        /// <param name="asmString">The hexidecimal string representing the data to be written.</param>
        /// <returns></returns>
        public NIDebugger WriteHexString(uint address, String hexString)
        {
            byte[] data = new byte[hexString.Length / 2];

            for (int x = 0; x < hexString.Length; x += 2)
            {
                    data[x / 2] = Byte.Parse(hexString.Substring(x, 2), NumberStyles.HexNumber);
            }

            return WriteData(address, data);
        }

        /// <summary>
        /// Searches the memory space of the debugged process.
        /// </summary>
        /// <param name="opts">The SearchOptions to be used to perform the search.</param>
        /// <param name="results">The output array that will hold addresses where a match was found.</param>
        /// <returns></returns>
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

        /// <summary>
        /// Writes data to the debugged process at a given address.
        /// </summary>
        /// <param name="address">The address to write the data.</param>
        /// <param name="data">The data to be written.</param>
        /// <returns></returns>
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

        /// <summary>
        /// Writes a String to a given address in the debugged process, using the specificied string encoding.
        /// </summary>
        /// <param name="address">The address to write the String.</param>
        /// <param name="str">The String to be written.</param>
        /// <param name="encode">The encoding that should be used for the String.</param>
        /// <returns></returns>
        public NIDebugger WriteString(uint address, String str, Encoding encode)
        {
            return WriteData(address, encode.GetBytes(str));
        }

        /// <summary>
        /// Reads a String from a given address in the debugged process, using the specificied string encoding.
        /// </summary>
        /// <param name="address">The address to begin reading the String.</param>
        /// <param name="maxLength">The maximum length of the String to be read.</param>
        /// <param name="encode">The encoding that the String uses.</param>
        /// <param name="value">The output variable that will hold the read value.</param>
        /// <returns></returns>
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
        /// <summary>
        /// Reads a DWORD value from the debugged process at a given address.
        /// </summary>
        /// <param name="address">The address to begin reading the DWORD value</param>
        /// <param name="value">The output variable that will hold the read value.</param>
        /// <returns></returns>
        public NIDebugger ReadDWORD(uint address, out uint value)
        {
            byte[] data;
            ReadData(address, 4, out data);
            value = BitConverter.ToUInt32(data, 0);
            return this;
        }

        /// <summary>
        /// Writes a DWORD value to the memory of a debugged process.
        /// </summary>
        /// <param name="address">The address to begin writing the DWORD value.</param>
        /// <param name="value">The value to be written.</param>
        /// <returns></returns>
        public NIDebugger WriteDWORD(uint address, uint value)
        {
            byte[] data = BitConverter.GetBytes(value);
            return WriteData(address, data);
        }

        /// <summary>
        /// Helper method that simplifies reading a DWORD value from the stack.
        /// </summary>
        /// <param name="espOffset">The offset based on ESP to reading.</param>
        /// <param name="value">The output variable that holds the read value.</param>
        /// <returns></returns>
        public NIDebugger ReadStackValue(uint espOffset, out uint value)
        {
            ReadDWORD(Context.Esp + espOffset, out value);
            return this;
        }

        /// <summary>
        /// Helper method that simplifies writing a value to the stack.
        /// </summary>
        /// <param name="espOffset">The offset based on ESP to write.</param>
        /// <param name="value">The value to be written.</param>
        /// <returns></returns>
        public NIDebugger WriteStackValue(uint espOffset, uint value)
        {
            return WriteDWORD(Context.Esp + espOffset, value);
        }
        /// <summary>
        /// Dumps the debugged process from memory to disk.
        /// </summary>
        /// <param name="opts">The DumpOptions to be used.</param>
        /// <returns></returns>
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
        /// <summary>
        /// Inserts a JMP instruction at the given address which lands at the given destination.
        /// </summary>
        /// <param name="address">The address to place the JMP instruction</param>
        /// <param name="destination">The destination the JMP should land at.</param>
        /// <param name="overwrittenOpcodes">The output variable that will contain the overwritten instructions.</param>
        /// <returns></returns>
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
        /// <summary>
        /// Allocates memory in the debugged process.
        /// </summary>
        /// <param name="size">The number of bytes to allocate.</param>
        /// <param name="address">The output variable containing the address of the allocated memory.</param>
        /// <returns></returns>
        public NIDebugger AllocateMemory(uint size, out uint address)
        {
            IntPtr memLocation = Win32.VirtualAllocEx((IntPtr)debuggedProcessInfo.hProcess, new IntPtr(), size, (uint)Win32.StateEnum.MEM_RESERVE | (uint)Win32.StateEnum.MEM_COMMIT, (uint)Win32.AllocationProtectEnum.PAGE_EXECUTE_READWRITE);

            address = (uint) memLocation;
            return this;
        }
        /// <summary>
        /// Finds the address for the given method inside the given module. 
        /// The method requested must be exported to be found. 
        /// This is equivalent to the GetProcAddress() Win32 API but takes into account ASLR by reading the export tables directly from the loaded modules within the debugged process.
        /// </summary>
        /// <param name="modName">Name of the DLL that contains the function (must include extension)</param>
        /// <param name="method">The method whose address is being requested.</param>
        /// <returns>The address of the method if it was found</returns>
        /// <exception cref="System.Exception">Target doesn't have module:  + modName +  loaded.</exception>
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

            }
            return 0;


        }


        #endregion

        #region Control Methods

        /// <summary>
        /// Begins the debugging process of an executable.
        /// </summary>
        /// <param name="opts">The StartupOptions to be used during Execute().</param>
        /// <returns></returns>
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


        /// <summary>
        /// Signals that the debugged process should be resumed, and that the debugger should continue to monitor for BreakPoint hits.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Terminates the debugged process.
        /// </summary>
        public void Terminate()
        {
            Detach();
            debuggedProcess.Kill();
        }

        /// <summary>
        /// Detaches the debugger from the debugged process.
        /// This is done by removing all registered BreakPoints and then resuming the debugged process.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Sets a BreakPoint at a given address in the debugged process.
        /// </summary>
        /// <param name="address">The address at which a BreakPoint should be placed.</param>
        /// <returns></returns>
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
        /// <summary>
        /// Clears a BreakPoint that has been previously set in the debugged process.
        /// </summary>
        /// <param name="address">The address at which the BreakPoint should be removed.</param>
        /// <returns></returns>
        public NIDebugger ClearBreakpoint(uint address)
        {
            if (breakpoints.Keys.Contains(address))
            {

                WriteData(address, breakpoints[address].originalBytes);
                breakpoints.Remove(address);
            }
            return this;
        }

        /// <summary>
        /// Gets the length of the current instruction. This is based on the current value of EIP.
        /// </summary>
        /// <returns>The length (in bytes) of the current instruction.</returns>
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

        /// <summary>
        /// Gets the opcodes for the current instruction.
        /// </summary>
        /// <returns>Byte array consisting of the opcodes for the current instruction.</returns>
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
            byte[] codes = new byte[size];
            Array.Copy(data, codes, size);

            return codes;
        }

        /// <summary>
        /// Method that performs SingleStep X number of times.
        /// </summary>
        /// <param name="number">The number of times SingleStep() should be executed</param>
        /// <returns></returns>
        public NIDebugger SingleStep(int number)
        {
            for (int x = 0; x < number; x++)
            {
                SingleStep();
            }
            return this;
        }

        /// <summary>
        /// Performs a SingleStep operation, that is to stay it resumes the process and pauses at the very next instruction.
        /// Jumps are followed, conditional jumps are evaluated, Calls are either stepped into or over depending on StepIntoCalls value.
        /// </summary>
        /// <returns></returns>
        public NIDebugger SingleStep()
        {
            getContext(getCurrentThreadId());
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

            if ((data[ldata.opcd_offset] == 0xE2))
            {
                // LOOP

                if (Context.Ecx == 1)
                {
                    // this instruction will make ECX 0, so we fall thru the jump now
                    nextAddress = (uint)(Context.Eip + ldata.size);

                }
                else if (Context.Ecx > 1)
                {
                    // this instruction will decrement ECX but it wont be 0 yet, so jump!
                    sbyte disp = (sbyte)data[1];
                    nextAddress = (uint)(Context.Eip + disp) + ldata.size;
                }


            }

            if ((data[ldata.opcd_offset] == 0xE0))
            {
                //LOOPNZ LOOPNE
                if (Context.Ecx == 1 && Context.GetFlag(NIContextFlag.ZERO) == false)
                {
                    nextAddress = (uint)(Context.Eip + ldata.size);
                }
                else if (Context.Ecx > 1 || Context.GetFlag(NIContextFlag.ZERO) != false)
                {
                    sbyte disp = (sbyte)data[1];
                    nextAddress = (uint)(Context.Eip + disp) + ldata.size;
                }
            }

            if ((data[ldata.opcd_offset] == 0xE1))
            {
                //LOOPZ LOOPE
                if (Context.Ecx == 1 && Context.GetFlag(NIContextFlag.ZERO) == true)
                {
                    nextAddress = (uint)(Context.Eip + ldata.size);
                }
                else if (Context.Ecx > 1 || Context.GetFlag(NIContextFlag.ZERO) != true)
                {
                    sbyte disp = (sbyte)data[1];
                    nextAddress = (uint)(Context.Eip + disp) + ldata.size;
                }
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

        /// <summary>
        /// Helper method that simplifies setting a BreakPoint on a function in the debugged process. Usefull only for functions that are exported from their associated modules.
        /// </summary>
        /// <param name="module">The module that holds the method.</param>
        /// <param name="method">The method to set a BreakPoint on.</param>
        /// <returns></returns>
        public NIDebugger SetProcBP(String module, String method)
        {
            SetBreakpoint(FindProcAddress(module, method));
            return this;
        }

        /// <summary>
        /// Helper method that simplifies clearing a BreakPoint on a function in the debugged process. Usefull only for functions that are exported from their associated modules.
        /// </summary>
        /// <param name="module">The module that holds the method.</param>
        /// <param name="method">The method to clear a BreakPoint from.</param>
        /// <returns></returns>
        public NIDebugger ClearProcBP(String module, String method)
        {
            ClearBreakpoint(FindProcAddress(module, method));
            return this;
        }

        #endregion

        #region Delegate Methods 
        /// <summary>
        /// This method continues to run the specifed action while the specified condition results in True
        /// </summary>
        /// <param name="condition">Method to be used in determining if Action should be performed, this method MUST return a boolean value.</param>
        /// <param name="action">The action delegate to be performed while Condition resolves to True</param>
        /// <returns></returns>
        public NIDebugger While(Func<bool> condition, Action action)
        {
            while (condition())
            {
                action();
            }

            return this;
        }

        /// <summary>
        /// This method continues to run the specifed action until the specified condition results in True
        /// </summary>
        /// <param name="condition">Method to be used in determining if Action should be performed, this method MUST return a boolean value.</param>
        /// <param name="action">The action delegate to be performed until Condition resolves to True.</param>
        /// <returns></returns>
        public NIDebugger Until(Func<bool> condition, Action action)
        {
            while (condition() != true)
            {
                action();
            }

            return this;
        }

        /// <summary>
        /// Runs a given Action count number of times.
        /// </summary>
        /// <param name="count">The number of times an Action should be run.</param>
        /// <param name="action">The action to be run.</param>
        /// <returns></returns>
        public NIDebugger Times(uint count, Action action)
        {
            for (uint x = 0; x < count; x++)
            {
                action();
            }
            return this;
        }

        /// <summary>
        /// This method runs the given Action if the specified condition results in True
        /// </summary>
        /// <param name="condition">Method to be used in determining if Action should be performed, this method MUST return a boolean value.</param>
        /// <param name="action">The action delegate to be performed if the condition evaluates to True.</param>
        /// <returns></returns>
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

    /// <summary>
    /// Class used to specify options used when dumping the debugged process through DumpProcess()
    /// </summary>
    public class NIDumpOptions
    {
        /// <summary>
        /// Determines if the EntryPoint of the debugged process should be changed when dumped.
        /// </summary>
        public bool ChangeEP = false;
        /// <summary>
        /// Only used if ChangeEP == True, this property determines what the new EntryPoint value should be.
        /// </summary>
        public uint EntryPoint = 0;
        /// <summary>
        /// Determines if a DumpFix should be performed on the debugged process.
        /// </summary>
        public bool PerformDumpFix = true;
        /// <summary>
        /// The output path for the dumped executable.
        /// </summary>
        public String OutputPath = "";
    }
    /// <summary>
    /// Enumeration for the various Flags.
    /// </summary>
    public enum NIContextFlag : uint
    {
        CARRY = 0x01, PARITY = 0x04, ADJUST = 0x10, ZERO = 0x40, SIGN = 0x80, DIRECTION = 0x400, OVERFLOW = 0x800
    }
    /// <summary>
    /// Class representing a given thread's Context (registers and flags).
    /// </summary>
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

    /// <summary>
    /// Class used to specify various startup options when calling Execute()
    /// </summary>
    public class NIStartupOptions
    {
        /// <summary>
        /// Gets or sets the path to the executable to be run.
        /// </summary>
        /// <value>
        /// The executable path.
        /// </value>
        public string executable { get; set; }
        /// <summary>
        /// Gets or sets the command line arguments.
        /// </summary>
        /// <value>
        /// The command line arguments.
        /// </value>
        public string commandLine { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the debugged process should be resumed immediately after creation, or if it should remain paused until Continue() is called.
        /// </summary>
        /// <value>
        /// If this is set to true, the debugged process will be started immediately after creation, otherwise it is left in a suspended state.
        /// </value>
        public bool resumeOnCreate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Win32 API call GetTickCount should be patched.
        /// </summary>
        /// <value>
        /// If this is set to true, the call will be patched (how it is patched is determine by the value of incrementTickCount), otherwise the method will be left alone.
        /// </value>
        public bool patchTickCount { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether GetTickCount should always return 1, or if it should return increasing numbers.
        /// </summary>
        /// <value>
        /// If this is set to true, GetTickCount will return increasing numbers, otherwise it will always return 1.
        /// </value>
        public bool incrementTickCount { get; set; }
    }

    /// <summary>
    /// Class representing a BreakPoint that has been placed in the debugged process.
    /// </summary>
    public class NIBreakPoint
    {
        /// <summary>
        /// Gets or sets the address of the BreakPoint.
        /// </summary>
        /// <value>
        /// The address of the BreakPoint.
        /// </value>
        public uint bpAddress { get; set; }
        /// <summary>
        /// Gets or sets the original bytes that were overwritten by the BreakPoint.
        /// </summary>
        /// <value>
        /// The original bytes that were overwritten by the BreakPoint.
        /// </value>
        public byte[] originalBytes {get; set;}

        /// <summary>
        /// Gets or sets the thread identifier. This value is populated once a BreakPoint has been hit to show which thread has hit it.
        /// </summary>
        /// <value>
        /// The thread identifier associated with this BreakPoint. This value is only valid in the context of a BreakPoint that has been hit.
        /// </value>
        public uint threadId { get; set; }
    }

    /// <summary>
    /// Class used to determine how the method SearchMemory() functions.
    /// </summary>
    public class NISearchOptions
    {
        /// <summary>
        /// Gets or sets the search string.
        /// </summary>
        /// <value>
        /// The search string.
        /// </value>
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
        /// <summary>
        /// Gets the search bytes that were parsed from the SearchString.
        /// </summary>
        /// <value>
        /// The search bytes that were parse from the SearchString.
        /// </value>
        public byte[] SearchBytes { get { return _searchBytes; } }
        /// <summary>
        /// Gets the byte mask that was determined from the SearchString.
        /// </summary>
        /// <value>
        /// The byte mask that was determined from the SearchString.
        /// </value>
        public byte[] ByteMask { get { return _maskBytes; } }

        private byte[] _searchBytes;
        private byte[] _maskBytes;



        /// <summary>
        /// Gets or sets the start address that the memory searching operation should begin.
        /// </summary>
        /// <value>
        /// The start address to begin searching from.
        /// </value>
        public uint StartAddress { get; set; }
        /// <summary>
        /// Gets or sets the end address that the memory searching operation should end.
        /// </summary>
        /// <value>
        /// The ending address for the memory searching operation.
        /// </value>
        public uint EndAddress { get; set; }
        /// <summary>
        /// Gets or sets the maximum occurences to find before returning from the memory searching operation.
        /// </summary>
        /// <value>
        /// The maximum occurences allowed.
        /// </value>
        public int MaxOccurs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the memory range should be limited to the main modules image.
        /// </summary>
        /// <value>
        ///   If this value is set to true, then only the main modules image will be searched, otherwise the search area will be defined by StartAddress and EndAddress.
        /// </value>
        public Boolean SearchImage { get; set; }
    }

    /// <summary>
    /// Enumeration used for getting values from a Context.
    /// </summary>
    public enum NIRegister
    {
        EAX, ECX, EDX, EBX, ESP, EBP, ESI, EDI, EIP
    }
}
