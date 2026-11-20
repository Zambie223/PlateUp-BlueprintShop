using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KitchenData;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>One saved list of shop items (a cart saved for later).</summary>
    internal sealed class Wishlist
    {
        public string Name;
        public readonly List<(ShopEntryKind Kind, int ID, int Quantity)> Items = new List<(ShopEntryKind, int, int)>();

        public int TotalQuantity
        {
            get
            {
                int n = 0;
                foreach ((ShopEntryKind _, int _, int q) in Items)
                {
                    n += q;
                }
                return n;
            }
        }

        public bool SameItemsAs(Wishlist other)
        {
            if (other.Items.Count != Items.Count)
            {
                return false;
            }
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] != other.Items[i])
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Wishlists (user request 2026-11-19): named lists of shop items, saved on this computer
    /// for every run and game (each player keeps their own), loaded back into the cart from the
    /// shop. Mod List's options page can export them to the clipboard and import them back.
    ///
    /// File: BlueprintShop-wishlists.txt next to BlueprintShop.cfg. The clipboard uses the same
    /// text, so a shared list can also be pasted into the file. Items are stored by game data
    /// ID (names differ between languages); the "# name" after each item is only a comment.
    /// </summary>
    internal static class Wishlists
    {
        public const string FileName = "BlueprintShop-wishlists.txt";
        public const int MaxLists = 200;
        public const int MaxItemsPerList = 512;
        public const int MaxNameLength = 32;
        private const string Header = "# Blueprint Shop wishlists";

        private static List<Wishlist> _lists;
        private static string _path;

        public static List<Wishlist> All
        {
            get
            {
                EnsureLoaded();
                return _lists;
            }
        }

        private static string FilePath => _path ?? (_path = Path.Combine(Application.persistentDataPath, FileName));

        private static void EnsureLoaded()
        {
            if (_lists != null)
            {
                return;
            }
            _lists = new List<Wishlist>();
            try
            {
                if (File.Exists(FilePath))
                {
                    _lists.AddRange(Parse(File.ReadAllText(FilePath)));
                    ShopLog.Info($"Loaded {_lists.Count} wishlist(s).");
                }
            }
            catch (Exception e)
            {
                ShopLog.Warn($"Couldn't read {FileName}: {e.Message}");
            }
        }

        public static void Save()
        {
            EnsureLoaded();
            string tmp = FilePath + ".tmp";
            try
            {
                File.WriteAllText(tmp, Format(_lists));
                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                ShopLog.Warn($"Couldn't save {FileName}: {e.Message}");
            }
        }

        /// <summary>A name not used by any list yet: the given one, or "Name (2)" and so on.</summary>
        public static string UniqueName(string wanted)
        {
            EnsureLoaded();
            string name = CleanName(wanted);
            if (name.Length == 0)
            {
                for (int i = _lists.Count + 1; ; i++)
                {
                    name = $"Wishlist {i}";
                    if (Find(name) == null)
                    {
                        return name;
                    }
                }
            }
            if (Find(name) == null)
            {
                return name;
            }
            for (int i = 2; ; i++)
            {
                string suffix = $" ({i})";
                string candidate = (name.Length + suffix.Length > MaxNameLength ? name.Substring(0, MaxNameLength - suffix.Length) : name) + suffix;
                if (Find(candidate) == null)
                {
                    return candidate;
                }
            }
        }

        public static Wishlist Find(string name)
        {
            foreach (Wishlist w in All)
            {
                if (string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return w;
                }
            }
            return null;
        }

        public static void Add(Wishlist list)
        {
            EnsureLoaded();
            _lists.Add(list);
            Save();
        }

        public static void Remove(Wishlist list)
        {
            EnsureLoaded();
            _lists.Remove(list);
            Save();
        }

        // ----------------------------------------------------------------- clipboard (Mod List page)

        public enum IoResult
        {
            None,
            Exported,
            NothingToExport,
            Imported,
            ImportedNothingNew,
            NothingToImport,
            Failed
        }

        /// <summary>What the last export/import did, shown on the options page until it closes.</summary>
        public static IoResult LastResult;

        public static void ExportToClipboard()
        {
            try
            {
                if (All.Count == 0)
                {
                    LastResult = IoResult.NothingToExport;
                    return;
                }
                GUIUtility.systemCopyBuffer = Format(All);
                LastResult = IoResult.Exported;
                ShopLog.Info($"Copied {All.Count} wishlist(s) to the clipboard.");
            }
            catch (Exception e)
            {
                LastResult = IoResult.Failed;
                ShopLog.Warn($"Couldn't copy the wishlists to the clipboard: {e.Message}");
            }
        }

        public static void ImportFromClipboard()
        {
            try
            {
                List<Wishlist> incoming = Parse(GUIUtility.systemCopyBuffer ?? "");
                if (incoming.Count == 0)
                {
                    LastResult = IoResult.NothingToImport;
                    return;
                }
                int added = 0;
                foreach (Wishlist w in incoming)
                {
                    Wishlist existing = Find(w.Name);
                    if (existing != null && existing.SameItemsAs(w))
                    {
                        continue; // already have exactly this list
                    }
                    if (All.Count >= MaxLists)
                    {
                        break;
                    }
                    w.Name = UniqueName(w.Name);
                    _lists.Add(w);
                    added++;
                }
                if (added > 0)
                {
                    Save();
                }
                LastResult = added > 0 ? IoResult.Imported : IoResult.ImportedNothingNew;
                ShopLog.Info($"Imported {added} of {incoming.Count} wishlist(s) from the clipboard.");
            }
            catch (Exception e)
            {
                LastResult = IoResult.Failed;
                ShopLog.Warn($"Couldn't import wishlists from the clipboard: {e.Message}");
            }
        }

        // ----------------------------------------------------------------- text format

        public static string Format(List<Wishlist> lists)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            sb.Append("# Each list starts with \"list: <name>\". Each item is \"<appliance|decor> <id> x<count>\"; the text after # is just a note.\n");
            foreach (Wishlist w in lists)
            {
                sb.Append('\n').Append("list: ").Append(w.Name).Append('\n');
                foreach ((ShopEntryKind kind, int id, int qty) in w.Items)
                {
                    sb.Append(kind == ShopEntryKind.Decor ? "decor " : "appliance ")
                        .Append(id.ToString(CultureInfo.InvariantCulture))
                        .Append(" x").Append(qty.ToString(CultureInfo.InvariantCulture))
                        .Append("  # ").Append(ItemName(kind, id)).Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>Reads lists from text; anything it doesn't understand is skipped.</summary>
        public static List<Wishlist> Parse(string text)
        {
            var lists = new List<Wishlist>();
            Wishlist current = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                if (line.StartsWith("list:", StringComparison.OrdinalIgnoreCase))
                {
                    if (lists.Count >= MaxLists)
                    {
                        break;
                    }
                    string name = CleanName(line.Substring(5));
                    current = new Wishlist { Name = name.Length > 0 ? name : $"Wishlist {lists.Count + 1}" };
                    lists.Add(current);
                    continue;
                }
                if (current == null || current.Items.Count >= MaxItemsPerList)
                {
                    continue;
                }
                int hash = line.IndexOf('#');
                if (hash >= 0)
                {
                    line = line.Substring(0, hash).Trim();
                }
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }
                ShopEntryKind kind;
                if (parts[0].Equals("appliance", StringComparison.OrdinalIgnoreCase))
                {
                    kind = ShopEntryKind.Appliance;
                }
                else if (parts[0].Equals("decor", StringComparison.OrdinalIgnoreCase))
                {
                    kind = ShopEntryKind.Decor;
                }
                else
                {
                    continue;
                }
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    continue;
                }
                int qty = 1;
                if (parts.Length >= 3)
                {
                    string q = parts[2].TrimStart('x', 'X');
                    if (!int.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out qty))
                    {
                        continue;
                    }
                }
                qty = Mathf.Clamp(qty, 1, ShopService.MaxQuantityPerLine);
                int at = current.Items.FindIndex(i => i.Kind == kind && i.ID == id);
                if (at >= 0)
                {
                    (ShopEntryKind k, int i2, int q2) = current.Items[at];
                    current.Items[at] = (k, i2, Mathf.Min(q2 + qty, ShopService.MaxQuantityPerLine));
                }
                else
                {
                    current.Items.Add((kind, id, qty));
                }
            }
            lists.RemoveAll(w => w.Items.Count == 0);
            return lists;
        }

        public static string CleanName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "")
            {
                // No control characters, and no rich-text tags in the shop's labels.
                if (!char.IsControl(c) && c != '<' && c != '>')
                {
                    sb.Append(c);
                }
            }
            string s = sb.ToString().Trim();
            return s.Length > MaxNameLength ? s.Substring(0, MaxNameLength).Trim() : s;
        }

        public static string ItemName(ShopEntryKind kind, int id)
        {
            if (kind == ShopEntryKind.Appliance && GameData.Main.TryGet(id, out Appliance a))
            {
                return a.Name;
            }
            if (kind == ShopEntryKind.Decor && GameData.Main.TryGet(id, out Decor d))
            {
                return d.name;
            }
            return $"#{id}";
        }
    }
}
