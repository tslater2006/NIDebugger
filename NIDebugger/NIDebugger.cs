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
    public static class Extensions
    {
        public enum NIFlags : uint
        {
            CARRY = 0x01,
            PARITY = 0x04,
            ADJUST = 0x10,
            ZERO = 0x40,
            SIGN = 0x80,
            DIRECTION = 0x400,
            OVERFLOW = 0x800
        }
        public static bool GetFlag(this Win32.CONTEXT ctx, NIFlags i)
        {
            return (ctx.EFlags & (uint)i) == (uint)i;
        }

        public static void SetFlag(this Win32.CONTEXT ctx, NIFlags i, bool value)
        {
            ctx.EFlags -= GetFlag(ctx, i) ? (uint)i : 0;

            ctx.EFlags ^= (value) ? (uint)i : 0;

        }
    }
    public class NIDebugger
    {
        Dictionary<uint, NIBreakPoint> breakpoints = new Dictionary<uint, NIBreakPoint>();
        Dictionary<int, IntPtr> threadHandles = new Dictionary<int, IntPtr>();

        private static ManualResetEvent mre = new ManualResetEvent(false);
        BackgroundWorker bwContinue = new BackgroundWorker();
        Win32.PROCESS_INFORMATION debuggedProcessInfo;
        public Win32.CONTEXT ctx = new Win32.CONTEXT();
        NIBreakPoint lastBreakpoint; 

        Process debuggedProcess;

        private byte[] BREAKPOINT = new byte[] { 0xEB, 0xFE };
        


        public NIDebugger()
        {
            bwContinue.DoWork += bw_Continue;
        }


        public Win32.CONTEXT getContext()
        {
            return getContext(debuggedProcessInfo.dwThreadId);
        }


        public void updateContext()
        {
            updateContext(debuggedProcessInfo.dwThreadId);
        }

        public void updateContext(int threadId)
        {
            IntPtr hThread = getThreadHandle(threadId);

            Win32.SetThreadContext(hThread,ref ctx);
        }

        public Win32.CONTEXT getContext(int threadId)
        {

            IntPtr hThread = getThreadHandle(threadId);

            Win32.CONTEXT ctx = new Win32.CONTEXT();
            ctx.ContextFlags = (uint)Win32.CONTEXT_FLAGS.CONTEXT_ALL;
            Win32.GetThreadContext(hThread, ref ctx);

            return ctx;

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
                Win32.CONTEXT ctx = getContext();
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
                            this.ctx = getContext(pThread.Id);
                            e.Cancel = true;
                            mre.Set();
                            return;
                        }
                    }
                }
                resumeAllThreads();
                //Console.WriteLine("threads resumed");
            }
            return;
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
            if (lastBreakpoint != null)
            {
                updateContext( (int)lastBreakpoint.threadId);
            }
            else
            {
                updateContext();
            }
            
            mre.Reset();
            bwContinue.RunWorkerAsync();
            mre.WaitOne();
        }

        public void Detach()
        {
            pauseAllThreads();
            List<uint> bpAddresses = breakpoints.Keys.ToList();
            foreach(uint addr in bpAddresses)
            {
                clearBreakpoint(addr);
            }
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

        public IntPtr allocateMemory(uint size)
        {
            IntPtr memLocation = Win32.VirtualAllocEx((IntPtr)debuggedProcessInfo.hProcess, new IntPtr(), size, (uint)Win32.StateEnum.MEM_RESERVE | (uint)Win32.StateEnum.MEM_COMMIT, (uint) Win32.AllocationProtectEnum.PAGE_EXECUTE_READWRITE);

            return memLocation;
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
