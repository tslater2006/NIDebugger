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
            TestSingleStep();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void TestSingleStep()
        {
            NIDebugger debug = new NIDebugger();

            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"C:\Program Files (x86)\PuTTY\putty.exe";
            opts.resumeOnCreate = false;
            debug.StepIntoCalls = false;
            
            Process p = debug.Execute(opts);

            debug.DumpProcess(new DumpOptions() { OutputPath = @"C:\Users\Timothy\Desktop\Putty_Dump.exe", ChangeEP = false, PerformDumpFix = true });

            uint bpAddress = debug.getProcAddress("user32.dll", "SetWindowTextW");

            NIBreakPoint bp = debug.setBreakpoint(bpAddress);

            debug.Continue();

            while (debug.getInstrOpcodes().Substring(0,2).Equals("74") == false)
            {
                debug.SingleStep();
                Console.WriteLine("Instruction length: " + debug.getInstrLength());
                Console.WriteLine("Instruction: " + debug.getInstrOpcodes());
            }
            NIContext foo = debug.Context;

            foo.SetFlag(ContextFlag.ZERO, true);

            debug.SingleStep();
            Console.WriteLine("Instruction length: " + debug.getInstrLength());
            Console.WriteLine("Instruction: " + debug.getInstrOpcodes());

            debug.SingleStep();
            Console.WriteLine("Instruction length: " + debug.getInstrLength());
            Console.WriteLine("Instruction: " + debug.getInstrOpcodes());

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

            uint memoryCave = debug.allocateMemory(100);
            debug.writeString(memoryCave, "Welcome to NIDebugger", Encoding.Unicode);

            while (p.HasExited == false)
            {
                NIBreakPoint bp = debug.setBreakpoint(bpAddress);

                debug.Continue();
                uint oldStringAddr = debug.getStackValue(8);

                String curVal = debug.readString(oldStringAddr, 100, Encoding.Unicode);
                Console.WriteLine("Old value: " + curVal);

                debug.setStackValue(8, memoryCave);
                debug.SingleStep();
            }

            debug.Detach();
        }
    }
}
