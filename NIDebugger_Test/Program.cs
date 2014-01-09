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

            uint bpAddress = debug.FindProcAddress("user32.dll", "SetWindowTextW");

            NIBreakPoint bp = debug.SetBreakpoint(bpAddress);

            debug.Continue();

            while (debug.GetInstrOpcodes().Substring(0,2).Equals("74") == false)
            {
                debug.SingleStep();
                Console.WriteLine("Instruction length: " + debug.GetInstrLength());
                Console.WriteLine("Instruction: " + debug.GetInstrOpcodes());
            }
            NIContext foo = debug.Context;

            foo.SetFlag(ContextFlag.ZERO, true);

            debug.SingleStep();
            Console.WriteLine("Instruction length: " + debug.GetInstrLength());
            Console.WriteLine("Instruction: " + debug.GetInstrOpcodes());

            debug.SingleStep();
            Console.WriteLine("Instruction length: " + debug.GetInstrLength());
            Console.WriteLine("Instruction: " + debug.GetInstrOpcodes());

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

            uint bpAddress = debug.FindProcAddress("user32.dll", "SetWindowTextW");

            uint memoryCave = debug.AllocateMemory(100);
            debug.WriteString(memoryCave, "Welcome to NIDebugger", Encoding.Unicode);

            while (p.HasExited == false)
            {
                NIBreakPoint bp = debug.SetBreakpoint(bpAddress);

                debug.Continue();
                uint oldStringAddr = debug.ReadStackValue(8);

                String curVal = debug.ReadString(oldStringAddr, 100, Encoding.Unicode);
                Console.WriteLine("Old value: " + curVal);

                debug.WriteStackValue(8, memoryCave);
                debug.SingleStep();
            }

            debug.Detach();
        }
    }
}
