using System;
using System.Collections.Generic;
using Kitchen;
using MessagePack;

namespace BlueprintShop
{
    public enum ShopEntryKind : byte
    {
        Appliance = 0,
        /// <summary>Wallpaper or flooring (KitchenData.Decor), free, decoration nights only.</summary>
        Decor = 1
    }

    [Flags]
    public enum ShopEntryFlags : byte
    {
        None = 0,
        Staple = 1,
        Upgrade = 2,
        /// <summary>SellOnlyAsUnique: at most one per order, and only while none is owned or pending.</summary>
        Unique = 4
    }

    public struct ShopEntry : IEquatable<ShopEntry>
    {
        public ShopEntryKind Kind;
        public int ID;
        public int Price;
        public ShopEntryFlags Flags;

        public bool Equals(ShopEntry other) =>
            Kind == other.Kind && ID == other.ID && Price == other.Price && Flags == other.Flags;
    }

    public enum ShopAction : byte
    {
        None = 0,
        Purchase = 1,
        TakeBlueprint = 2,
        Close = 3,
        /// <summary>Sell what the owner is holding. One line: the held appliance's ID.</summary>
        Sell = 4,
        /// <summary>Sell everything outside (bin icon) that was bought. One line whose Quantity
        /// is the OutsideHash the menu showed, so a changed list is refused.</summary>
        SellOutside = 5,
        /// <summary>Purchase, but appliances go into the buyer's hands one by one (build queue)
        /// instead of parcels. Same lines as Purchase.</summary>
        PurchaseToHands = 6,
        /// <summary>Send the owner's items still waiting in the build queue to the door as parcels.</summary>
        QueueToDoor = 7
    }

    /// <summary>Outside items of one kind: how many will sell (and for how much), how many are kept.</summary>
    public struct OutsideLine
    {
        public int ApplianceID;
        public bool IsParcel;
        public int SellCount;
        public int Total;
        public int RequiredCount;
    }

    public enum HeldKind : byte
    {
        None = 0,
        Appliance = 1,
        Parcel = 2,
        /// <summary>Holding something that can't be sold (blueprint, item, ...).</summary>
        Other = 3
    }

    [Flags]
    public enum ShopStateFlags : byte
    {
        None = 0,
        /// <summary>prepare_uninstall is on: purchases aren't tagged, refunds are $0.</summary>
        UninstallPrep = 1
    }

    public struct ShopOrderLine
    {
        public ShopEntryKind Kind;
        public int ID;
        public int Quantity;
    }

    public struct OwnedCount
    {
        public int ID;
        public int Count;
    }

    /// <summary>Host -> every machine. Only the owner's machine shows anything.</summary>
    public struct ShopViewData : IViewData, IViewResponseData, IViewData.ICheckForChanges<ShopViewData>
    {
        public int OwnerPlayerID;
        public int OwnerInputSource;
        public int Money;
        public int Day;
        public bool IsDecorNight;
        public int CatalogHash;
        public List<ShopEntry> Entries;
        /// <summary>Highest response Sequence the host has processed for this session.</summary>
        public int AckSequence;
        public bool LastSucceeded;
        public string Status;
        // v2
        public ShopStateFlags StateFlags;
        public HeldKind HeldKind;
        public int HeldID;
        public int HeldRefund;
        public int OwnedHash;
        public List<OwnedCount> Owned;
        // v3
        public bool HeldRequired;
        public int OutsideHash;
        public List<OutsideLine> Outside;
        // v4
        /// <summary>The owner's items waiting in the build queue (not counting one in their hands).</summary>
        public int QueueCount;
        /// <summary>What was paid for the held item (HeldRefund is this minus the sale loss).</summary>
        public int HeldPaid;
        /// <summary>The host's sale loss setting, for the sell box's explanation.</summary>
        public int SaleLossPercent;
        /// <summary>The status line under the title (see UpgradeUnlock).</summary>
        public UpgradeState UpgradeState;
        /// <summary>LockedUntilNight: the night upgrades unlock on. UnlockedEarlier: the night they unlocked.</summary>
        public int UpgradeNight;
        /// <summary>Roll mode: tonight's and next night's unlock chance, in tenths of a percent.</summary>
        public int UpgradeChanceTonight;
        public int UpgradeChanceNext;

