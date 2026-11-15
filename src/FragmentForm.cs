using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RussiaFlag
{
    internal sealed class FragmentForm : Form
    {
        private readonly TextBox path;
        private readonly NumericUpDown start, end;
        private readonly Anthem player = new Anthem();
        private readonly Settings settings;
        private readonly bool mutedTest;
        internal readonly Button Browse, Preview, Stop, SaveButton, CancelButtonControl;
        internal FragmentForm(Settings settings, bool mutedTest = false, Func<string> chooseTestFile = null)
        {
            this.settings = settings;
            this.mutedTest = mutedTest;
            Text = "SHAMAN — Я русский: нужный фрагмент";
            ClientSize = new Size(600, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            Controls.Add(new Label { Text = "Выберите запись и границы припева со скриншота.", AutoSize = true, Location = new Point(18, 18) });
            path = new TextBox { Text = settings.ShamanPath, Location = new Point(18, 52), Width = 440, ReadOnly = true };
            Browse = new Button { Text = "Выбрать файл…", Location = new Point(465, 50), Size = new Size(118, 30) };
            Controls.Add(path); Controls.Add(Browse);
            Controls.Add(new Label { Text = "Начало, сек.", AutoSize = true, Location = new Point(18, 101) });
            Controls.Add(new Label { Text = "Конец, сек.", AutoSize = true, Location = new Point(290, 101) });
            start = new NumericUpDown { Minimum = 0, Maximum = 86400, DecimalPlaces = 2, Increment = .1m, Value = Math.Min(86400, Math.Max(0, settings.StartMs / 1000m)), Location = new Point(135, 97), Width = 120 };
            end = new NumericUpDown { Minimum = 0, Maximum = 86400, DecimalPlaces = 2, Increment = .1m, Value = Math.Min(86400, Math.Max(0, settings.EndMs / 1000m)), Location = new Point(395, 97), Width = 120 };
            Controls.Add(start); Controls.Add(end);
            Preview = new Button { Text = "Прослушать", Location = new Point(18, 145), Size = new Size(130, 34) };
            Stop = new Button { Text = "Остановить", Location = new Point(160, 145), Size = new Size(130, 34) };
            SaveButton = new Button { Text = "Сохранить", Location = new Point(320, 202), Size = new Size(130, 34) };
            CancelButtonControl = new Button { Text = "Отмена", Location = new Point(460, 202), Size = new Size(120, 34), DialogResult = DialogResult.Cancel };
            Controls.Add(Preview); Controls.Add(Stop); Controls.Add(SaveButton); Controls.Add(CancelButtonControl);
            AcceptButton = SaveButton; CancelButton = CancelButtonControl;
            Browse.Click += delegate
            {
                if (chooseTestFile != null) { path.Text = chooseTestFile(); return; }
                using (OpenFileDialog picker = new OpenFileDialog { Title = "Запись SHAMAN — Я русский", Filter = "Аудиофайлы|*.mp3;*.wav;*.wma|Все файлы|*.*", CheckFileExists = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) path.Text = picker.FileName;
            };
            Preview.Click += delegate { Run(delegate { player.PlayFile(path.Text, (int)(start.Value * 1000), (int)(end.Value * 1000), mutedTest); }); };
            Stop.Click += delegate { player.Dispose(); };
            SaveButton.Click += delegate { Run(SaveFragment); };
            FormClosed += delegate { player.Dispose(); };
        }
        private void Run(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                if (mutedTest) throw;
                MessageBox.Show(this, error.Message, "Фрагмент музыки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void SaveFragment()
        {
            int from = (int)(start.Value * 1000), to = (int)(end.Value * 1000);
            int duration = player.ReadDuration(path.Text);
            if (from < 0 || to <= from || to > duration) throw new InvalidOperationException("Укажите начало и конец внутри записи. Длина: " + (duration / 1000.0).ToString("F2") + " сек.");
            player.Dispose();
            string target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", (mutedTest ? "menu-test-import" : "shaman-track") + Path.GetExtension(path.Text));
            if (!String.Equals(Path.GetFullPath(path.Text), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path.Text, target, true);
            settings.ShamanPath = target; settings.StartMs = from; settings.EndMs = to;
            settings.FragmentReady = true; settings.Music = "shaman"; settings.Save();
            DialogResult = DialogResult.OK; Close();
        }
        internal static void VerifyButtons(System.Text.StringBuilder report)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            Settings test = new Settings { PersistPath = Path.Combine(root, "fragment-test-settings.ini") };
            using (FragmentForm form = new FragmentForm(test, true, delegate { return Path.Combine(root, "assets", "anthem.mp3"); }))
            {
                form.Show(); Application.DoEvents();
                form.Browse.PerformClick();
                if (!File.Exists(form.path.Text)) throw new Exception("Fragment Browse button failed");
                form.start.Value = 1; form.end.Value = 2.5m;
                form.Preview.PerformClick();
                if (form.player.Status() != "playing") throw new Exception("Fragment Preview button failed");
                form.Stop.PerformClick();
                if (form.player.Status() != "closed") throw new Exception("Fragment Stop button failed");
                form.SaveButton.PerformClick();
                Settings saved = Settings.LoadFrom(test.PersistPath);
                if (!saved.FragmentReady || saved.StartMs != 1000 || saved.EndMs != 2500 || !File.Exists(saved.ShamanPath) || saved.Music != "shaman")
                    throw new Exception("Fragment Save button failed");
                File.Delete(saved.ShamanPath); File.Delete(test.PersistPath);
                report.AppendLine("Fragment Browse, Preview (muted), Stop and Save; local import and exact bounds persist: PASS");
            }
        }
    }
}
