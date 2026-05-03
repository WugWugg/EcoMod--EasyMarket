using Eco.Core.Controller;
using Eco.Core.PropertyHandling;
using Eco.Core.Systems;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Items;
using Eco.Gameplay.Settlements;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyMarket
{
    public class EasyMarketData
    {
        public static JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }


    /// <summary>
    /// JSON ready object of <see cref="StoreItemData"/>
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public class StoreItemDataDTO
    {
        public List<StoreCategoryDTO> SellCategories { get; set; }
        public List<StoreCategoryDTO> BuyCategories { get; set; }

        public StoreItemDataDTO() { }

        public StoreItemDataDTO(StoreItemData data)
        {
            SellCategories = data.SellCategories.Select(x => new StoreCategoryDTO(x)).ToList();
            BuyCategories = data.BuyCategories.Select(x => new StoreCategoryDTO(x)).ToList();
        }

        public void  MergeWith(StoreComponent component)
        {
            component.StoreData.BuyCategories.AddRange(
                BuyCategories.Select(x => x.IntoStoreCategory(component, true)));
            component.StoreData.SellCategories.AddRange(
                SellCategories.Select(x => x.IntoStoreCategory(component, false)));
        }
    }

    /// <summary>
    /// JSON ready object of <see cref="StoreCategory"/>
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public class StoreCategoryDTO
    {
        public string Name { get; set; }
        public List<TradeOfferDTO> Offers { get; set; }

        public StoreCategoryDTO() { }

        public StoreCategoryDTO(StoreCategory storeCategory)
        {
            Name = storeCategory.Name;
            Offers = storeCategory.Offers.Select(x => new TradeOfferDTO(x)).ToList();
        }

        public StoreCategory IntoStoreCategory(StoreComponent store, bool isBuying)
        {
            var category = new StoreCategory(store, isBuying);
            category.Name = Name;
            foreach (var x in Offers)
            {
                if (x.SellableKind is SellableKind.Item)
                {
                    Type? itemType = AppDomain.CurrentDomain
                        .GetAssemblies()
                        .Select(a => a.GetType(x.ItemID!))
                        .FirstOrDefault(t => t != null);
                    if (itemType is not Type)
                    {
                        Logger.Info($"Failed to resolve item. ItemID: {x.ItemID}");
                        continue;
                    }
                    int? item = Item.GetID(itemType);
                    if (item is not int itemID)
                    {
                        Logger.Info($"Failed to resolve item. ItemID: {x.ItemID}");
                        continue;
                    }
                    category.AddTradeOffer(
                        itemID,
                        x.MinDurability ?? -1,
                        x.MaxDurability ?? -1,
                        x.Price,
                        x.Limit ?? 0,
                        GetSettlementFromName(x.SettlementName),
                        ByteColor.FromHex(x.HexRGBA),
                        x.MinIntegrity ?? -1,
                        x.MaxIntegrity ?? -1);
                }
                else if (x.SellableKind is SellableKind.Tag)
                    category.AddTagTradeOffer(
                        x.TagName!,
                        x.Price,
                        x.Limit ?? 0,
                        x.MinDurability ?? -1,
                        x.MaxDurability ?? -1,
                        x.MinIntegrity ?? -1,
                        x.MaxIntegrity ?? -1);
            }
            return category;
        }

        private Settlement? GetSettlementFromName(string? name)
        {
            if (name == null || name == string.Empty) return null;
            foreach (var settlement in Registrars.Get<Settlement>())
            {
                if (String.Equals(settlement.Name, name, StringComparison.OrdinalIgnoreCase))
                    return settlement;
            }
            Logger.Info($"Failed to resolve settlement for item. Setting to unaffliated. Name: {name}");
            return null;
        }
    }

    /// <summary>
    /// JSON ready object of <see cref="TradeOffer"/>
    /// </summary>
    /// 
    [SupportedOSPlatform("windows7.0")]
    public class TradeOfferDTO
    {        
        public SellableKind SellableKind { get; set; }
        public float Price { get; set; }
        public string? FriendlyName { get; set; }
        public string? ItemID { get; set; }
        public string? TagName { get; set; }

        public int? Limit { get; set; }
        public float? MaxIntegrity { get; set; }
        public float? MinIntegrity { get; set; }
        public float? MaxDurability { get; set; } 
        public float? MinDurability { get; set; }
        public string? SettlementName { get; set; }
        public string? HexRGBA { get; set; }

        public TradeOfferDTO() { }

        public TradeOfferDTO(TradeOffer tradeOffer)
        {
            // Read the item being traded
            if (tradeOffer.Tag != null)
            {
                SellableKind = SellableKind.Tag;
                TagName = tradeOffer.Tag.Name;
            }
            else
            {
                SellableKind = SellableKind.Item;
                ItemID = tradeOffer.Stack.Item.GetType().FullName;
                FriendlyName = tradeOffer.Stack.Item.DisplayName;
            }
            // Read the price
            Price = tradeOffer.Price;
            // Read the meta data if set
            if (tradeOffer.Limit != 0)
                Limit = tradeOffer.Limit;
            if (tradeOffer.MaxIntegrity != -1)
                MaxIntegrity = tradeOffer.MaxIntegrity;
            if (tradeOffer.MinIntegrity != -1)
                MinIntegrity = tradeOffer.MinIntegrity;
            if (tradeOffer.MaxDurability != -1)
                MaxDurability = tradeOffer.MaxDurability;
            if (tradeOffer.MinDurability != -1)
                MinDurability = tradeOffer.MinDurability;
            if (tradeOffer.Stack.Item is ISettlementAssociated)
                SettlementName = tradeOffer.Settlement?.Name ?? String.Empty;
            if (tradeOffer.Stack.Item is ColorItem)
                HexRGBA = tradeOffer.Color.HexRGBA;
        }
    }

    public enum SellableKind
    {
        Item,
        Tag
    }
}
