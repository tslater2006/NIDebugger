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
            opts.resumeOnCreate = false;
            Process p = debug.Execute(opts);

            uint bpAddress = debug.getProcAddress("user32.dll", "SetWindowTextW");

            NIBreakPoint bp = debug.setBreakpoint(bpAddress);

            debug.Continue();
            Console.WriteLine("Instruction length: " + debug.getInstrLength());
            Console.WriteLine("Instruction: " + debug.getInstrOpcodes());
            debug.clearBreakpoint(bp);

            IntPtr memoryCave = debug.allocateMemory(100);
            uint eaxVal = debug.getStackValue(8);

            String curVal = debug.readString(debug.ctx.Eax, 100, Encoding.Unicode);
            Console.WriteLine("Old value: " + curVal);
            debug.writeString((uint)memoryCave, "Welcome to NIDebugger", Encoding.Unicode);

            debug.setStackValue(8, (uint)memoryCave);

            debug.updateContext();
  
            debug.Detach();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