        public bool IsChangedFrom(ShopViewData check)
        {
            return OwnerPlayerID != check.OwnerPlayerID
                || OwnerInputSource != check.OwnerInputSource
                || Money != check.Money
                || Day != check.Day
                || IsDecorNight != check.IsDecorNight
                || CatalogHash != check.CatalogHash
                || AckSequence != check.AckSequence
                || LastSucceeded != check.LastSucceeded
                || Status != check.Status
                || StateFlags != check.StateFlags
                || HeldKind != check.HeldKind
                || HeldID != check.HeldID
                || HeldRefund != check.HeldRefund
                || OwnedHash != check.OwnedHash
                || HeldRequired != check.HeldRequired
                || OutsideHash != check.OutsideHash
                || QueueCount != check.QueueCount
                || HeldPaid != check.HeldPaid
                || SaleLossPercent != check.SaleLossPercent
                || UpgradeState != check.UpgradeState
                || UpgradeNight != check.UpgradeNight
                || UpgradeChanceTonight != check.UpgradeChanceTonight
                || UpgradeChanceNext != check.UpgradeChanceNext;
        }
    }

    /// <summary>Host -> every machine: one player's build queue. Only the owner's machine shows it.</summary>
    public struct BuildQueueData : IViewData, IViewData.ICheckForChanges<BuildQueueData>
    {
        public int PlayerID;
        public int InputSource;
        /// <summary>Appliance ID of the queued item in the player's hands, or 0.</summary>
        public int HeldID;
        /// <summary>Items are waiting but the player is holding something else.</summary>
        public bool HandsBusy;
        /// <summary>Waiting items per appliance ID, in purchase order.</summary>
        public List<OwnedCount> Waiting;

