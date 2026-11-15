using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RussiaFlag
{
    internal sealed class ShortcutStore
    {
        private readonly string file;
        internal readonly List<string> Items = new List<string>();
        internal ShortcutStore(string zone, string root = null)
        {
            file = Path.Combine(root ?? AppDomain.CurrentDomain.BaseDirectory, "shortcuts-" + zone + ".txt");
            if (!File.Exists(file)) return;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                try
                {
                    string path = Encoding.UTF8.GetString(Convert.FromBase64String(line));
                    if (!Items.Exists(delegate(string value) { return String.Equals(value, path, StringComparison.OrdinalIgnoreCase); })) Items.Add(path);
                }
                catch (FormatException) { }
            }
        }
        internal void Add(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                if (!File.Exists(full) && !Directory.Exists(full)) continue;
                if (!Items.Exists(delegate(string value) { return String.Equals(value, full, StringComparison.OrdinalIgnoreCase); })) Items.Add(full);
            }
            Save();
        }
        internal void Remove(string path) { Items.Remove(path); Save(); }
        private void Save()
        {
            List<string> lines = new List<string>();
            foreach (string path in Items) lines.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
            File.WriteAllLines(file + ".tmp", lines.ToArray(), Encoding.UTF8);
            if (File.Exists(file)) File.Replace(file + ".tmp", file, null); else File.Move(file + ".tmp", file);
        }
    }
    internal sealed class ShortcutPocket : Form
    {
        private readonly ShortcutStore store;
        private readonly ListView list;
        private readonly ImageList icons;
        private readonly Label empty;
        internal ShortcutPocket(bool lake, Point anchor, string root = null)
        {
            store = new ShortcutStore(lake ? "lake" : "office", root);
            Text = lake ? "Рыболовный ящик — ярлыки" : "Тайник в столе — ярлыки";
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(520, 350); FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; MinimumSize = new Size(450, 290);
            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(anchor.X, area.Right - Width)), Math.Max(area.Top, Math.Min(anchor.Y - Height, area.Bottom - Height)));
            BackColor = lake ? Color.FromArgb(38, 49, 36) : Color.FromArgb(52, 35, 26); ForeColor = Color.FromArgb(244, 234, 210);
            Label heading = new Label { Text = lake ? "РЫБОЛОВНЫЙ ЯЩИК" : "ТАЙНИК В СТОЛЕ", Font = new Font("Segoe UI Semibold", 12), Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 9, 0, 0) };
            Label hint = new Label { Text = "Перетащите файлы сюда. Двойной щелчок — открыть.", Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 4, 0, 0) };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(7), WrapContents = false };
            Button add = new Button { Text = "Добавить файлы", AutoSize = true }, folder = new Button { Text = "Папку", AutoSize = true }, remove = new Button { Text = "Убрать ссылку", AutoSize = true }, open = new Button { Text = "Открыть", AutoSize = true };
            foreach (Button button in new Button[] { add, folder, remove, open }) { button.ForeColor = Color.Black; buttons.Controls.Add(button); }
            icons = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
            list = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = icons, MultiSelect = false, HideSelection = false, BackColor = Color.FromArgb(246, 240, 223), ForeColor = Color.FromArgb(40, 36, 28), AllowDrop = true };
            empty = new Label { Text = "Здесь пока пусто.\nДобавьте файлы, папки или ярлыки.\nИсходные файлы останутся на месте.", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(92, 80, 63), BackColor = list.BackColor, AllowDrop = true };
            Controls.Add(list); Controls.Add(hint); Controls.Add(heading); Controls.Add(buttons);
            list.Controls.Add(empty);
            add.Click += delegate { using (OpenFileDialog picker = new OpenFileDialog { Multiselect = true, Title = "Добавить ссылки в ящик", CheckFileExists = true }) if (picker.ShowDialog(this) == DialogResult.OK) Import(picker.FileNames); };
            folder.Click += delegate { using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Выберите папку" }) if (picker.ShowDialog(this) == DialogResult.OK) Import(new string[] { picker.SelectedPath }); };
            remove.Click += delegate { if (list.SelectedItems.Count > 0) { store.Remove((string)list.SelectedItems[0].Tag); Reload(); } };
            open.Click += delegate { Launch(); }; list.DoubleClick += delegate { Launch(); };
            list.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.KeyCode == Keys.Enter) Launch(); };
            DragEventHandler drag = delegate(object sender, DragEventArgs args) { args.Effect = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; };
            DragEventHandler drop = delegate(object sender, DragEventArgs args) { string[] paths = args.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null) Import(paths); };
            list.DragEnter += drag; list.DragDrop += drop; empty.DragEnter += drag; empty.DragDrop += drop;
            Reload();
        }
        private void Import(string[] paths)
        {
            try { store.Add(paths); Reload(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void Reload()
        {
            list.Items.Clear(); icons.Images.Clear();
            foreach (string path in store.Items)
            {
                string key = icons.Images.Count.ToString();
                Icon icon = null;
                try { if (File.Exists(path)) icon = Icon.ExtractAssociatedIcon(path); } catch { }
                icons.Images.Add(key, icon ?? SystemIcons.Application);
                if (icon != null) icon.Dispose();
                list.Items.Add(new ListViewItem(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), key) { Tag = path, ToolTipText = path });
            }
            list.ShowItemToolTips = true; empty.Visible = list.Items.Count == 0;
        }
        private void Launch()
        {
            if (list.SelectedItems.Count == 0) return;
            string path = (string)list.SelectedItems[0].Tag;
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Файл был перемещён или удалён. Уберите ссылку и добавьте её заново.");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        protected override void Dispose(bool disposing) { if (disposing) icons.Dispose(); base.Dispose(disposing); }
    }
}
