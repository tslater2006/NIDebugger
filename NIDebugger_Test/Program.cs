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
        static NIDebugger debug = new NIDebugger();
        static uint memoryCave;
        static void Main(string[] args)
        {
            MassiveSingleStep();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }


        static void MassiveSingleStep()
        {
            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.resumeOnCreate = false;

            debug.Execute(opts);
            debug.StepIntoCalls = false;
            
            while (debug.Process.HasExited == false)
            {
                debug.SingleStep();
            }
        }
        static void ChangeTitle()
        {
            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.resumeOnCreate = false;

            NISearchOptions searchOpts = new NISearchOptions();
            searchOpts.SearchImage = true;
            searchOpts.SearchString = "50 FF 35 ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 8B 8C 24 DC 07 00 00 5F 5E 33 CC E8 ?? ?? ?? ?? 8B E5 5D C3 90 90 90 90 90";
            searchOpts.MaxOccurs = 1;

            uint[] occurs;
            // work
            debug.Execute(opts)
                .AllocateMemory(100, out memoryCave)
                .WriteString(memoryCave, "Welcome To NIDebugger", Encoding.Unicode)
                .SearchMemory(searchOpts,out occurs)
                .SetBreakpoint(occurs[0])
                .Continue()
                .SetRegister(NIRegister.EAX,memoryCave)
                .Detach();
        }

        public static bool IsStillRunning()
        {
            return debug.Process.HasExited == false;
        }

        public static void OverwriteText()
        {
            uint oldValue;
            String oldString = "";

            debug.SingleStep()
                .SetProcBP("user32.dll", "SetWindowTextW")
                .Continue()
                .ReadStackValue(8, out oldValue)
                .ReadString(oldValue, 100, Encoding.Unicode, out oldString)
                .WriteStackValue(8, memoryCave);

            Console.WriteLine("Old value: " + oldString);
        }
    }
}
