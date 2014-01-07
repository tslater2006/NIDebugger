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
            ChangeTitle();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void TestSingleStep()
        {
            NIDebugger debug = new NIDebugger();

            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.resumeOnCreate = false;
            debug.AutoClearBP = true;

            Process p = debug.Execute(opts);

           

            for (var x = 0; x < 3; x++)
            {
                debug.SingleStep();
                Console.WriteLine("Instruction length: " + debug.getInstrLength());
                Console.WriteLine("Instruction: " + debug.getInstrOpcodes());
            }

            debug.SingleStep();

            debug.Detach();

        }

        static void ChangeTitle()
        {
            NIDebugger debug = new NIDebugger();

            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.resumeOnCreate = false;
            debug.AutoClearBP = true;

            Process p = debug.Execute(opts);

            uint bpAddress = debug.getProcAddress("user32.dll", "SetWindowTextW");

            NIBreakPoint bp = debug.setBreakpoint(bpAddress);

            debug.Continue();

            uint memoryCave = debug.allocateMemory(100);
            uint eaxVal = debug.getStackValue(8);

            String curVal = debug.readString(debug.Context.Eax, 100, Encoding.Unicode);
            Console.WriteLine("Old value: " + curVal);
            debug.writeString(memoryCave, "Welcome to NIDebugger", Encoding.Unicode);

            debug.setStackValue(8, memoryCave);


            debug.Detach();
        }
    }
}
