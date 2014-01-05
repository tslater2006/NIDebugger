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
            
            NIBreakPoint bp = debug.setBreakpoint(0xdc62A8);

            debug.Continue();

            debug.clearBreakpoint(bp);

            IntPtr memoryCave = debug.allocateMemory(100);
            uint eaxVal = debug.ctx.Eax;

            String curVal = debug.readString(debug.ctx.Eax, 100, Encoding.Unicode);
            Console.WriteLine("Old value: " + curVal);
            debug.writeString((uint)memoryCave, "Welcome to NIDebugger", Encoding.Unicode);

            debug.ctx.Eax = (uint)memoryCave;

            debug.updateContext();

            uint addr = debug.getProcAddress("kernel32.dll","Module32First");    
            addr = debug.getProcAddress("comdlg32.dll", "ChooseColorA");    
            debug.Detach();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void debug_BreakpointHit(object sender, NIBreakpPointEventArgs data)
        {
            Console.WriteLine("Breakpoint Hit: " + data.address);
        }
    }
}
