using System;
using System.IO;
using System.Text.Json;
using ISBSSOFExtra;

internal static class Program
{
    private static void Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "isb-content-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int checks = 0;
        void Check(bool valid, string name)
        {
            if (!valid) throw new InvalidOperationException(name);
            checks++;
        }
        try
        {
            foreach (string dll in new[] { "ISB-SpecialForces.dll", "ISBSOF_Extras.dll", "ISBSOF_Extras.Fika.dll", "ISBNotify.dll", "ISBNotify.Fika.dll" })
                File.WriteAllText(Path.Combine(root, dll), dll);
            foreach (string folder in new[] { "Blackout", "ISBSpecialForces_Audios", "ISBSpecialForces_Videos", "Office", "Nightmare" })
            {
                Directory.CreateDirectory(Path.Combine(root, folder));
                File.WriteAllText(Path.Combine(root, folder, "runtime.asset"), folder);
            }
            string original = EventContentCheck.Compute(root);
            Check(original.Length == 64, "A complete package produces a signature");
            File.WriteAllText(Path.Combine(root, "ISBSpecialForces_Audios", ".gitkeep"), "");
            File.WriteAllText(Path.Combine(root, "Office", ".gitkeep"), "");
            Check(EventContentCheck.Compute(root) == original, "Clean and upgraded installations ignore development markers");
            File.WriteAllText(Path.Combine(root, "Nightmare", "scp173_config.jsonc"), "{}");
            Check(EventContentCheck.Compute(root) == original, "Local Nightmare settings remain excluded");
            string asset = Path.Combine(root, "Office", "runtime.asset");
            File.WriteAllText(asset, "changed asset");
            Check(EventContentCheck.Compute(root) != original, "A real asset change still fails compatibility");
            File.WriteAllText(asset, "Office");
            string bridge = Path.Combine(root, "ISBSOF_Extras.Fika.dll");
            File.WriteAllText(bridge, "changed bridge");
            Check(EventContentCheck.Compute(root) != original, "A different Fika bridge still fails compatibility");
            File.WriteAllText(bridge, "ISBSOF_Extras.Fika.dll");
            File.Delete(asset);
            Check(EventContentCheck.Compute(root) == "missing:Office", "A development marker cannot replace missing runtime assets");
            Console.WriteLine(JsonSerializer.Serialize(new { passed = checks }));
            foreach (string directory in args)
                Console.WriteLine(JsonSerializer.Serialize(new { directory, signature = EventContentCheck.Compute(directory) }));
        }
        finally
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(root).StartsWith(temp, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
