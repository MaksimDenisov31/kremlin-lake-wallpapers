using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RussiaFlag
{
    // MCI devices belong to the thread that opened them. All operations, including
    // deadline cleanup, run on a dedicated STA thread with its own message loop.
    internal sealed class Anthem : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int mciSendString(string command, StringBuilder output, int length, IntPtr callback);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern bool mciGetErrorString(int error, StringBuilder message, int length);
        private readonly object gate = new object();
        private readonly string alias = "audio_" + Guid.NewGuid().ToString("N");
        private Control dispatcher;
        private Thread worker;
        private System.Threading.Timer deadline;
        private bool opened;
        private int generation;
        internal int PlaybackEndMs { get; private set; }
        private static void Send(string command)
        {
            int error = mciSendString(command, null, 0, IntPtr.Zero);
            if (error == 0) return;
            StringBuilder message = new StringBuilder(512);
            mciGetErrorString(error, message, message.Capacity);
            throw new InvalidOperationException("Ошибка аудио (MCI " + error + "): " + message);
        }
        private void EnsureWorker()
        {
            if (worker != null) return;
            Exception startup = null;
            using (ManualResetEvent ready = new ManualResetEvent(false))
            {
                worker = new Thread(delegate()
                {
                    try { dispatcher = new Control(); IntPtr unused = dispatcher.Handle; }
                    catch (Exception error) { startup = error; }
                    ready.Set();
                    if (startup == null) { Application.Run(); dispatcher.Dispose(); }
                }) { IsBackground = true, Name = "Wallpaper audio" };
                worker.SetApartmentState(ApartmentState.STA); worker.Start(); ready.WaitOne();
            }
            if (startup != null) { worker.Join(); worker = null; throw startup; }
        }
        private void Run(Action action)
        {
            EnsureWorker();
            try { dispatcher.Invoke(action); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        private void Open(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Не найден аудиофайл", path);
            Send("open \"" + Path.GetFullPath(path) + "\" type mpegvideo alias " + alias);
            opened = true; Send("set " + alias + " time format milliseconds");
        }
        private int Duration()
        {
            StringBuilder value = new StringBuilder(128);
            int error = mciSendString("status " + alias + " length", value, value.Capacity, IntPtr.Zero);
            if (error != 0) throw new InvalidOperationException("Не удалось прочитать длину записи (MCI " + error + ").");
            return int.Parse(value.ToString());
        }
        internal int ReadDuration(string path)
        {
            lock (gate)
            {
                int result = 0;
                Run(delegate { Close(); try { Open(path); result = Duration(); } finally { Close(); } });
                return result;
            }
        }
        internal void Play(bool muted)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "anthem.mp3");
            PlayFile(path, 0, ReadDuration(path), muted);
        }
        internal void PlayLoop(string path, bool muted)
        {
            lock (gate) Run(delegate
            {
                Close();
                try { Open(path); Send("setaudio " + alias + " volume to " + (muted ? "0" : "220")); Send("play " + alias + " repeat"); }
                catch { Close(); throw; }
            });
        }
        internal void PlayFile(string path, int fromMs, int toMs, bool muted)
        {
            lock (gate) Run(delegate
            {
                Close();
                try
                {
                    Open(path);
                    if (fromMs < 0 || toMs <= fromMs || toMs > Duration()) throw new InvalidOperationException("Укажите начало и конец внутри записи; конец должен быть позже начала.");
                    Send("setaudio " + alias + " volume to " + (muted ? "0" : "650"));
                    Send("play " + alias + " from " + fromMs + " to " + toMs);
                    PlaybackEndMs = toMs;
                    int token = generation;
                    Control owner = dispatcher;
                    deadline = new System.Threading.Timer(delegate
                    {
                        try { owner.BeginInvoke((Action)delegate { if (generation == token) Close(); }); }
                        catch (InvalidOperationException) { }
                    }, null, toMs - fromMs, Timeout.Infinite);
                }
                catch { Close(); throw; }
            });
        }
        internal string Status()
        {
            lock (gate)
            {
                if (worker == null) return "closed";
                string result = "closed";
                Run(delegate
                {
                    if (!opened) return;
                    StringBuilder value = new StringBuilder(128);
                    int error = mciSendString("status " + alias + " mode", value, value.Capacity, IntPtr.Zero);
                    if (error != 0) throw new InvalidOperationException("MCI status: " + error);
                    result = value.ToString();
                });
                return result;
            }
        }
        private void Close()
        {
            generation++;
            if (deadline != null) { deadline.Dispose(); deadline = null; }
            if (opened) { Send("close " + alias); opened = false; }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (worker == null) return;
                try { Run(delegate { try { Close(); } finally { Application.ExitThread(); } }); }
                finally { worker.Join(); worker = null; dispatcher = null; }
            }
        }
    }
}
