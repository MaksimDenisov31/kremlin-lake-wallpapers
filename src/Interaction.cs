using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Accessibility;

namespace RussiaFlag
{
    internal sealed class InteractionState
    {
        internal bool Lamp, Monitor, OfficePocket, LakePocket, Nature, Paused;
        internal float Curtain, CurtainTarget;
        internal double Time;
    }
    internal sealed class InputTarget
    {
        internal Rectangle Bounds;
        internal string Action;
        internal SceneCanvas Scene;
        internal PointF[] Shape;
        internal bool Contains(Point point)
        {
            if (!Bounds.Contains(point)) return false;
            if (Shape == null) return true;
            bool inside = false;
            for (int i = 0, j = Shape.Length - 1; i < Shape.Length; j = i++)
                if ((Shape[i].Y > point.Y) != (Shape[j].Y > point.Y) && point.X < (Shape[j].X - Shape[i].X) * (point.Y - Shape[i].Y) / (Shape[j].Y - Shape[i].Y) + Shape[i].X) inside = !inside;
            return inside;
        }
    }
    internal sealed class SceneGesture
    {
        internal InputTarget Target;
        internal Point Point, Start;
        internal int Kind; // 0 down, 1 move, 2 up
    }
    internal sealed class DesktopInput : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct MouseData { internal Point Point; internal uint Mouse, Flags, Time; internal UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromPoint(Point point, out IAccessible accessible, out object child);
        private readonly Control dispatcher;
        private readonly Action<SceneGesture> deliver;
        private readonly Thread worker;
        private readonly HookProc callback;
        private Control hookControl;
        private IntPtr hook;
        private volatile bool disposed;
        private volatile InputTarget[] targets = new InputTarget[0];
        private InputTarget captured;
        private Point start;
        private int movePending;
        private Point latestMove;
        internal bool Installed { get { return hook != IntPtr.Zero && !disposed; } }
        internal DesktopInput(Control dispatcher, Action<SceneGesture> deliver)
        {
            this.dispatcher = dispatcher; this.deliver = deliver; callback = OnMouse;
            Exception failure = null;
            using (ManualResetEvent ready = new ManualResetEvent(false))
            {
                worker = new Thread(delegate()
                {
                    hookControl = new Control(); IntPtr unused = hookControl.Handle;
                    hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                    if (hook == IntPtr.Zero) failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Desktop mouse hook");
                    ready.Set();
                    if (failure == null) Application.Run();
                    if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero; hookControl.Dispose();
                }) { IsBackground = true, Name = "Wallpaper desktop interactions" };
                worker.SetApartmentState(ApartmentState.STA); worker.Start(); ready.WaitOne();
            }
            if (failure != null) { worker.Join(); throw failure; }
        }
        internal void Update(InputTarget[] value) { targets = value ?? new InputTarget[0]; }
        internal static bool IsDesktopPoint(Point point)
        {
            StringBuilder name = new StringBuilder(256);
            IntPtr window = WindowFromPoint(point);
            uint actualPid, shellPid;
            NativeDesktop.GetWindowThreadProcessId(window, out actualPid); NativeDesktop.GetWindowThreadProcessId(GetShellWindow(), out shellPid);
            if (actualPid == 0 || shellPid == 0 || actualPid != shellPid) return false;
            GetClassName(GetAncestor(window, 2), name, name.Capacity);
            if (name.ToString() != "Progman" && name.ToString() != "WorkerW") return false;
            GetClassName(window, name, name.Capacity);
            string cls = name.ToString();
            if (cls != "SysListView32" && cls != "SHELLDLL_DefView" && cls != "WorkerW" && cls != "Progman") return false;
            if (cls != "SysListView32") return true;
            // Fail closed if accessibility cannot confirm empty desktop space.
            IAccessible accessible = null;
            try
            {
                object child;
                if (AccessibleObjectFromPoint(point, out accessible, out child) != 0 || accessible == null) return false;
                int role = Convert.ToInt32(accessible.get_accRole(child));
                return role == 10 || role == 33 || role == 9; // CLIENT, LIST or WINDOW; never LISTITEM (an icon)
            }
            catch { return false; }
            finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || disposed) return CallNextHookEx(hook, code, message, data);
            try
            {
                int msg = message.ToInt32();
                if (msg != 0x201 && msg != 0x202 && msg != 0x200) return CallNextHookEx(hook, code, message, data);
                Point point = ((MouseData)Marshal.PtrToStructure(data, typeof(MouseData))).Point;
                if (msg == 0x201 && captured == null)
                {
                    foreach (InputTarget target in targets)
                    {
                        if (!target.Contains(point) || !IsDesktopPoint(point)) continue;
                        captured = target; start = point; Post(new SceneGesture { Target = target, Point = point, Start = start, Kind = 0 });
                        return new IntPtr(1);
                    }
                }
                else if (captured != null && msg == 0x200)
                {
                    latestMove = point;
                    if (Interlocked.CompareExchange(ref movePending, 1, 0) == 0)
                    {
                        InputTarget owner = captured; Point origin = start;
                        dispatcher.BeginInvoke((Action)delegate
                        {
                            Interlocked.Exchange(ref movePending, 0);
                            if (!disposed) deliver(new SceneGesture { Target = owner, Point = latestMove, Start = origin, Kind = 1 });
                        });
                    }
                }
                else if (captured != null && msg == 0x202)
                {
                    InputTarget target = captured; captured = null;
                    Post(new SceneGesture { Target = target, Point = point, Start = start, Kind = 2 });
                    return new IntPtr(1);
                }
            }
            catch (Exception error) { captured = null; Program.Log(error); }
            return CallNextHookEx(hook, code, message, data);
        }
        private void Post(SceneGesture gesture)
        { dispatcher.BeginInvoke((Action)delegate { if (!disposed) deliver(gesture); }); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; targets = new InputTarget[0];
            if (hookControl != null && !hookControl.IsDisposed) hookControl.BeginInvoke((Action)Application.ExitThread);
            worker.Join();
        }
    }
    internal sealed class AnimationClock : IDisposable
    {
        internal const int TargetFps = 100;
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint value);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint value);
        private readonly ManualResetEvent quit = new ManualResetEvent(false);
        private readonly Thread worker;
        private readonly Control dispatcher;
        private int pending;
        private volatile bool disposed;
        private volatile bool suspended;
        internal bool Suspended { get { return suspended; } set { suspended = value; } }
        private readonly bool precise;
        internal AnimationClock(Control dispatcher, Action tick)
        {
            this.dispatcher = dispatcher;
            precise = timeBeginPeriod(1) == 0;
            worker = new Thread(delegate()
            {
                Stopwatch time = Stopwatch.StartNew(); double next = 0;
                while (!quit.WaitOne(0))
                {
                    double delay = next - time.Elapsed.TotalMilliseconds;
                    if (delay > 0) { quit.WaitOne(Math.Max(1, (int)Math.Ceiling(delay))); continue; }
                    next += 1000.0 / TargetFps;
                    if (next < time.Elapsed.TotalMilliseconds) next = time.Elapsed.TotalMilliseconds + 1000.0 / TargetFps;
                    if (suspended) continue;
                    if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) continue;
                    try
                    {
                        dispatcher.BeginInvoke((Action)delegate
                        {
                            // Keep the slot occupied until drawing finishes. A slow frame
                            // cannot queue another paint ahead of mouse/timer messages.
                            try { if (!disposed && !suspended) tick(); }
                            finally { Interlocked.Exchange(ref pending, 0); }
                        });
                    }
                    catch (InvalidOperationException) { break; }
                }
            }) { IsBackground = true, Name = "Wallpaper animation" };
            worker.Start();
        }
        public void Dispose()
        {
            disposed = true; quit.Set(); worker.Join(); if (precise) timeEndPeriod(1);
        }
    }
}
