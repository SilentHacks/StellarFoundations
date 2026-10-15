using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace StellarFoundations;

// The journal makes replacement of the world and its mod metadata recoverable as a unit.
internal static class AtomicSaveBundle
{
    private const string Folder = ".stellarfoundations-save";
    private static readonly object Gate = new object();
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private sealed class Entry { public string Name = ""; public bool Existed; public string Hash = ""; }
    private sealed class Journal { public int Format = 1; public bool Committed; public List<Entry> Files = new List<Entry>(); }

    internal static void Commit(string directory, IReadOnlyDictionary<string, byte[]> files, Action<string>? checkpoint = null)
    {
        lock (Gate)
        {
            Recover(directory);
            if (files.Count < 2 || files.Count > 4) throw new ArgumentException("A bounded save bundle is required.");
            var folder = Prepare(directory);
            var journal = new Journal();
            var prepared = false;
            try
            {
                foreach (var file in files)
                {
                    ValidateName(file.Key);
                    var target = Path.Combine(directory, file.Key);
                    RejectLink(target);
                    var entry = new Entry { Name = file.Key, Existed = File.Exists(target) };
                    if (entry.Existed)
                    {
                        var previous = File.ReadAllBytes(target);
                        Write(Path.Combine(folder, file.Key + ".previous"), previous);
                        entry.Hash = Hash(previous);
                    }
                    Write(Path.Combine(folder, file.Key + ".next"), file.Value);
                    journal.Files.Add(entry);
                }
                WriteJournal(folder, journal);
                prepared = true;
                checkpoint?.Invoke("prepared");
                foreach (var entry in journal.Files)
                {
                    Replace(Path.Combine(folder, entry.Name + ".next"), Path.Combine(directory, entry.Name));
                    checkpoint?.Invoke(entry.Name);
                }
                journal.Committed = true;
                WriteJournal(folder, journal);
                checkpoint?.Invoke("committed");
                File.Delete(Path.Combine(folder, "journal.json"));
            }
            catch
            {
                if (prepared) Recover(directory);
                throw;
            }
        }
    }

    internal static bool Recover(string directory)
    {
        lock (Gate)
        {
            var folder = Path.Combine(directory, Folder);
            var path = Path.Combine(folder, "journal.json");
            if (!File.Exists(path)) return false;
            RejectLink(folder); RejectLink(path);
            if (new FileInfo(path).Length > 65536) throw new IOException("Oversized foundation recovery journal.");
            var journal = JsonConvert.DeserializeObject<Journal>(File.ReadAllText(path, Utf8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 8, MissingMemberHandling = MissingMemberHandling.Error }) ?? throw new IOException("Invalid foundation save journal.");
            if (journal.Format != 1 || journal.Files == null || journal.Files.Count < 2 || journal.Files.Count > 4) throw new IOException("Unsupported foundation save journal.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in journal.Files)
            {
                if (entry == null) throw new IOException("Missing recovery target.");
                ValidateName(entry.Name);
                if (!names.Add(entry.Name)) throw new IOException("Duplicate recovery target.");
                RejectLink(Path.Combine(directory, entry.Name));
                var backup = Path.Combine(folder, entry.Name + ".previous");
                RejectLink(backup);
                if (!journal.Committed && entry.Existed && (!File.Exists(backup) || Hash(File.ReadAllBytes(backup)) != entry.Hash))
                    throw new IOException("Recovery backup missing or damaged. World files were left untouched.");
            }
            if (!journal.Committed)
                foreach (var entry in journal.Files)
                {
                    var target = Path.Combine(directory, entry.Name);
                    if (entry.Existed)
                    {
                        var stage = Path.Combine(folder, entry.Name + ".restore");
                        Write(stage, File.ReadAllBytes(Path.Combine(folder, entry.Name + ".previous")));
                        Replace(stage, target);
                    }
                    else if (File.Exists(target)) File.Delete(target);
                }
            File.Delete(path);
            return !journal.Committed;
        }
    }

    internal static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static string Prepare(string directory)
    {
        RejectLink(directory);
        var folder = Path.Combine(directory, Folder);
        Directory.CreateDirectory(folder); RejectLink(folder); return folder;
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name != Path.GetFileName(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
            throw new IOException("Save bundle targets must be filenames.");
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Save bundle paths must not be links.");
    }
    private static void WriteJournal(string folder, Journal journal)
    {
        var stage = Path.Combine(folder, "journal.next");
        Write(stage, Utf8.GetBytes(JsonConvert.SerializeObject(journal)));
        Replace(stage, Path.Combine(folder, "journal.json"));
    }
    private static void Replace(string stage, string target)
    {
        if (File.Exists(target)) File.Replace(stage, target, null); else File.Move(stage, target);
    }
    private static void Write(string path, byte[] bytes)
    {
        RejectLink(path);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
    }
}
