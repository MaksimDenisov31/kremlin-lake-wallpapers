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
        private readonly PocketStore store;
        private readonly ListView list;
        private readonly ImageList icons;
        private readonly List<Icon> iconSources = new List<Icon>();
        private readonly Label empty;
        private readonly ContextMenuStrip fileMenu;
        internal ShortcutPocket(bool lake, Point anchor, string root = null)
        {
            store = new PocketStore(lake ? "lake" : "office", root);
            Text = lake ? "Рыболовный ящик — файлы" : "Тайник в столе — файлы";
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(720, 390); FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; MinimumSize = new Size(450, 290);
            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(anchor.X, area.Right - Width)), Math.Max(area.Top, Math.Min(anchor.Y - Height, area.Bottom - Height)));
            BackColor = lake ? Color.FromArgb(38, 49, 36) : Color.FromArgb(52, 35, 26); ForeColor = Color.FromArgb(244, 234, 210);
            Label heading = new Label { Text = lake ? "РЫБОЛОВНЫЙ ЯЩИК" : "ТАЙНИК В СТОЛЕ", Font = new Font("Segoe UI Semibold", 12), Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 9, 0, 0) };
            Label hint = new Label { Text = "Перетащите сюда — переместить. ПКМ — вернуть на прежнее место.", Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 4, 0, 0) };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(12), WrapContents = true, BackColor = Color.FromArgb(245, 245, 247) };
            Button add = new PocketButton("Переместить файлы"), folder = new PocketButton("Переместить папку"), remove = new PocketButton("Вернуть на место"), open = new PocketButton("Открыть", true);
            foreach (Button button in new Button[] { add, folder, remove, open }) buttons.Controls.Add(button);
            buttons.Resize += delegate
            {
                int columns = buttons.ClientSize.Width >= 680 ? 4 : 2;
                buttons.Height = columns == 4 ? 72 : 120;
                foreach (Control button in buttons.Controls) button.Width = Math.Max(140, (buttons.ClientSize.Width - 24) / columns - 8);
            };
            list = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, HideSelection = false, BackColor = Color.FromArgb(246, 240, 223), ForeColor = Color.FromArgb(40, 36, 28), AllowDrop = true };
            remove.Enabled = open.Enabled = false;
            list.SelectedIndexChanged += delegate { remove.Enabled = open.Enabled = list.SelectedItems.Count > 0; };
            icons = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
            list.LargeImageList = icons;
            empty = new Label { Text = "Здесь пока пусто.\nПеретащите файлы с рабочего стола в ящик.\nДля возврата: ПКМ → Вернуть на прежнее место.", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(92, 80, 63), BackColor = list.BackColor, AllowDrop = true };
            Controls.Add(list); Controls.Add(hint); Controls.Add(heading); Controls.Add(buttons);
            list.Controls.Add(empty);
            add.Click += delegate { using (OpenFileDialog picker = new OpenFileDialog { Multiselect = true, Title = "Переместить файлы в ящик", CheckFileExists = true }) if (picker.ShowDialog(this) == DialogResult.OK) Import(picker.FileNames); };
            folder.Click += delegate { using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Выберите папку" }) if (picker.ShowDialog(this) == DialogResult.OK) Import(new string[] { picker.SelectedPath }); };
            remove.Click += delegate { RestoreSelected(); };
            open.Click += delegate { Launch(); }; list.DoubleClick += delegate { Launch(); };
            fileMenu = new ContextMenuStrip();
            fileMenu.Items.Add("Открыть", null, delegate { Launch(); });
            fileMenu.Items.Add("Вернуть на прежнее место", null, delegate { RestoreSelected(); });
            fileMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs args) { args.Cancel = list.SelectedItems.Count == 0; };
            list.ContextMenuStrip = fileMenu;
            list.MouseDown += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Right) return;
                ListViewItem hit = list.HitTest(args.Location).Item;
                list.SelectedItems.Clear();
                if (hit != null) { hit.Selected = true; hit.Focused = true; }
            };
            list.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.KeyCode == Keys.Enter) Launch(); };
            DragEventHandler drag = delegate(object sender, DragEventArgs args) { args.Effect = args.Data.GetDataPresent(DataFormats.FileDrop) && (args.AllowedEffect & DragDropEffects.Move) != 0 ? DragDropEffects.Move : DragDropEffects.None; };
            DragEventHandler drop = delegate(object sender, DragEventArgs args) { string[] paths = args.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null) Import(paths); };
            list.DragEnter += drag; list.DragDrop += drop; empty.DragEnter += drag; empty.DragDrop += drop;
            Reload();
            Shown += delegate
            {
                try { store.MigrateLegacy(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Reload(); }
            };
        }
        private void Import(string[] paths)
        {
            try { store.Import(paths); }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { Reload(); }
        }
        private void RestoreSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            PocketEntry entry = (PocketEntry)list.SelectedItems[0].Tag;
            try
            {
                try { store.Restore(entry); }
                catch (UnauthorizedAccessException)
                {
                    string zone = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(entry.Stored)));
                    Enabled = false;
                    try { PocketElevation.Request(zone, PocketElevation.Token(entry)); }
                    finally { Enabled = true; store.Refresh(); Activate(); }
                }
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { Reload(); }
        }
        private void Reload()
        {
            list.Items.Clear(); icons.Images.Clear();
            ReleaseIconSources();
            foreach (PocketEntry entry in store.Items)
            {
                string path = entry.Current;
                string key = icons.Images.Count.ToString();
                Icon icon = null;
                try { if (File.Exists(path)) icon = Icon.ExtractAssociatedIcon(path); } catch { }
                // ImageList can defer reading the Icon until its native handle is created.
                // Keep each source alive through reloads and until the image list is disposed.
                if (icon == null) icon = (Icon)SystemIcons.Application.Clone();
                iconSources.Add(icon);
                icons.Images.Add(key, icon);
                list.Items.Add(new ListViewItem(Path.GetFileName(entry.Original), key) { Tag = entry, ToolTipText = "Вернуть в: " + entry.Original });
            }
            list.ShowItemToolTips = true; empty.Visible = list.Items.Count == 0;
        }
        private void Launch()
        {
            if (list.SelectedItems.Count == 0) return;
            string path = ((PocketEntry)list.SelectedItems[0].Tag).Current;
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Файл не найден. Проверьте папку pocket-files рядом с обоями.");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void ReleaseIconSources()
        {
            foreach (Icon icon in iconSources) icon.Dispose();
            iconSources.Clear();
        }
        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                if (disposing) { icons.Dispose(); ReleaseIconSources(); fileMenu.Dispose(); }
            }
        }
    }
}
