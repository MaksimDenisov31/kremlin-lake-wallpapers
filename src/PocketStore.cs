using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RussiaFlag
{
    internal sealed class PocketEntry
    {
        internal string Original, Stored;
        internal string Current { get { return File.Exists(Stored) || Directory.Exists(Stored) ? Stored : Original; } }
    }

    internal sealed class PocketStore
    {
        private readonly string manifest, storage, projectRoot, zone;
        internal readonly List<PocketEntry> Items = new List<PocketEntry>();
        internal PocketStore(string zone, string root = null)
        {
            this.zone = zone;
            projectRoot = Path.GetFullPath(root ?? AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            storage = Path.Combine(projectRoot, "pocket-files", zone);
            manifest = Path.Combine(projectRoot, "pocket-" + zone + ".txt");
            if (!File.Exists(manifest)) return;
            foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 2) throw new IOException("Повреждён список файлов ящика. Файлы сохранены в pocket-files.");
                string original = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                string stored = Path.GetFullPath(Path.Combine(projectRoot, Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))));
                if (!Inside(stored, storage)) throw new IOException("Недопустимый путь в списке файлов ящика.");
                Items.Add(new PocketEntry { Original = original, Stored = stored });
            }
        }
        private static bool Inside(string path, string parent)
        {
            return path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        internal void Import(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                string original = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                if (Items.Exists(delegate(PocketEntry existing) { return String.Equals(existing.Stored, original, StringComparison.OrdinalIgnoreCase); })) continue;
                if (!File.Exists(original) && !Directory.Exists(original)) continue;
                if (String.Equals(original, projectRoot, StringComparison.OrdinalIgnoreCase) || Inside(original, projectRoot) || Inside(projectRoot, original))
                    throw new IOException("Нельзя переместить в ящик папку проекта, её содержимое или родительскую папку.");
                string folder = Path.Combine(storage, Guid.NewGuid().ToString("N"));
                PocketEntry entry = new PocketEntry { Original = original, Stored = Path.Combine(folder, Path.GetFileName(original)) };
                Directory.CreateDirectory(folder);
                // Save the return address BEFORE moving data, so an interrupted move is recoverable.
                Items.Add(entry);
                try { Save(); }
                catch { Items.Remove(entry); Directory.Delete(folder); throw; }
                try { Move(original, entry.Stored); }
                catch
                {
                    // Keep the journal if an unexpected partial move left data at the destination.
                    if (!File.Exists(entry.Stored) && !Directory.Exists(entry.Stored))
                    {
                        Items.Remove(entry); Save(); Directory.Delete(folder);
                    }
                    throw;
                }
            }
        }
        internal void Restore(PocketEntry entry)
        {
            if (!Items.Contains(entry)) return;
            if (File.Exists(entry.Stored) || Directory.Exists(entry.Stored))
            {
                if (File.Exists(entry.Original) || Directory.Exists(entry.Original))
                    throw new IOException("На прежнем месте уже есть файл или папка с таким именем. Переименуйте их или переместите и повторите возврат. Файлы не перезаписаны.");
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Original));
                Move(entry.Stored, entry.Original);
            }
            else if (!File.Exists(entry.Original) && !Directory.Exists(entry.Original))
                throw new FileNotFoundException("Файл не найден ни в ящике, ни на прежнем месте.");
            Items.Remove(entry);
            try { Save(); }
            catch { Items.Add(entry); throw; }
            string folder = Path.GetDirectoryName(entry.Stored);
            try { if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal void Refresh()
        {
            PocketStore refreshed = new PocketStore(zone, projectRoot);
            Items.Clear(); Items.AddRange(refreshed.Items);
        }
        private static void Move(string source, string destination)
        {
            if (Directory.Exists(source))
            {
                if (!String.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Папки можно перемещать в ящик в пределах одного диска. Файлы можно переносить между дисками. Исходная папка осталась на месте.");
                Directory.Move(source, destination);
            }
            else File.Move(source, destination);
        }
        internal void MigrateLegacy()
        {
            ShortcutStore legacy = new ShortcutStore(zone, projectRoot);
            foreach (string path in legacy.Items.ToArray())
            {
                bool recorded = Items.Exists(delegate(PocketEntry entry) { return String.Equals(entry.Original, path, StringComparison.OrdinalIgnoreCase); });
                if (!recorded && (File.Exists(path) || Directory.Exists(path))) Import(new string[] { path });
                legacy.Remove(path);
            }
        }
        private void Save()
        {
            List<string> lines = new List<string>();
            foreach (PocketEntry entry in Items)
                lines.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Original)) + "\t" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Stored.Substring(projectRoot.Length + 1))));
            File.WriteAllLines(manifest + ".tmp", lines.ToArray(), Encoding.UTF8);
            if (File.Exists(manifest)) File.Replace(manifest + ".tmp", manifest, null); else File.Move(manifest + ".tmp", manifest);
        }
    }
}
