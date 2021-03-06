﻿using System;
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

            NIStartupOptions opts = new NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.resumeOnCreate = false;
            debug.Execute(opts);

            Console.WriteLine("Installing VEH");
            debug.InstallHardVEH();

            Console.WriteLine("Setting HWBP on Execute");
            debug.SetHardBreakPoint(0xc562a8, HWBP_MODE.MODE_LOCAL, HWBP_TYPE.TYPE_EXECUTE, HWBP_SIZE.SIZE_1);
            

            Console.WriteLine("Generating Hello World String in Target");
            uint memoryCave;
            debug.AllocateMemory(100, out memoryCave);

            debug.WriteString(memoryCave, "Welcome to NIDebugger HWBPs", Encoding.Unicode);

            Console.WriteLine("Running...");
            

            debug.Continue();

            // hope and pray
            Console.WriteLine("Our EIP after HWBP is: " + debug.Context.Eip.ToString("X8"));

            Console.WriteLine("Setting EAX to new String address");
            String oldString;
            debug.ReadString(debug.Context.Eax, 100, Encoding.Unicode, out oldString);

            debug.Context.Eax = memoryCave;


            debug.Detach();
            //debug.Detach();


            //ChangeAllSetText();

            Console.WriteLine("Press any key to exit...");
           // Console.ReadKey();
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

            uint oldValue;
            String oldString;
            debug.Execute(opts)
                .AllocateMemory(100, out memoryCave)
                .WriteString(memoryCave, "Welcome To NIDebugger", Encoding.Unicode)
                .SetProcBP("user32.dll","SetWindowTextW")
                .Continue()
                .ReadStackValue(8, out oldValue)
                .ReadString(oldValue, 100, Encoding.Unicode, out oldString)
                .WriteStackValue(8, memoryCave)
                .Detach();
        }

        static void ChangeAllSetText()
        {
            NIStartupOptions opts = new NonIntrusive.NIStartupOptions();
            opts.executable = @"c:\windows\system32\notepad.exe";
            opts.resumeOnCreate = false;

            uint oldValue;
            String oldString;
            debug.Execute(opts)
                .AllocateMemory(100, out memoryCave)
                .WriteString(memoryCave, "Welcome To NIDebugger", Encoding.Unicode)
                .While(IsStillRunning,OverwriteText)
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
