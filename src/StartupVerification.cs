using System;
using System.IO;
using System.Threading;

namespace RussiaFlag
{
    internal static class StartupVerification
    {
        internal static int Run()
        {
            // Isolated kernel-object names: never stop a real application in this test.
            string prefix = "Local\\RussiaFlag-startup-test-" + Guid.NewGuid().ToString("N");
            if (Program.StopSovietRoom(prefix + "-absent-event", prefix + "-absent-mutex") != 0)
                throw new Exception("Startup without SovietRoom failed");
            using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-event"))
            using (ManualResetEvent ready = new ManualResetEvent(false))
            {
                bool observed = false;
                Thread room = new Thread(delegate()
                {
                    using (Mutex instance = new Mutex(true, prefix + "-mutex"))
                    {
                        ready.Set(); observed = stop.WaitOne(5000); instance.ReleaseMutex();
                    }
                });
                room.IsBackground = true; room.Start();
                if (!ready.WaitOne(5000)) throw new Exception("Simulated room did not start");
                int result = Program.StopSovietRoom(prefix + "-event", prefix + "-mutex");
                if (!room.Join(5000) || !observed || result != 0) throw new Exception("Graceful room shutdown failed");
                // A stale event with no process must also allow normal startup.
                if (Program.StopSovietRoom(prefix + "-event", prefix + "-mutex") != 0) throw new Exception("Stopped room blocks startup");
            }
            File.WriteAllText("startup-verification.txt", "PASS: SovietRoom absent (no installed files needed); running room receives stop signal and releases its mutex; already stopped room allows startup.\r\n");
            return 0;
        }
    }
}
