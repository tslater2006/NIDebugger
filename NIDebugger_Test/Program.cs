using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NonIntrusive;
using System.Threading;
using System.Diagnostics;
namespace NIDebugger_Test
{
    class Program
    {
        static void Main(string[] args)
        {
            NIDebugger debug = new NIDebugger();

            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.commandLine = "\"c:\\asbps9t.sds\"";
            opts.resumeOnCreate = false;
            Process p = debug.Execute(opts);

            NIBreakPoint bp = debug.setBreakpoint(0xca2538);

            debug.Continue();

            debug.clearBreakpoint(bp);

            Win32.CONTEXT ctx = debug.getContext();

            IntPtr memoryCave = debug.allocateMemory(100);
            uint eaxVal = ctx.Eax;

            String curVal = debug.readString(ctx.Eax, 100, Encoding.Unicode);

            debug.writeString((uint)memoryCave, "Welcome to NIDebugger", Encoding.Unicode);

            ctx.Eax = (uint)memoryCave;

            debug.updateContext(ctx);

            debug.Detach();
            Console.ReadKey();
        }

        static void debug_BreakpointHit(object sender, NIBreakpPointEventArgs data)
        {
            Console.WriteLine("Breakpoint Hit: " + data.address);
        }
    }
}
