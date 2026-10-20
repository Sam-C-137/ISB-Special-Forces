using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ISBSSOFExtra
{
    internal sealed class EventContentCheck
    {
        private readonly Task<string> _check;
        internal string Signature => _check.IsCompleted ? _check.Result : "pending";

        internal EventContentCheck(string directory)
        {
            _check = Task.Run(() => Compute(directory));
        }

        internal static string Compute(string directory)
        {
            try
            {
                var files = new List<string>();
                foreach (string name in new[]
                {
                    "ISB-SpecialForces.dll",
                    "ISBSOF_Extras.dll",
                    "ISBSOF_Extras.Fika.dll",
                    "ISBNotify.dll",
                    "ISBNotify.Fika.dll"
                }

                )
                {
                    string path = Path.Combine(directory, name);
                    if (!File.Exists(path))
                        return "missing:" + name;
                    files.Add(path);
                }

                foreach (string folder in new[]
                {
                    "Blackout",
                    "ISBSpecialForces_Audios",
                    "ISBSpecialForces_Videos",
                    "Office",
                    "Nightmare"
                }

                )
                {
                    string path = Path.Combine(directory, folder);
                    if (!Directory.Exists(path))
                        return "missing:" + folder;
                    var contents = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Where(p => !string.Equals(Path.GetFileName(p), ".gitkeep", StringComparison.OrdinalIgnoreCase) && !(folder == "Nightmare" && p.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase)) && !p.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (contents.Length == 0)
                        return "missing:" + folder;
                    files.AddRange(contents);
                }

                string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var manifest = new StringBuilder();
                using (var hash = SHA256.Create())
                {
                    foreach (string path in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        string relative = Path.GetFullPath(path).Substring(root.Length).Replace('\\', '/').ToLowerInvariant();
                        using (var stream = File.OpenRead(path))
                            manifest.Append(relative).Append(':').Append(BitConverter.ToString(hash.ComputeHash(stream))).Append('\n');
                    }

                    return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString()))).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                return "unreadable:" + ex.GetType().Name;
            }
        }

        internal static bool Compatible(string local, string remote) => local != null && local.Length == 64 && local.All(Uri.IsHexDigit) && string.Equals(local, remote, StringComparison.Ordinal);
    }
}
