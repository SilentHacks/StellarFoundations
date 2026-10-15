using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StellarFoundations;

public sealed class RecoveryTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void StartupRecoversAnOrphanedJournalWithoutRelyingOnTheOriginalWriter(bool committed)
    {
        WithOrphanedSave((directory, folder) => {
            var path = Path.Combine(folder, "journal.json"); var journal = JObject.Parse(File.ReadAllText(path));
            journal["Committed"] = committed; File.WriteAllText(path, journal.ToString());
            Assert.Equal(!committed, AtomicSaveBundle.Recover(directory));
            Assert.Equal(committed ? "new world" : "old world", File.ReadAllText(Path.Combine(directory, "world.json")));
            Assert.Equal(committed ? "new metadata" : "old metadata", File.ReadAllText(Path.Combine(directory, "sdmodkit-content.json")));
            Assert.False(File.Exists(path));
        });
    }

    [Fact] public void DamagedSecondBackupPreventsAnyPartialRollback()
    {
        WithOrphanedSave((directory, folder) => {
            File.WriteAllText(Path.Combine(folder, "sdmodkit-content.json.previous"), "damaged");
            Assert.Throws<IOException>(() => AtomicSaveBundle.Recover(directory));
            Assert.Equal("new world", File.ReadAllText(Path.Combine(directory, "world.json")));
            Assert.Equal("new metadata", File.ReadAllText(Path.Combine(directory, "sdmodkit-content.json")));
            Assert.True(File.Exists(Path.Combine(folder, "journal.json")));
        });
    }

    [Fact] public void OversizedRecoveryJournalFailsBeforeChangingWorldFiles()
    {
        WithOrphanedSave((directory, folder) => {
            File.WriteAllText(Path.Combine(folder, "journal.json"), new string(' ', 65537));
            Assert.Throws<IOException>(() => AtomicSaveBundle.Recover(directory));
            Assert.Equal("new world", File.ReadAllText(Path.Combine(directory, "world.json")));
        });
    }

    private static void WithOrphanedSave(Action<string, string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "StellarFoundationsTests", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(directory, ".stellarfoundations-save"); Directory.CreateDirectory(folder);
        try
        {
            var entries = new JArray();
            foreach (var pair in new[] { ("world.json", "world"), ("sdmodkit-content.json", "metadata") })
            {
                var previous = Encoding.UTF8.GetBytes("old " + pair.Item2);
                File.WriteAllBytes(Path.Combine(folder, pair.Item1 + ".previous"), previous);
                File.WriteAllText(Path.Combine(directory, pair.Item1), "new " + pair.Item2);
                entries.Add(new JObject { ["Name"] = pair.Item1, ["Existed"] = true, ["Hash"] = AtomicSaveBundle.Hash(previous) });
            }
            File.WriteAllText(Path.Combine(folder, "journal.json"), new JObject { ["Format"] = 1, ["Committed"] = false, ["Files"] = entries }.ToString());
            assertion(directory, folder);
        }
        finally { Directory.Delete(directory, true); }
    }
}
