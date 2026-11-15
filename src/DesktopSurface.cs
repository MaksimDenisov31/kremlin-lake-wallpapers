using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RussiaFlag
{
    internal sealed class DesktopWindowInfo
    {
        internal IntPtr Handle, Parent;
        internal string ClassName;
        internal uint ProcessId;
        internal long ExtendedStyle;
        internal bool Visible;
    }
    internal sealed class DesktopSurfacePlan
    {
        internal IntPtr Parent, ShellView, Worker, InsertAfter;
        internal uint ProcessId;
        internal bool Layered;
    }
    internal static class DesktopSurfacePolicy
    {
        // Host topology reference (not a stable Microsoft API): Lively's current
        // WinDesktopCore.cs, SetupDesktopLayer / TryAttachToDesktop:
        // https://github.com/lively-community/lively/blob/core-separation/src/Lively/Lively/Core/WinDesktopCore.cs
        // This implementation only adds our own child; it never reorders shell windows.
        internal static bool TrySelect(IList<DesktopWindowInfo> windows, out DesktopSurfacePlan plan)
        {
            plan = null;
            if (windows == null) return false;
            DesktopWindowInfo progman = null;
            foreach (DesktopWindowInfo item in windows)
                if (item.Parent == IntPtr.Zero && item.ClassName == "Progman" && item.ProcessId != 0) { progman = item; break; }
            if (progman == null) return false;
            bool raised = (progman.ExtendedStyle & 0x00200000L) != 0; // WS_EX_NOREDIRECTIONBITMAP
            if (raised)
            {
                DesktopWindowInfo view = Child(windows, progman.Handle, "SHELLDLL_DefView");
                DesktopWindowInfo worker = Child(windows, progman.Handle, "WorkerW");
                if (view == null || worker == null || !progman.Visible || !worker.Visible || view.ProcessId != progman.ProcessId || worker.ProcessId != progman.ProcessId ||
                    (view.ExtendedStyle & 0x00080000L) == 0 || windows.IndexOf(view) >= windows.IndexOf(worker) ||
                    Child(windows, worker.Handle, "SHELLDLL_DefView") != null) return false;
                plan = new DesktopSurfacePlan { Parent = progman.Handle, ShellView = view.Handle, Worker = worker.Handle,
                    InsertAfter = view.Handle, ProcessId = progman.ProcessId, Layered = true };
                return true;
            }
            for (int i = 0; i < windows.Count; i++)
            {
                DesktopWindowInfo host = windows[i];
                if (host.Parent != IntPtr.Zero || host.ProcessId != progman.ProcessId ||
                    (host.ClassName != "Progman" && host.ClassName != "WorkerW")) continue;
                DesktopWindowInfo view = Child(windows, host.Handle, "SHELLDLL_DefView");
                if (view == null || view.ProcessId != progman.ProcessId) continue;
                for (int j = i + 1; j < windows.Count; j++)
                {
                    DesktopWindowInfo worker = windows[j];
                    if (worker.Parent != IntPtr.Zero || worker.ClassName != "WorkerW") continue;
                    if (!worker.Visible || worker.ProcessId != progman.ProcessId || Child(windows, worker.Handle, "SHELLDLL_DefView") != null) return false;
                    plan = new DesktopSurfacePlan { Parent = worker.Handle, ShellView = view.Handle, Worker = worker.Handle,
                        InsertAfter = IntPtr.Zero, ProcessId = progman.ProcessId, Layered = false };
                    return true;
                }
            }
            return false;
        }

        private static DesktopWindowInfo Child(IList<DesktopWindowInfo> windows, IntPtr parent, string cls)
        {
            foreach (DesktopWindowInfo item in windows)
                if (item.Parent == parent && item.ClassName == cls) return item;
            return null;
        }
    }

    // A process-owned canvas, created hidden before it is attached as a desktop child.
    // It never sets an image, color, registry value, or Explorer icon visibility.
    // Destroying this HWND reveals Explorer's existing wallpaper/fill underneath it.
    internal sealed class DesktopSurface : Control
    {
        private readonly Rectangle screenBounds;
        private readonly SceneCanvas source;
        private DesktopSurfacePlan attachment;
        private bool attached;
        private string stage = "CreateControl";
        private IntPtr parentDpi, childDpi;
        private Rectangle parentBounds;

        internal DesktopSurface(Rectangle screenBounds, SceneCanvas sourceCanvas)
        {
            if (sourceCanvas == null) throw new ArgumentNullException("sourceCanvas");
            if (screenBounds.Width < 1 || screenBounds.Height < 1) throw new ArgumentOutOfRangeException("screenBounds");
            this.screenBounds = screenBounds;
            source = sourceCanvas;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            Visible = false;
            Bounds = screenBounds;
            source.SceneInvalidated += SourceInvalidated;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Never pass Explorer to CreateWindowEx. Native parenting is a separate,
                // checked operation after our hidden window has been created successfully.
                cp.Parent = attached ? attachment.Parent : IntPtr.Zero;
                cp.Style = (cp.Style & ~unchecked((int)0xD0000000)) | 0x04000000 |
                    (attached ? 0x40000000 : unchecked((int)0x80000000));
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000004; // NOACTIVATE, TOOLWINDOW, NOPARENTNOTIFY
                if (attachment != null && attachment.Layered) cp.ExStyle |= 0x00080000 | 0x00000020; // LAYERED + mouse-transparent
                return cp;
            }
        }

        internal static DesktopSurface CreateAndShow(Rectangle screenBounds, SceneCanvas sourceCanvas)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("Native desktop surfaces require Windows.");
            DesktopSurfacePlan plan = null;
            DesktopSurface surface = null;
            string operation = "SelectDesktopParent";
            try
            {
                if (!DesktopSurfacePolicy.TrySelect(DesktopSurfaceNative.Snapshot(), out plan))
                {
                    // Explorer's established live-wallpaper protocol is not a documented
                    // Win32 API. Unsupported layouts fail closed without changing settings.
                    DesktopSurfaceNative.RequestWallpaperHost();
                    if (!DesktopSurfacePolicy.TrySelect(DesktopSurfaceNative.Snapshot(), out plan))
                        throw new InvalidOperationException("Не найден безопасный слой под значками рабочего стола.");
                }
                operation = "EnterParentDpiContext";
                // WinForms can retain the DPI context from Control/NativeWindow construction.
                // The scope must begin BEFORE the constructor, not merely before Handle.
                using (DesktopSurfaceDpiScope scope = new DesktopSurfaceDpiScope(plan.Parent))
                {
                    operation = "CreateControl";
                    surface = new DesktopSurface(screenBounds, sourceCanvas);
                    surface.attachment = plan;
                    surface.parentDpi = scope.Context;
                    surface.AttachAndShow();
                }
                return surface;
            }
            catch (Exception error)
            {
                string context = "stage=" + (surface == null ? operation : surface.stage) +
                    "; parent=" + (plan == null ? "none" : plan.Parent.ToString()) +
                    "; parentPid=" + (plan == null ? "none" : plan.ProcessId.ToString()) +
                    "; layered=" + (plan != null && plan.Layered) + "; screen=" + screenBounds +
                    (surface == null ? "" : "; parentDpi=" + surface.parentDpi + "; childDpi=" + surface.childDpi + "; parentBounds=" + surface.parentBounds);
                Win32Exception native = error as Win32Exception;
                if (native != null) context += "; nativeError=" + native.NativeErrorCode;
                if (surface != null) { try { surface.Dispose(); } catch { } }
                InvalidOperationException failure = new InvalidOperationException("Не удалось создать фон флага; настройки Windows не менялись.", error);
                failure.Data["DesktopSurface"] = context;
                throw failure;
            }
        }

        private void AttachAndShow()
        {
            DesktopSurfacePlan plan = attachment;
            stage = "CreateHiddenHandle";
            IntPtr canvas = Handle;
            stage = "VerifyWindowDpiContext";
            childDpi = DesktopSurfaceNative.GetWindowDpiAwarenessContext(canvas);
            if (childDpi == IntPtr.Zero || !DesktopSurfaceNative.AreDpiAwarenessContextsEqual(parentDpi, childDpi))
                throw new InvalidOperationException("DPI-контекст окна флага не совпадает с родительским окном Explorer.");
            stage = "MapMonitorBounds";
            parentBounds = DesktopSurfaceNative.MapPhysicalScreenBounds(plan.Parent, screenBounds);
            stage = "SetChildStyle";
            int style = DesktopSurfaceNative.ReadStyle(canvas);
            DesktopSurfaceNative.WriteStyle(canvas, (style & ~unchecked((int)0x90000000)) | 0x40000000 | 0x04000000);
            stage = "SetParent";
            DesktopSurfaceNative.ClearLastError(0);
            IntPtr previous = DesktopSurfaceNative.SetParent(canvas, plan.Parent);
            int parentError = Marshal.GetLastWin32Error();
            if (previous == IntPtr.Zero && parentError != 0) throw new Win32Exception(parentError, "SetParent");
            if (DesktopSurfaceNative.GetParent(canvas) != plan.Parent)
                throw new InvalidOperationException("SetParent не прикрепил окно флага к проверенному слою Explorer.");
            stage = "SetLayeredWindowAttributes";
            if (plan.Layered && !DesktopSurfaceNative.SetLayeredWindowAttributes(canvas, 0, 255, 2))
                throw NativeFailure("SetLayeredWindowAttributes");
            stage = "PositionHiddenChild";
            if (!DesktopSurfaceNative.SetWindowPos(canvas, plan.InsertAfter, parentBounds.X, parentBounds.Y,
                parentBounds.Width, parentBounds.Height, 0x0010 | 0x0020)) throw NativeFailure("SetWindowPos"); // NOACTIVATE | FRAMECHANGED
            attached = true;
            stage = "VerifyDesktopLayers";
            if (!IsAttached) throw new InvalidOperationException("Порядок слоёв рабочего стола изменился. Флаг остановлена; фон Windows не изменён.");
            stage = "ShowDesktopChild";
            if (!DesktopSurfaceNative.SetWindowPos(canvas, IntPtr.Zero, 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0040)) throw NativeFailure("Show desktop child");
            RefreshScene();
            stage = "RestoreThreadDpiContext";
        }

        internal bool IsAttached
        {
            get
            {
                if (!attached || IsDisposed || !IsHandleCreated || attachment == null) return false;
                if (!DesktopSurfaceNative.IsWindow(Handle) || !DesktopSurfaceNative.IsWindow(attachment.Parent) ||
                    !DesktopSurfaceNative.IsWindow(attachment.ShellView) || !DesktopSurfaceNative.IsWindow(attachment.Worker) ||
                    !DesktopSurfaceNative.IsWindowVisible(attachment.Parent) || !DesktopSurfaceNative.IsWindowVisible(attachment.Worker) ||
                    DesktopSurfaceNative.GetParent(Handle) != attachment.Parent) return false;
                uint pid;
                NativeDesktop.GetWindowThreadProcessId(attachment.Parent, out pid);
                if (pid != attachment.ProcessId) return false;
                if (attachment.Layered)
                    return DesktopSurfaceNative.GetParent(attachment.ShellView) == attachment.Parent &&
                        DesktopSurfaceNative.GetParent(attachment.Worker) == attachment.Parent &&
                        DesktopSurfaceNative.ComesBefore(attachment.ShellView, Handle) &&
                        DesktopSurfaceNative.ComesBefore(Handle, attachment.Worker);
                return DesktopSurfaceNative.ComesBefore(DesktopSurfaceNative.GetParent(attachment.ShellView), attachment.Worker);
            }
        }

        internal void RefreshScene() { if (attached && !IsDisposed && IsHandleCreated) Refresh(); }
        private void SourceInvalidated(object sender, EventArgs e) { RefreshScene(); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!source.IsDisposed) source.PaintScene(e.Graphics, ClientRectangle);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) source.SceneInvalidated -= SourceInvalidated;
            attached = false;
            base.Dispose(disposing);
        }
        private static Exception NativeFailure(string operation)
        { return new Win32Exception(Marshal.GetLastWin32Error(), operation + ": не удалось создать фон флага; настройки Windows не менялись."); }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            // If WinForms recreates this handle, create another hidden standalone window.
            // The host will observe IsAttached=false and rebuild through the verified factory.
            attached = false;
            base.OnHandleDestroyed(e);
        }
    }

    internal sealed class DesktopSurfaceDpiScope : IDisposable
    {
        internal readonly IntPtr Context;
        private IntPtr previous;
        internal DesktopSurfaceDpiScope(IntPtr parent)
        {
            try
            {
                Context = DesktopSurfaceNative.GetWindowDpiAwarenessContext(parent);
                if (Context == IntPtr.Zero) throw new InvalidOperationException("Не удалось определить DPI-контекст Explorer.");
                previous = DesktopSurfaceNative.SetThreadDpiAwarenessContext(Context);
                if (previous == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadDpiAwarenessContext");
            }
            catch (EntryPointNotFoundException error)
            {
                throw new PlatformNotSupportedException("Для безопасного подключения к рабочему столу требуется Windows 10 версии 1607 или новее.", error);
            }
        }
        public void Dispose()
        {
            if (previous == IntPtr.Zero) return;
            IntPtr restore = previous;
            previous = IntPtr.Zero;
            if (DesktopSurfaceNative.SetThreadDpiAwarenessContext(restore) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RestoreThreadDpiAwarenessContext");
        }
    }

    internal static class DesktopSurfaceNative
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder value, int maximum);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint relation);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("kernel32.dll", EntryPoint = "SetLastError", SetLastError = true)] internal static extern void ClearLastError(uint error);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll", SetLastError = true)] private static extern int MapWindowPoints(IntPtr from, IntPtr to, [In, Out] Point[] points, uint count);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        internal static int ReadStyle(IntPtr hwnd)
        {
            ClearLastError(0);
            int style = IntPtr.Size == 8 ? unchecked((int)GetWindowLong64(hwnd, -16).ToInt64()) : GetWindowLong32(hwnd, -16);
            int error = Marshal.GetLastWin32Error();
            if (style == 0 && error != 0) throw new Win32Exception(error, "GetWindowLongPtr(GWL_STYLE)");
            return style;
        }
        internal static void WriteStyle(IntPtr hwnd, int style)
        {
            ClearLastError(0);
            long previous = IntPtr.Size == 8 ? SetWindowLong64(hwnd, -16, new IntPtr((long)(uint)style)).ToInt64() : SetWindowLong32(hwnd, -16, style);
            int error = Marshal.GetLastWin32Error();
            if (previous == 0 && error != 0) throw new Win32Exception(error, "SetWindowLongPtr(GWL_STYLE)");
        }
        internal static Rectangle MapPhysicalScreenBounds(IntPtr parent, Rectangle physical)
        {
            Point first = physical.Location, last = new Point(physical.Right, physical.Bottom);
            if (!PhysicalToLogicalPointForPerMonitorDPI(parent, ref first) || !PhysicalToLogicalPointForPerMonitorDPI(parent, ref last))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PhysicalToLogicalPointForPerMonitorDPI");
            Point[] corners = new Point[] { first, last };
            ClearLastError(0);
            int result = MapWindowPoints(IntPtr.Zero, parent, corners, 2);
            int error = Marshal.GetLastWin32Error();
            if (result == 0 && error != 0) throw new Win32Exception(error, "MapWindowPoints");
            Rectangle mapped = Rectangle.FromLTRB(corners[0].X, corners[0].Y, corners[1].X, corners[1].Y);
            if (mapped.Width < 1 || mapped.Height < 1) throw new InvalidOperationException("Преобразование координат монитора вернуло пустую область.");
            return mapped;
        }

        internal static void RequestWallpaperHost()
        {
            IntPtr progman = FindWindow("Progman", null), ignored;
            if (progman != IntPtr.Zero) SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 2, 1000, out ignored);
        }

        internal static List<DesktopWindowInfo> Snapshot()
        {
            List<DesktopWindowInfo> windows = new List<DesktopWindowInfo>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                AddWindow(windows, hwnd, IntPtr.Zero, 0);
                return windows.Count < 4096;
            }, IntPtr.Zero);
            return windows;
        }

        private static void AddWindow(List<DesktopWindowInfo> windows, IntPtr hwnd, IntPtr parent, int depth)
        {
            StringBuilder cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            uint pid;
            NativeDesktop.GetWindowThreadProcessId(hwnd, out pid);
            string name = cls.ToString();
            windows.Add(new DesktopWindowInfo { Handle = hwnd, Parent = parent, ClassName = name, ProcessId = pid, Visible = IsWindowVisible(hwnd),
                ExtendedStyle = IntPtr.Size == 8 ? GetWindowLong64(hwnd, -20).ToInt64() : (long)(uint)GetWindowLong32(hwnd, -20) });
            if (depth >= 3 || (name != "Progman" && name != "WorkerW" && name != "SHELLDLL_DefView")) return;
            IntPtr child = IntPtr.Zero;
            for (int count = 0; count < 512; count++)
            {
                child = FindWindowEx(hwnd, child, null, null);
                if (child == IntPtr.Zero) break;
                AddWindow(windows, child, hwnd, depth + 1);
            }
        }

        internal static bool ComesBefore(IntPtr upper, IntPtr lower)
        {
            if (upper == IntPtr.Zero || lower == IntPtr.Zero || upper == lower) return false;
            for (int index = 0; index < 4096; index++)
            {
                upper = GetWindow(upper, 2); // GW_HWNDNEXT: next lower sibling in z-order.
                if (upper == IntPtr.Zero) return false;
                if (upper == lower) return true;
            }
            return false;
        }
    }
}
