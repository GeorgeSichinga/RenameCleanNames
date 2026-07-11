// Program.cs
//
// RenameCleanNames
// -----------------
// Right-click a folder in Explorer -> "Rename with Clean Names".
// Removes any (...) or [...] tag from the folder itself and from every
// file/subfolder inside it (e.g. "Song (www.SongsLover.com)" -> "Song").
//
// Flow:
//   1. Walk the whole tree, build a rename plan (old path -> new path).
//   2. Print the plan and ask for confirmation.
//   3. Apply renames deepest-first so parent renames never break paths
//      that were already computed for their children.
//
// Author: George Sichinga

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TagLib;

namespace RenameCleanNames
{
    // File extensions we'll attempt to clean ID3/metadata tags on.
    // TagLib supports many more formats, but we keep this explicit and small
    // so the tool never touches file types it wasn't asked to.
    internal static class AudioExtensions
    {
        public static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".m4a", ".flac", ".ogg", ".wav", ".wma"
        };
    }

    internal class RenameItem
    {
        public string OldPath = "";
        public string NewPath = "";
        public bool IsDirectory;
    }

    // A planned metadata edit: which fields on which audio file need cleaning.
    internal class TagItem
    {
        public string FilePath = "";
        public string? OldTitle, NewTitle;
        public string? OldArtist, NewArtist;
        public string? OldAlbum, NewAlbum;
    }

    internal static class Program
    {
        // Matches a bracketed/parenthesized chunk plus any surrounding spare space,
        // e.g. " (www.SongsLover.com)" or "[SiteTag]".
        private static readonly Regex TagPattern =
            new Regex(@"\s*[\(\[][^\)\]]*[\)\]]\s*", RegexOptions.Compiled);

        // Matches a bare domain-like token even when it's NOT wrapped in
        // brackets, e.g. "Lookin - www.SongsLover.com" (common in ID3 titles).
        private static readonly Regex DomainPattern = new Regex(
            @"\b(?:www\.)?[a-zA-Z0-9][a-zA-Z0-9-]*\.(?:com|net|org|info|biz|co|io|tv|me|cc|to)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static int Main(string[] args)
        {
            // Explorer passes the clicked folder as the first argument ("%1").
            string rootPath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

            if (!Directory.Exists(rootPath))
            {
                Console.WriteLine($"Folder not found: {rootPath}");
                return 1;
            }

            var plan = new List<RenameItem>();
            BuildPlan(rootPath, plan);

            var tagPlan = BuildTagPlan(rootPath);

            if (plan.Count == 0 && tagPlan.Count == 0)
            {
                Console.WriteLine("Nothing to clean. No bracketed tags found in names or metadata.");
                Pause();
                return 0;
            }

            if (plan.Count > 0) PrintPlan(plan);
            if (tagPlan.Count > 0) PrintTagPlan(tagPlan);

            int total = plan.Count + tagPlan.Count;
            Console.Write($"\nApply {total} change(s) ({plan.Count} rename(s), {tagPlan.Count} metadata fix(es))? [y/N]: ");
            string answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("Cancelled. No changes made.");
                Pause();
                return 0;
            }

            // Metadata is edited first, while paths still match what was scanned.
            // Filenames are renamed afterward, since that doesn't affect tag content.
            ApplyTagPlan(tagPlan);
            ApplyPlan(plan);
            Console.WriteLine("\nDone.");
            Pause();
            return 0;
        }

        // Post-order walk: recurse into children first, then queue a rename
        // for the current item. This guarantees deepest items appear first
        // in the plan, which is also the safe order to apply them in.
        private static void BuildPlan(string path, List<RenameItem> plan)
        {
            bool isDir = Directory.Exists(path);

            if (isDir)
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(path))
                {
                    BuildPlan(child, plan);
                }
            }

            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            string cleaned = isDir ? CleanFolderName(name) : CleanFileName(name);

            if (cleaned != name && cleaned.Length > 0)
            {
                string? parent = Path.GetDirectoryName(path);
                if (parent == null) return;

                string newPath = Path.Combine(parent, cleaned);
                plan.Add(new RenameItem { OldPath = path, NewPath = newPath, IsDirectory = isDir });
            }
        }

        // Files: clean the name only, keep the extension untouched.
        private static string CleanFileName(string name)
        {
            string ext = Path.GetExtension(name);
            string baseName = Path.GetFileNameWithoutExtension(name);
            return CleanText(baseName) + ext;
        }

        // Folders: clean the whole name, no extension to preserve.
        private static string CleanFolderName(string name) => CleanText(name);

        // Strips bracketed tags and bare domain mentions, then tidies up
        // leftover spacing and dangling separators like " - ".
        private static string CleanText(string text)
        {
            string result = TagPattern.Replace(text, " ");   // e.g. (Ft. X), [SiteTag]
            result = DomainPattern.Replace(result, " ");      // e.g. www.SongsLover.com

            // Remove a leftover trailing/leading " - " left behind once the
            // domain text between it and the edge of the string is gone.
            result = Regex.Replace(result, @"(\s*-\s*){2,}", " - "); // collapse repeated dashes
            result = Regex.Replace(result, @"^\s*-\s*|\s*-\s*$", ""); // trim edge dashes
            result = Regex.Replace(result, @"\s{2,}", " ");           // collapse double spaces
            result = result.Trim().Trim('-').Trim();

            return result.Length > 0 ? result : text; // never produce an empty name
        }

        private static void PrintPlan(List<RenameItem> plan)
        {
            Console.WriteLine($"Found {plan.Count} item(s) to rename:\n");
            foreach (var item in plan)
            {
                string kind = item.IsDirectory ? "[folder]" : "[file]  ";
                Console.WriteLine($"{kind} {Path.GetFileName(item.OldPath)}");
                Console.WriteLine($"       -> {Path.GetFileName(item.NewPath)}");
            }
        }

        private static void ApplyPlan(List<RenameItem> plan)
        {
            foreach (var item in plan)
            {
                try
                {
                    if (System.IO.File.Exists(item.NewPath) || Directory.Exists(item.NewPath))
                    {
                        Console.WriteLine($"Skipped (target already exists): {item.NewPath}");
                        continue;
                    }

                    if (item.IsDirectory)
                        Directory.Move(item.OldPath, item.NewPath);
                    else
                        System.IO.File.Move(item.OldPath, item.NewPath);

                    Console.WriteLine($"Renamed: {Path.GetFileName(item.OldPath)} -> {Path.GetFileName(item.NewPath)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed: {item.OldPath} ({ex.Message})");
                }
            }
        }

        // Scans every supported audio file under root and works out which
        // Title/Artist/Album fields contain a bracketed tag that needs stripping.
        private static List<TagItem> BuildTagPlan(string root)
        {
            var items = new List<TagItem>();

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!AudioExtensions.Supported.Contains(Path.GetExtension(file)))
                    continue;

                TagLib.File? tf = null;
                try
                {
                    tf = TagLib.File.Create(file);
                }
                catch
                {
                    // Not a readable/tagged audio file — skip quietly.
                    continue;
                }

                using (tf)
                {
                    string title = tf.Tag.Title ?? "";
                    string artist = tf.Tag.FirstPerformer ?? "";
                    string album = tf.Tag.Album ?? "";

                    string newTitle = CleanText(title);
                    string newArtist = CleanText(artist);
                    string newAlbum = CleanText(album);

                    bool changed = (title.Length > 0 && newTitle != title)
                                || (artist.Length > 0 && newArtist != artist)
                                || (album.Length > 0 && newAlbum != album);

                    if (changed)
                    {
                        items.Add(new TagItem
                        {
                            FilePath = file,
                            OldTitle = title, NewTitle = newTitle,
                            OldArtist = artist, NewArtist = newArtist,
                            OldAlbum = album, NewAlbum = newAlbum
                        });
                    }
                }
            }

            return items;
        }

        private static void PrintTagPlan(List<TagItem> tagPlan)
        {
            Console.WriteLine($"\nFound {tagPlan.Count} audio file(s) with tagged metadata:\n");
            foreach (var item in tagPlan)
            {
                Console.WriteLine($"[metadata] {Path.GetFileName(item.FilePath)}");
                if (item.OldTitle != item.NewTitle)
                    Console.WriteLine($"    Title : \"{item.OldTitle}\" -> \"{item.NewTitle}\"");
                if (item.OldArtist != item.NewArtist)
                    Console.WriteLine($"    Artist: \"{item.OldArtist}\" -> \"{item.NewArtist}\"");
                if (item.OldAlbum != item.NewAlbum)
                    Console.WriteLine($"    Album : \"{item.OldAlbum}\" -> \"{item.NewAlbum}\"");
            }
        }

        private static void ApplyTagPlan(List<TagItem> tagPlan)
        {
            foreach (var item in tagPlan)
            {
                try
                {
                    using var tf = TagLib.File.Create(item.FilePath);
                    tf.Tag.Title = item.NewTitle;
                    if (!string.IsNullOrEmpty(item.NewArtist))
                        tf.Tag.Performers = new[] { item.NewArtist };
                    tf.Tag.Album = item.NewAlbum;
                    tf.Save();
                    Console.WriteLine($"Updated metadata: {Path.GetFileName(item.FilePath)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to update metadata for {item.FilePath} ({ex.Message})");
                }
            }
        }

        // Keep the console window open when launched from Explorer's context menu.
        private static void Pause()
        {
            Console.WriteLine("\nPress any key to close...");
            Console.ReadKey();
        }
    }
}
