using System;
using HarmonyLib;
using Kitchen;
using Kitchen.Formatters.Kitchen;
using MessagePack;

namespace BlueprintShop
{
    /// <summary>
    /// The game serializes IViewData / IResponseData through generated union formatters that
    /// only know vanilla types. Each union value is written as [int key, payload]. These patches
    /// claim our own keys (ShopIds.*UnionKey) and leave every other value to the original code.
    ///
    /// Unmodded players: an unknown key makes the vanilla deserializer skip the value, but the
    /// shop view itself still can't be created on their machine, so every player in an online
    /// game needs this mod.
    /// </summary>
    [HarmonyPatch(typeof(IViewDataFormatter), nameof(IViewDataFormatter.Serialize))]
    internal static class ViewDataSerializePatch
    {
        private static bool Prefix(ref MessagePackWriter writer, IViewData value)
        {
            if (!(value is ShopViewData data))
            {
                return true;
            }
            writer.WriteArrayHeader(2);
            writer.Write(ShopIds.ViewDataUnionKey);
            ShopSerializer.Write(ref writer, data);
            return false;
        }
    }

    [HarmonyPatch(typeof(IViewDataFormatter), nameof(IViewDataFormatter.Deserialize))]
    internal static class ViewDataDeserializePatch
    {
        private static bool Prefix(ref MessagePackReader reader, ref IViewData __result)
        {
            if (!UnionPeek.IsKey(ref reader, ShopIds.ViewDataUnionKey))
            {
                return true;
            }
            reader.ReadArrayHeader();
            reader.ReadInt32();
            __result = ShopSerializer.ReadView(ref reader);
            return false;
        }
    }

    [HarmonyPatch(typeof(IResponseDataFormatter), nameof(IResponseDataFormatter.Serialize))]
    internal static class ResponseSerializePatch
    {
        private static bool Prefix(ref MessagePackWriter writer, IResponseData value)
        {
            if (!(value is ShopResponseData data))
            {
                return true;
            }
            writer.WriteArrayHeader(2);
            writer.Write(ShopIds.ResponseUnionKey);
            ShopSerializer.Write(ref writer, data);
            return false;
        }
    }

    [HarmonyPatch(typeof(IResponseDataFormatter), nameof(IResponseDataFormatter.Deserialize))]
    internal static class ResponseDeserializePatch
    {
        private static bool Prefix(ref MessagePackReader reader, ref IResponseData __result)
        {
            if (!UnionPeek.IsKey(ref reader, ShopIds.ResponseUnionKey))
            {
                return true;
            }
            reader.ReadArrayHeader();
            reader.ReadInt32();
            __result = ShopSerializer.ReadResponse(ref reader);
            return false;
        }
    }

    internal static class UnionPeek
    {
        /// <summary>True if the next value is a 2-element union array whose key is <paramref name="key"/>.
        /// Doesn't advance <paramref name="reader"/>.</summary>
        public static bool IsKey(ref MessagePackReader reader, int key)
        {
            try
            {
                MessagePackReader peek = reader.CreatePeekReader();
                if (peek.End || peek.NextMessagePackType != MessagePackType.Array)
                {
                    return false;
                }
                if (peek.ReadArrayHeader() != 2 || peek.NextMessagePackType != MessagePackType.Integer)
                {
                    return false;
                }
                return peek.ReadInt32() == key;
            }
            catch (Exception)
            {
                // Anything we can't parse isn't ours; let the vanilla formatter handle (or report) it.
                return false;
            }
        }
    }
}
