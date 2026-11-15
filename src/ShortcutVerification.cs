using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.ComponentModel;
using System.Windows.Forms;
namespace RussiaFlag
{
    internal static class ShortcutVerification
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception("Pocket check: " + message); }
        private static void Drop(ListView list, params string[] paths)
        {
            DataObject data = new DataObject(DataFormats.FileDrop, paths);
            DragEventArgs args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Move | DragDropEffects.Copy, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(list, new object[] { args });
            Check(args.Effect == DragDropEffects.Move, "Explorer receives MOVE effect");
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(list, new object[] { args });
        }
        internal static int Run()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".tools", "pocket-verification", Guid.NewGuid().ToString("N"));
            string project = Path.Combine(root, "project"), source = Path.Combine(root, "source");
            Directory.CreateDirectory(project); Directory.CreateDirectory(source);
            string file = Path.Combine(source, "document.pdf"), content = "%PDF-1.4\n% icon lifecycle fixture\n%%EOF";
            File.WriteAllText(file, content);
            string folder = Path.Combine(source, "folder"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "nested.txt"), "nested content");
            PocketStore storage = new PocketStore("data", project);
            storage.Import(new string[] { file }); PocketEntry entry = storage.Items[0];
            Check(!File.Exists(file) && File.ReadAllText(entry.Stored) == content, "move removes original and preserves bytes");
            storage.Import(new string[] { entry.Stored, file }); Check(storage.Items.Count == 1, "no duplicate on repeated drop");
            storage = new PocketStore("data", project); entry = storage.Items[0];
            File.WriteAllText(file, "new desktop file"); bool refused = false;
            try { storage.Restore(entry); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(file) == "new desktop file" && File.ReadAllText(entry.Stored) == content, "return refuses overwrite");
            File.Delete(file); storage.Restore(entry);
            Check(File.ReadAllText(file) == content && !File.Exists(entry.Stored), "return moves file back");
            string other = Path.Combine(source, "other"); Directory.CreateDirectory(other);
            string sameName = Path.Combine(other, "document.pdf"); File.WriteAllText(sameName, "different file");
            storage.Import(new string[] { file, sameName });
            Check(storage.Items.Count == 2 && storage.Items[0].Stored != storage.Items[1].Stored, "same names isolated");
            foreach (PocketEntry item in storage.Items.ToArray()) storage.Restore(item);
            Check(File.ReadAllText(file) == content && File.ReadAllText(sameName) == "different file", "same names restored independently");
            storage.Import(new string[] { file }); entry = storage.Items[0];
            File.Move(entry.Stored, entry.Original);
            storage = new PocketStore("data", project); storage.Restore(storage.Items[0]);
            Check(storage.Items.Count == 0 && File.ReadAllText(file) == content, "interrupted return recovered");
            bool blocked = false; try { storage.Import(new string[] { project }); } catch (IOException) { blocked = true; }
            Check(blocked && Directory.Exists(project), "cannot move project into itself");
            new ShortcutStore("legacy", project).Add(new string[] { file });
            PocketStore legacy = new PocketStore("legacy", project); legacy.MigrateLegacy(); legacy.MigrateLegacy();
            Check(legacy.Items.Count == 1 && !File.Exists(file) && new ShortcutStore("legacy", project).Items.Count == 0, "legacy moved once");
            legacy.Restore(legacy.Items[0]);
            string cross = Path.Combine(Path.GetTempPath(), "RussiaFlag-move-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(cross, content); storage.Import(new string[] { cross }); entry = storage.Items[0];
            Check(!File.Exists(cross) && File.ReadAllText(entry.Stored) == content, "cross-volume move");
            storage.Restore(entry); Check(File.ReadAllText(cross) == content, "cross-volume return"); File.Delete(cross);
            PocketStore elevated = new PocketStore("office", project);
            elevated.Import(new string[] { file }); PocketEntry elevatedEntry = elevated.Items[0];
            bool cancelled = !PocketElevation.Request("office", PocketElevation.Token(elevatedEntry), delegate(System.Diagnostics.ProcessStartInfo info)
            {
                Check(info.Verb == "runas" && info.UseShellExecute && info.Arguments.StartsWith("--restore-pocket office "), "UAC requests only restore helper");
                throw new Win32Exception(1223);
            });
            Check(cancelled && File.Exists(elevatedEntry.Stored) && !File.Exists(file), "UAC cancellation preserves stored file");
            bool invalidToken = false;
            try { PocketElevation.Execute("office", "../invalid", project); } catch (ArgumentException) { invalidToken = true; }
            Check(invalidToken && File.Exists(elevatedEntry.Stored), "helper rejects invalid item identifier");
            Check(PocketElevation.Request("office", PocketElevation.Token(elevatedEntry), delegate(System.Diagnostics.ProcessStartInfo info)
            {
                PocketElevation.Execute("office", PocketElevation.Token(elevatedEntry), project); return 0;
            }), "restore helper completes");
            elevated.Refresh(); Check(elevated.Items.Count == 0 && File.ReadAllText(file) == content, "UI reload sees helper restore");
            foreach (bool lake in new bool[] { true, false })
            {
                string zone = lake ? "lake" : "office";
                new PocketStore(zone, project).Import(new string[] { file });
                for (int cycle = 0; cycle < 4; cycle++)
                using (ShortcutPocket pocket = new ShortcutPocket(lake, new Point(100, 400), project))
                {
                    ListView list = null;
                    foreach (Control control in pocket.Controls) if (control is ListView) list = (ListView)control;
                    Check(list != null && list.Items.Count == 1, "saved PDF loaded");
                    Check(list.LargeImageList.Handle != IntPtr.Zero, "lazy icon creation before Show");
                    using (Bitmap image = new Bitmap(list.LargeImageList.Images[0])) Check(image.Width == 32, "icon readable");
                    pocket.Show(); Application.DoEvents();
                    if (cycle == 0)
                    {
                        using (Bitmap preview = new Bitmap(pocket.Width, pocket.Height))
                        {
                            pocket.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                            preview.Save(Path.Combine(root, lake ? "lake-buttons.png" : "office-buttons.png"));
                        }
                        foreach (int width in new int[] { 450, 720 })
                        {
                            pocket.ClientSize = new Size(width, 390); Application.DoEvents();
                            foreach (Control panel in pocket.Controls)
                                foreach (Control control in panel.Controls)
                                    if (control is PocketButton)
                                    {
                                        Check(control.Left >= 0 && control.Right <= panel.ClientSize.Width && control.Bottom <= panel.ClientSize.Height, "button visible after resizing");
                                        Check(TextRenderer.MeasureText(control.Text, control.Font).Width <= control.Width - 16, "button label fits");
                                    }
                        }
                    }
                    Drop(list, folder, ((PocketEntry)list.Items[0].Tag).Stored);
                    Check(list.Items.Count == 2 && !Directory.Exists(folder), "drop moves folder");
                    list.EnsureVisible(1); Application.DoEvents();
                    Rectangle bounds = list.Items[1].GetBounds(ItemBoundsPortion.Icon);
                    Point click = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(list,
                        new object[] { new MouseEventArgs(MouseButtons.Right, 1, click.X, click.Y, 0) });
                    Check(list.SelectedItems.Count == 1 && list.SelectedItems[0].Index == 1, "right click selects file");
                    list.ContextMenuStrip.Show(list, click); Application.DoEvents();
                    Check(list.ContextMenuStrip.Visible && list.ContextMenuStrip.Items[1].Enabled, "return menu clickable");
                    list.ContextMenuStrip.Items[1].PerformClick(); list.ContextMenuStrip.Close();
                    Check(list.Items.Count == 1 && File.ReadAllText(Path.Combine(folder, "nested.txt")) == "nested content", "context menu returns folder intact");
                    Check(list.LargeImageList.Handle != IntPtr.Zero, "icons survive reload");
                    pocket.Refresh(); Application.DoEvents(); pocket.Close();
                }
                PocketStore reopened = new PocketStore(zone, project);
                Check(reopened.Items.Count == 1 && !File.Exists(file), "file persists after window closure");
                reopened.Restore(reopened.Items[0]); Check(File.ReadAllText(file) == content, "PDF returned intact");
            }
            File.WriteAllText("shortcut-verification.txt", "PASS: actual moves and returns, cross-volume files, overwrite refusal, same-name isolation, interrupted return, legacy migration, folder contents, 8 UI reopen cycles, lazy icons, drag/drop MOVE and right-click return in both pockets.\r\nPASS: UAC helper launch configuration, cancellation without file changes, identifier validation, helper restore and UI refresh; readable rounded button labels at narrow and wide sizes. UAC consent itself requires an interactive user.\r\n");
            return 0;
        }
    }
}