        public bool IsChangedFrom(BuildQueueData check)
        {
            if (PlayerID != check.PlayerID || InputSource != check.InputSource || HeldID != check.HeldID
                || HandsBusy != check.HandsBusy || (Waiting?.Count ?? 0) != (check.Waiting?.Count ?? 0))
            {
                return true;
            }
            for (int i = 0; i < (Waiting?.Count ?? 0); i++)
            {
                if (Waiting[i].ID != check.Waiting[i].ID || Waiting[i].Count != check.Waiting[i].Count)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Host -> every machine: the floating refund label over one binned appliance.</summary>
    public struct SaleLabelData : IViewData, IViewData.ICheckForChanges<SaleLabelData>
    {
        public int Refund;
        /// <summary>Kept because the menu needs it; shown as "Needed" instead of a price.</summary>
        public bool Required;

        public bool IsChangedFrom(SaleLabelData check) => Refund != check.Refund || Required != check.Required;
    }

    /// <summary>Owner's machine -> host.</summary>
    public struct ShopResponseData : IResponseData, IViewResponseData
    {
        public int Sequence;
        public ShopAction Action;
        public List<ShopOrderLine> Lines;
    }

    /// <summary>
    /// Hand-written MessagePack encoding for the two message types. The game's unions are
    /// closed generated code, so ShopNetworkPatches routes our types here.
    /// Every list is length-capped on read so a malformed packet can't allocate unbounded memory.
    /// FormatVersion must change whenever the layout does: mismatched mods then fail loudly.
    /// </summary>
    internal static class ShopSerializer
    {
        private const int FormatVersion = 4;
        private const int ViewFields = 27;
        private const int LabelFields = 3;
        private const int QueueFields = 6;
        private const int MaxEntries = 4096;
        private const int MaxLines = 512;
        private const int MaxStatusLength = 512;

        public static void Write(ref MessagePackWriter writer, ShopViewData d)
        {
            writer.WriteArrayHeader(ViewFields);
            writer.Write(FormatVersion);
            writer.Write(d.OwnerPlayerID);
            writer.Write(d.OwnerInputSource);
            writer.Write(d.Money);
            writer.Write(d.Day);
            writer.Write(d.IsDecorNight);
            writer.Write(d.CatalogHash);
            int count = d.Entries?.Count ?? 0;
            writer.WriteArrayHeader(count);
            for (int i = 0; i < count; i++)
            {
                ShopEntry e = d.Entries[i];
                writer.WriteArrayHeader(4);
                writer.Write((byte)e.Kind);
                writer.Write(e.ID);
                writer.Write(e.Price);
                writer.Write((byte)e.Flags);
            }
            writer.Write(d.AckSequence);
            writer.Write(d.LastSucceeded);
            writer.Write(d.Status ?? "");
            writer.Write((byte)d.StateFlags);
            writer.Write((byte)d.HeldKind);
            writer.Write(d.HeldID);
            writer.Write(d.HeldRefund);
            writer.Write(d.OwnedHash);
            int owned = d.Owned?.Count ?? 0;
            writer.WriteArrayHeader(owned * 2);
            for (int i = 0; i < owned; i++)
            {
                writer.Write(d.Owned[i].ID);
                writer.Write(d.Owned[i].Count);
            }
            writer.Write(d.HeldRequired);
            writer.Write(d.OutsideHash);
            int outside = d.Outside?.Count ?? 0;
            writer.WriteArrayHeader(outside);
            for (int i = 0; i < outside; i++)
            {
                OutsideLine l = d.Outside[i];
                writer.WriteArrayHeader(5);
                writer.Write(l.ApplianceID);
                writer.Write(l.IsParcel);
                writer.Write(l.SellCount);
                writer.Write(l.Total);
                writer.Write(l.RequiredCount);
            }
            writer.Write(d.QueueCount);
            writer.Write(d.HeldPaid);
            writer.Write(d.SaleLossPercent);
            writer.Write((byte)d.UpgradeState);
            writer.Write(d.UpgradeNight);
            writer.Write(d.UpgradeChanceTonight);
            writer.Write(d.UpgradeChanceNext);
        }

        public static void Write(ref MessagePackWriter writer, BuildQueueData d)
        {
            writer.WriteArrayHeader(QueueFields);
            writer.Write(FormatVersion);
            writer.Write(d.PlayerID);
            writer.Write(d.InputSource);
            writer.Write(d.HeldID);
            writer.Write(d.HandsBusy);
            int count = d.Waiting?.Count ?? 0;
            writer.WriteArrayHeader(count * 2);
            for (int i = 0; i < count; i++)
            {
                writer.Write(d.Waiting[i].ID);
                writer.Write(d.Waiting[i].Count);
            }
        }

        public static BuildQueueData ReadQueue(ref MessagePackReader reader)
        {
            int fields = reader.ReadArrayHeader();
            int version = reader.ReadInt32();
            if (version != FormatVersion || fields != QueueFields)
            {
                throw new MessagePackSerializationException(
                    $"{ModMain.Name}: build queue data format {version}/{fields} doesn't match this version of the mod ({FormatVersion}). All players need the same mod version.");
            }
            BuildQueueData d = default;
            d.PlayerID = reader.ReadInt32();
            d.InputSource = reader.ReadInt32();
            d.HeldID = reader.ReadInt32();
            d.HandsBusy = reader.ReadBoolean();
            int values = reader.ReadArrayHeader();
            if (values > MaxEntries * 2 || values % 2 != 0)
            {
                throw new MessagePackSerializationException($"{ModMain.Name}: malformed build queue ({values})");
            }
            d.Waiting = new List<OwnedCount>(values / 2);
            for (int i = 0; i < values / 2; i++)
            {
                d.Waiting.Add(new OwnedCount { ID = reader.ReadInt32(), Count = reader.ReadInt32() });
            }
            return d;
        }

        public static void Write(ref MessagePackWriter writer, SaleLabelData d)
        {
            writer.WriteArrayHeader(LabelFields);
            writer.Write(FormatVersion);
            writer.Write(d.Refund);
            writer.Write(d.Required);
        }

        public static SaleLabelData ReadLabel(ref MessagePackReader reader)
        {
            int fields = reader.ReadArrayHeader();
            int version = reader.ReadInt32();
            if (version != FormatVersion || fields != LabelFields)
            {
                throw new MessagePackSerializationException(
                    $"{ModMain.Name}: label data format {version}/{fields} doesn't match this version of the mod ({FormatVersion}). All players need the same mod version.");
            }
            return new SaleLabelData
            {
                Refund = reader.ReadInt32(),
                Required = reader.ReadBoolean()
            };
        }

        public static ShopViewData ReadView(ref MessagePackReader reader)
        {
            int fields = reader.ReadArrayHeader();
            int version = reader.ReadInt32();
            if (version != FormatVersion || fields != ViewFields)
            {
                throw new MessagePackSerializationException(
                    $"{ModMain.Name}: view data format {version}/{fields} doesn't match this version of the mod ({FormatVersion}). All players need the same mod version.");
            }
            ShopViewData d = default;
            d.OwnerPlayerID = reader.ReadInt32();
            d.OwnerInputSource = reader.ReadInt32();
            d.Money = reader.ReadInt32();
            d.Day = reader.ReadInt32();
            d.IsDecorNight = reader.ReadBoolean();
            d.CatalogHash = reader.ReadInt32();
            int count = reader.ReadArrayHeader();
            if (count > MaxEntries)
            {
                throw new MessagePackSerializationException($"{ModMain.Name}: too many catalog entries ({count})");
            }
            d.Entries = new List<ShopEntry>(count);
            for (int i = 0; i < count; i++)
            {
                if (reader.ReadArrayHeader() != 4)
                {
                    throw new MessagePackSerializationException($"{ModMain.Name}: malformed catalog entry");
                }
                d.Entries.Add(new ShopEntry
                {
                    Kind = (ShopEntryKind)reader.ReadByte(),
                    ID = reader.ReadInt32(),
                    Price = reader.ReadInt32(),
                    Flags = (ShopEntryFlags)reader.ReadByte()
                });
            }
            d.AckSequence = reader.ReadInt32();
            d.LastSucceeded = reader.ReadBoolean();
            d.Status = Truncate(reader.ReadString());
            d.StateFlags = (ShopStateFlags)reader.ReadByte();
            d.HeldKind = (HeldKind)reader.ReadByte();
            d.HeldID = reader.ReadInt32();
            d.HeldRefund = reader.ReadInt32();
            d.OwnedHash = reader.ReadInt32();
            int ownedValues = reader.ReadArrayHeader();
            if (ownedValues > MaxEntries * 2 || ownedValues % 2 != 0)
            {
                throw new MessagePackSerializationException($"{ModMain.Name}: malformed owned counts ({ownedValues})");
            }
            d.Owned = new List<OwnedCount>(ownedValues / 2);
            for (int i = 0; i < ownedValues / 2; i++)
            {
                d.Owned.Add(new OwnedCount { ID = reader.ReadInt32(), Count = reader.ReadInt32() });
            }
            d.HeldRequired = reader.ReadBoolean();
            d.OutsideHash = reader.ReadInt32();
            int outside = reader.ReadArrayHeader();
            if (outside > MaxEntries)
            {
                throw new MessagePackSerializationException($"{ModMain.Name}: too many outside lines ({outside})");
            }
            d.Outside = new List<OutsideLine>(outside);
            for (int i = 0; i < outside; i++)
            {
                if (reader.ReadArrayHeader() != 5)
                {
                    throw new MessagePackSerializationException($"{ModMain.Name}: malformed outside line");
                }
                d.Outside.Add(new OutsideLine
                {
                    ApplianceID = reader.ReadInt32(),
                    IsParcel = reader.ReadBoolean(),
                    SellCount = reader.ReadInt32(),
                    Total = reader.ReadInt32(),
                    RequiredCount = reader.ReadInt32()
                });
            }
            d.QueueCount = reader.ReadInt32();
            d.HeldPaid = reader.ReadInt32();
            d.SaleLossPercent = reader.ReadInt32();
            d.UpgradeState = (UpgradeState)reader.ReadByte();
            d.UpgradeNight = reader.ReadInt32();
            d.UpgradeChanceTonight = reader.ReadInt32();
            d.UpgradeChanceNext = reader.ReadInt32();
            return d;
        }

        public static void Write(ref MessagePackWriter writer, ShopResponseData r)
        {
            writer.WriteArrayHeader(4);
            writer.Write(FormatVersion);
            writer.Write(r.Sequence);
            writer.Write((byte)r.Action);
            int count = r.Lines?.Count ?? 0;
            writer.WriteArrayHeader(count);
            for (int i = 0; i < count; i++)
            {
                ShopOrderLine l = r.Lines[i];
                writer.WriteArrayHeader(3);
                writer.Write((byte)l.Kind);
                writer.Write(l.ID);
                writer.Write(l.Quantity);
            }
        }

        public static ShopResponseData ReadResponse(ref MessagePackReader reader)
        {
            int fields = reader.ReadArrayHeader();
            int version = reader.ReadInt32();
            if (version != FormatVersion || fields != 4)
            {
                throw new MessagePackSerializationException(
                    $"{ModMain.Name}: response format {version}/{fields} doesn't match this version of the mod ({FormatVersion}). All players need the same mod version.");
            }
            ShopResponseData r = default;
            r.Sequence = reader.ReadInt32();
            r.Action = (ShopAction)reader.ReadByte();
            int count = reader.ReadArrayHeader();
            if (count > MaxLines)
            {
                throw new MessagePackSerializationException($"{ModMain.Name}: too many order lines ({count})");
            }
            r.Lines = new List<ShopOrderLine>(count);
            for (int i = 0; i < count; i++)
            {
                if (reader.ReadArrayHeader() != 3)
                {
                    throw new MessagePackSerializationException($"{ModMain.Name}: malformed order line");
                }
                r.Lines.Add(new ShopOrderLine
                {
                    Kind = (ShopEntryKind)reader.ReadByte(),
                    ID = reader.ReadInt32(),
                    Quantity = reader.ReadInt32()
                });
            }
            return r;
        }

        private static string Truncate(string s)
        {
            if (s == null)
            {
                return "";
            }
            return s.Length <= MaxStatusLength ? s : s.Substring(0, MaxStatusLength);
        }
    }
}
