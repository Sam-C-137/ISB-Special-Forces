using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ISBSpecialForces.Prepatch
{
    internal static class ISBZofiaSainMigration
    {
        internal const string LegacyRole = "ISBBerserker";
        internal const string CurrentRole = "ISBFollowerZofia";
        internal static void Run()
        {
            var log = Logger.CreateLogSource("ISB Role Migration");
            try
            {
                foreach (var dll in Directory.EnumerateFiles(BepInEx.Paths.PluginPath, "SAIN.dll", SearchOption.AllDirectories))
                    MigrateDirectory(Path.GetDirectoryName(dll), Path.Combine(BepInEx.Paths.ConfigPath, "ISBSpecialForces", "role-name-backups"));
            }
            catch (Exception ex)
            {
                log.LogError("Could not migrate saved SAIN role names: " + ex.Message);
            }
        }

        internal static void MigrateDirectory(string root, string backupRoot)
        {
            foreach (var directory in new[]
            {
                "Presets",
                "Default Bot Config Values"
            }

            )
            {
                var folder = Path.Combine(root, directory);
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(file => Path.GetFileName(file) == "ISB March Five.json" ? 0 : 1))
                {
                    var original = File.ReadAllText(file);
                    if (original.IndexOf(LegacyRole, StringComparison.OrdinalIgnoreCase) < 0 && original.IndexOf("ISB March Five", StringComparison.Ordinal) < 0 && original.IndexOf("ISB Zofia", StringComparison.Ordinal) < 0)
                        continue;
                    var tree = JToken.Parse(original);
                    bool changed = Rewrite(tree);
                    var group = tree as JObject;
                    var role = group?["WildSpawnType"]?.ToString();
                    bool zofia = string.Equals(role, CurrentRole, StringComparison.OrdinalIgnoreCase) || role == "13706";
                    var destination = file;
                    bool legacyCommanderName = (string.Equals(role, "ISBBossCommander", StringComparison.OrdinalIgnoreCase) || role == "13707") && group?["Name"]?.ToString() == "ISB March Five";
                    if (zofia || legacyCommanderName)
                    {
                        var name = zofia ? "ISB March Five" : "ISB Vengeful The Chosen One";
                        if (group["Name"]?.ToString() != name)
                        {
                            group["Name"] = name;
                            changed = true;
                        }

                        if (Path.GetFileName(file) == "ISB Zofia.json" || legacyCommanderName)
                        {
                            destination = Path.Combine(Path.GetDirectoryName(file), name + ".json");
                            changed = true;
                        }
                    }

                    if (!changed)
                        continue;
                    Directory.CreateDirectory(backupRoot);
                    using (var hash = SHA256.Create())
                    {
                        string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(file + "\n" + original))).Replace("-", "");
                        var backup = Path.Combine(backupRoot, key + ".bak");
                        if (!File.Exists(backup))
                            File.Copy(file, backup);
                    }

                    if (destination != file && File.Exists(destination))
                    {
                        var existing = JObject.Parse(File.ReadAllText(destination));
                        var existingRole = existing["WildSpawnType"]?.ToString();
                        bool sameRole = zofia ? string.Equals(existingRole, CurrentRole, StringComparison.OrdinalIgnoreCase) || existingRole == "13706" : string.Equals(existingRole, "ISBBossCommander", StringComparison.OrdinalIgnoreCase) || existingRole == "13707";
                        if (!sameRole)
                            throw new IOException("SAIN role-name collision at " + destination);
                        File.Delete(file);
                        continue;
                    }

                    var temporary = destination + ".isb-role-tmp";
                    File.WriteAllText(temporary, tree.ToString(Formatting.Indented), new UTF8Encoding(false));
                    if (File.Exists(destination))
                        File.Replace(temporary, destination, null);
                    else
                        File.Move(temporary, destination);
                    if (destination != file)
                        File.Delete(file);
                }
            }
        }

        internal static bool Rewrite(JToken tree)
        {
            bool changed = false;
            if (tree is JValue value && value.Type == JTokenType.String && string.Equals((string)value.Value, LegacyRole, StringComparison.OrdinalIgnoreCase))
            {
                value.Value = CurrentRole;
                return true;
            }

            if (tree is JObject obj)
            {
                var role = obj["WildSpawnType"]?.ToString();
                if ((string.Equals(role, LegacyRole, StringComparison.OrdinalIgnoreCase) || string.Equals(role, CurrentRole, StringComparison.OrdinalIgnoreCase) || role == "13706") && obj["Name"]?.ToString() == "ISB Zofia")
                {
                    obj["Name"] = "ISB March Five";
                    changed = true;
                }

                foreach (var property in obj.Properties().ToArray())
                {
                    changed |= Rewrite(property.Value);
                    if (!string.Equals(property.Name, LegacyRole, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (obj[CurrentRole] == null)
                        obj[CurrentRole] = property.Value.DeepClone();
                    property.Remove();
                    changed = true;
                }
            }
            else if (tree is JContainer container)
                foreach (var child in container.Children().ToArray())
                    changed |= Rewrite(child);
            return changed;
        }
    }
}
