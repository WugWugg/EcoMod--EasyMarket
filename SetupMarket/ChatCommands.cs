using Eco.Core.FileStorage;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Systems;
using Eco.Gameplay.Civics.GameValues.PropertyValues;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Economy;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Gameplay.Systems.Chat;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Gameplay.Systems.Messaging.Notifications;
using Eco.Gameplay.UI;
using Eco.Shared.IoC;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Networking;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyMarket
{
    [ChatCommandHandler]
    [SupportedOSPlatform("windows7.0")]
    public partial class EasyMarketPlugin
    {
        [ChatCommand("Prints sub-commands for EasyMarket.", ChatAuthorizationLevel.Moderator)]
        public static void easymarket() { }

        [ChatSubCommand(
            "easymarket",
            "Changes the deed you are on to this mod's user and changes all stores on it to use be ran by the EasyMarket account.",
            ChatAuthorizationLevel.Admin)]
        public static async void create(User endUser)
        {
            var report = new List<string>();
            // Find the user
            User easyMarketUser;
            try
            {
                easyMarketUser = EasyMarketStatus.GetUser(EasyMarketPlugin.Obj.status);
            }
            catch (Exception ex)
            {
                endUser.Player.ErrorLocStr(ex.Message);
                return;
            }
            report.Add($"Owner: {easyMarketUser.MarkedUpName}");
            // Find the deed.
            Deed deed;
            try
            {
                deed = FindDeed(endUser.Position.XZi());
            }
            catch (DeedMissing ex)
            {
                endUser.Player.ErrorLocStr(ex.Message);
                return;
            }
            if (deed.IsHomesteadDeed)
            {
                endUser.Player.ErrorLoc($"This command will not work on a homestead deed!");
                return;
            }
            report.Add($"Deed: {deed.MarkedUpName}");
            report.Add($"\tPrevious Owner: {deed.Owner?.MarkedUpName ?? "<No Owner>"}");
            // Find the currency.
            Currency currency;
            try
            {
                currency = FindCurrency(EasyMarketConfig.Obj.CurrencyName);
            }
            catch (CurrencyMissing ex)
            {
                endUser.Player.ErrorLocStr(ex.Message);
                return;
            }
            report.Add($"Currency: {currency.MarkedUpName}");
            report.Add($"Bank Account: {easyMarketUser.BankAccount.MarkedUpName}");
            // Find all stores
            report.Add($"Apply To:");
            IEnumerable<WorldObject> stores = ServiceHolder<IWorldObjectManager>.Obj.All
                .Where(x => {
                    return x.HasComponent<StoreComponent>() &&
                        x.HasComponent<CreditComponent>() &&
                        x.GetDeed() == deed;
                });
            foreach (var s in stores)
            {
                report.Add($"\t- {s.MarkedUpName}");
            }
            // Report success!
            var makeChanges = await endUser.Player.ConfirmBox(
                $"<size=150%>Apply these changes?</size><br>" +
                $"<align=left>" +
                Localizer.DoStr(string.Join("<br>", report)) +
                $"</align>"
            );
            if (makeChanges)
            {
                deed.ForceChangeOwners(easyMarketUser, OwnerChangeType.AdminCommand);
                deed.MarkDirty();
                // For each store:
                foreach (var s in stores)
                {
                    var creditComponent = s.GetComponent<CreditComponent>();
                    creditComponent.CreditData.BankAccount = easyMarketUser.BankAccount;
                    creditComponent.CreditData.Currency = currency;
                    var onOffComponent = s.GetComponent<OnOffComponent>();
                    if (onOffComponent != null)
                    {
                        onOffComponent.On = true;
                    }
                }
                endUser.Player.MsgLoc($"{stores.Count()} stores updated.");
            }
            else
            {
                endUser.Player.MsgLoc($"No changes made.");
            }
        }

        [ChatSubCommand(
            "easymarket",
            "Prints the version of this mod.",
            ChatAuthorizationLevel.Moderator)]
        public static void version(IChatClient chat)
        {
            chat.MsgLoc($"{Logger.NAME} version: {EasyMarketPlugin.VERSION}");
        }

        [ChatSubCommand(
            "easymarket",
            "Creates the EasyMarket player. Warning: once created the player cannot be removed from the world.",
            ChatAuthorizationLevel.Admin)]
        public static void initAccount(IChatClient chat)
        {
            var status = EasyMarketPlugin.Obj.GetStatusObject();
            switch (status)
            {
                case EasyMarketStatus.Error err:
                    chat.ErrorLocStr($"No account created.");
                    chat.MsgLocStr($"EasyMarket is not working. Check server logs for errors.");
                    return;
                case EasyMarketStatus.Init:
                    chat.ErrorLocStr($"No account created.");
                    chat.MsgLocStr($"EasyMarket is still starting. Check server logs for errors if this is unexpected.");
                    return;
                case EasyMarketStatus.NeedsCurrency x:
                    chat.ErrorLocStr($"No account created");
                    chat.MsgLocStr($"EasyMarket is already using the {x.user.MarkedUpName} account.");
                    return;
                case EasyMarketStatus.Running x:
                    chat.ErrorLocStr($"No account created");
                    chat.MsgLocStr($"EasyMarket is already running with the {x.user.MarkedUpName} account.");
                    return;
                case EasyMarketStatus.Stopped:
                    chat.ErrorLocStr($"No account created.");
                    chat.MsgLocStr($"EasyMarket has stopped running. Check server logs for errors if this is unexpected.");
                    return;
                default:
                    Logger.Info($"Unexpected status: {status}. Please contact mod author.");
                    chat.ErrorLocStr($"No account created.");
                    chat.MsgLocStr($"EasyMarket is not working as expected. Please contact the mod author.");
                    return;

                case EasyMarketStatus.NeedsAccount:
                    break; // continue
            }
            User easyMarketUser;
            try
            {
                easyMarketUser = EasyMarketPlugin.Obj.GetOrCreateUser(EasyMarketConfig.Obj.OwnerName);
            }
            catch (UsernameConflict ex)
            {
                chat.ErrorLocStr($"No account created.");
                chat.MsgLocStr(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Logger.Info($"Got execption: {ex.Message}");
                chat.ErrorLocStr("No account created.");
                chat.MsgLocStr("EasyMarket failed when creating the account. Check server logs for details.");
                return;
            }
            EasyMarketPlugin.Obj.Initialize(
                new Eco.Core.Utils.TimedTask(Localizer.DoStr($"Re-initializing {Logger.NAME}"))
            );
            chat.OkBoxLoc($"{easyMarketUser.MarkedUpName} created.");
        }

        [ChatSubCommand(
            "easymarket",
            "Creates the currency EasyMarket will use.",
            ChatAuthorizationLevel.Admin)]
        public static void initCurrency(IChatClient chat)
        {
            var status = EasyMarketPlugin.Obj.GetStatusObject();
            User easyMarketUser;
            switch (status)
            {
                case EasyMarketStatus.Error err:
                    chat.ErrorLocStr($"No currency created.");
                    chat.MsgLocStr($"EasyMarket is not working. Check server logs for errors.");
                    return;
                case EasyMarketStatus.Init:
                    chat.ErrorLocStr($"No currency created.");
                    chat.MsgLocStr($"EasyMarket is still starting. Check server logs for errors if this is unexpected.");
                    return;
                case EasyMarketStatus.Running x:
                    chat.ErrorLocStr($"No currency created");
                    chat.MsgLocStr($"EasyMarket is already running with the {x.user.MarkedUpName} account.");
                    return;
                case EasyMarketStatus.Stopped:
                    chat.ErrorLocStr($"No currency created.");
                    chat.MsgLocStr($"EasyMarket has stopped running. Check server logs for errors if this is unexpected.");
                    return;
                case EasyMarketStatus.NeedsAccount:
                    chat.ErrorLocStr($"No currency created.");
                    chat.MsgLocStr($"EasyMarket needs the player {EasyMarketConfig.Obj.OwnerName} before it can create this currency. Please run '/easymarket createAccount' first.");
                    return;
                default:
                    Logger.Info($"Unexpected status: {status}. Please contact mod author.");
                    chat.ErrorLocStr($"No currency created.");
                    chat.MsgLocStr($"EasyMarket is not working as expected. Please contact the mod author.");
                    return;

                case EasyMarketStatus.NeedsCurrency x:
                    easyMarketUser = x.user;
                    break; // continue
            }
            Currency currency;
            try
            {
                currency = EasyMarketPlugin.GetOrCreateCurrency(easyMarketUser, EasyMarketConfig.Obj.CurrencyName);
            } 
            catch (Exception ex)
            {
                Logger.Info($"Got execption: {ex.Message}");
                chat.ErrorLocStr("No currency created.");
                chat.MsgLocStr("EasyMarket failed when creating the currency. Check server logs for details.");
                return;
            }
            EasyMarketPlugin.Obj.Initialize(
                new Eco.Core.Utils.TimedTask(Localizer.DoStr($"Re-initializing {Logger.NAME}"))
            );
            chat.OkBoxLoc($"{currency.MarkedUpName} created.");
        }

        [ChatSubCommand(
            "easymarket",
            "Runs the createAccount, createCurrency, and fund commands. Give this the amount of money the account should start with.",
            ChatAuthorizationLevel.Admin)]
        public static void init(IChatClient chat, float amount)
        {
            var status = EasyMarketPlugin.Obj.GetStatusObject();
            switch (status)
            {
                case EasyMarketStatus.Error err:
                    chat.ErrorLocStr($"No changes made.");
                    chat.MsgLocStr($"EasyMarket is not working. Check server logs for errors.");
                    return;
                case EasyMarketStatus.Init:
                    chat.ErrorLocStr($"No changes made.");
                    chat.MsgLocStr($"EasyMarket is still starting. Check server logs for errors if this is unexpected.");
                    return;
                case EasyMarketStatus.Stopped:
                    chat.ErrorLocStr($"No changes made.");
                    chat.MsgLocStr($"EasyMarket has stopped running. Check server logs for errors if this is unexpected.");
                    return;
                default:
                    Logger.Info($"Unexpected status: {status}. Please contact mod author.");
                    chat.ErrorLocStr($"No changes made.");
                    chat.MsgLocStr($"EasyMarket is not working as expected. Please contact the mod author.");
                    return;
                case EasyMarketStatus.NeedsAccount:
                case EasyMarketStatus.NeedsCurrency:
                case EasyMarketStatus.Running:
                    break;
            }
            // Do not reuse status, each init function will update the status as it goes. Recheck after calling!
            if (EasyMarketPlugin.Obj.GetStatusObject() is EasyMarketStatus.NeedsAccount)
                initAccount(chat);
            else
                chat.MsgLocStr($"Player account already existed.");
            if (EasyMarketPlugin.Obj.GetStatusObject() is EasyMarketStatus.NeedsCurrency)
                initCurrency(chat);
            else
                chat.MsgLocStr($"Currency already existed.");
            if (EasyMarketPlugin.Obj.GetStatusObject() is EasyMarketStatus.Running)
                fund(chat, amount);
        }

        [ChatSubCommand(
            "easymarket",
            "Spawns money into the EasyMarket account.",
            ChatAuthorizationLevel.Admin)]
        public static void fund(IChatClient chat, float amount)
        {
            var status = EasyMarketPlugin.Obj.GetStatusObject();
            switch (status)
            {
                case EasyMarketStatus.Error err:
                    chat.ErrorLocStr($"No money created.");
                    chat.MsgLocStr($"EasyMarket is not working. Check server logs for errors.");
                    return;
                case EasyMarketStatus.Init:
                    chat.ErrorLocStr($"No money created.");
                    chat.MsgLocStr($"EasyMarket is still starting. Check server logs for errors if this is unexpected.");
                    return;
                case EasyMarketStatus.Stopped:
                    chat.ErrorLocStr($"No money created.");
                    chat.MsgLocStr($"EasyMarket has stopped running. Check server logs for errors if this is unexpected.");
                    return;
                case EasyMarketStatus.NeedsCurrency:
                    chat.ErrorLocStr($"No money created.");
                    chat.MsgLocStr($"EasyMarket needs the currency {EasyMarketConfig.Obj.CurrencyName} to exists. Run '/easymarket createCurrency' first.");
                    return;
                case EasyMarketStatus.NeedsAccount:
                    chat.ErrorLocStr($"No money created.");
                    chat.MsgLocStr($"EasyMarket needs the player {EasyMarketConfig.Obj.OwnerName} to exists. Run '/easymarket createAccount' first.");
                    return;
                default:
                    Logger.Info($"Unexpected status: {status}. Please contact mod author.");
                    chat.ErrorLocStr($"No money created.");
                    chat.MsgLocStr($"EasyMarket is not working as expected. Please contact the mod author.");
                    return;
                case EasyMarketStatus.Running x:
                    break; // continue
            }
            // Get currency
            Currency currency;
            try
            {
                currency = FindCurrency(EasyMarketConfig.Obj.CurrencyName);
            }
            catch (Exception ex)
            {
                chat.ErrorLocStr(ex.Message);
                return;
            }
            // Get user
            User easyMarketUser;
            try
            {
                easyMarketUser = EasyMarketStatus.GetUser(EasyMarketPlugin.Obj.status);
            }
            catch (Exception ex)
            {
                chat.ErrorLocStr(ex.Message);
                return;
            }
            var bal = FundUser(easyMarketUser, currency, amount);
            chat.OkBoxLoc($"Spawned {amount} of {currency.MarkedUpName} for {easyMarketUser.MarkedUpName}.");
            chat.MsgLoc($"{easyMarketUser.BankAccount.MarkedUpName} is now at {bal}.");
        }

        [ChatSubCommand(
            "easymarket", 
            "Removes and destroys money from the EasyMarket account.",
            ChatAuthorizationLevel.Admin)]
        public static void defund(IChatClient chat, float amount)
        {
            // Get currency
            Currency currency;
            try
            {
                currency = FindCurrency(EasyMarketConfig.Obj.CurrencyName);
            }
            catch (Exception ex)
            {
                chat.ErrorLocStr(ex.Message);
                return;
            }
            // Get user
            User easyMarketUser;
            try
            {
                easyMarketUser = EasyMarketStatus.GetUser(EasyMarketPlugin.Obj.status);
            }
            catch (Exception ex)
            {
                chat.ErrorLocStr(ex.Message);
                return;
            }
            easyMarketUser.BankAccount.AddCurrency(currency, -amount, assertNegativeAmount: false);
            var bal = easyMarketUser.BankAccount.GetCurrencyHoldingVal(currency);
            chat.MsgLoc($"{easyMarketUser.BankAccount.MarkedUpName} is now at {bal}.");
        }

        [ChatSubCommand(
            "easymarket",
            "Loads a store template and applies them to the store you are looking at.",
            ChatAuthorizationLevel.Admin)]
        public static async void load(User endUser, INetObject target, string templateName)
        {
            var fs = await Eco.Core.PluginManager.Controller.ConfigStorage
                .GetOrCreateDirectoryAsync("EasyMarket");
            var filename = $"{templateName}.json";
            if (!await fs.ExistsAsync(filename))
            {
                endUser.ErrorLocStr("Failed to load.");
                endUser.MsgLoc($"EasyMarket could not find a matching template in the Configs/EasyMarket directory. Filename: {filename}");
                return;
            }
            if (target is WorldObject worldObject && worldObject.HasComponent<StoreComponent>())
            {
                try
                {
                    StoreComponent storeComponent = worldObject.GetComponent<StoreComponent>();
                    var raw = await fs.ReadAllTextAsync(filename);
                    var storeItemDataDTO = JsonSerializer.Deserialize<StoreItemDataDTO>(raw, EasyMarketData.JsonOptions);
                    if (storeItemDataDTO == null) throw new Exception($"Got null from file. Filename: {filename}");
                    if (storeComponent.AllOffers.Any())
                    {
                        var choice = await endUser.Player.OptionBox(
                            Localizer.DoStr($"This store already has trade offers in it. Should the new trade offers be added to or overwrite the existing ones?"),
                            ["Add Them To Existing", "Overwrite and Remove Existing"]);
                        if (choice == -1)
                        {
                            endUser.ErrorLocStr("No changes made.");
                            endUser.MsgLoc($"Popup was closed with out making a selection.");
                            return;
                        }
                        else if (choice == 0) { } // Add Them To Existing
                        else if (choice == 1) // Overwrite and Remove Existing
                        {
                            storeComponent.StoreData.BuyCategories.RemoveAll(_ => true);
                            storeComponent.StoreData.SellCategories.RemoveAll(_ => true);
                        }
                    }
                    storeItemDataDTO.MergeWith(storeComponent);
                    Logger.Info($"Loaded store offers from {filename} into store {worldObject.Name}.");
                    endUser.MsgLocStr(
                        $"Copied To Store: {worldObject.MarkedUpName}<br>" +
                        $"Template: {templateName}<br>" +
                        $"Offers: {storeItemDataDTO.SellCategories.Sum(x => x.Offers.Count)} sell / {storeItemDataDTO.BuyCategories.Sum(x => x.Offers.Count)} buy ");
                }
                catch (Exception ex)
                {
                    endUser.ErrorLocStr("Failed to load.");
                    endUser.MsgLoc($"EasyMarket failed to save the store's data. Check the server logs for errors.");
                    Logger.Info($"Failed to load store data. Error: {ex.Message}");
                }
            }
            else
            {
                endUser.ErrorLocStr("No store found. Please be looking at the store while calling this command.");
                return;
            }
        }

        [ChatSubCommand(
            "easymarket",
            "Saves the trade offers from the store you are looking at into a template.",
            ChatAuthorizationLevel.Admin)]
        public static async void save(User endUser, INetObject target, string templateName)
        {
            var fs = await Eco.Core.PluginManager.Controller.ConfigStorage
                .GetOrCreateDirectoryAsync("EasyMarket");
            var filename = $"{templateName}.json";
            if (await fs.ExistsAsync(filename))
            {
                var overwrite = await endUser.ConfirmBoxLoc($"Template <color=yellow>{templateName}</color> already exists.<br><br><size=200%>Overwrite?</size>");
                if (!overwrite)
                {
                    endUser.MsgLocStr($"No changes made.");
                    return;
                }
            }
            if (target is WorldObject worldObject && worldObject.HasComponent<StoreComponent>()) 
            {
                try
                {
                    StoreComponent storeComponent = worldObject.GetComponent<StoreComponent>();
                    var payload = new StoreItemDataDTO(storeComponent.StoreData);
                    var ser = JsonSerializer.Serialize(payload, EasyMarketData.JsonOptions);
                    await fs.WriteAllTextAsync(filename, ser);
                    endUser.MsgLocStr(
                        $"Copied From Store: {worldObject.MarkedUpName}<br>"+
                        $"Filename: {filename}<br>" +
                        $"Offers: {payload.SellCategories.Sum(x => x.Offers.Count)} sell / {payload.BuyCategories.Sum(x => x.Offers.Count)} buy ");
                    Logger.Info($"Saved store template. Name: {templateName}. Offers: {payload.SellCategories.Sum(x => x.Offers.Count)} sell / {payload.BuyCategories.Sum(x => x.Offers.Count)} buy");
                } catch (Exception ex)
                {
                    endUser.ErrorLocStr("Failed to save.");
                    endUser.MsgLoc($"EasyMarket failed to save the store's data. Check the server logs for errors.");
                    Logger.Info($"Failed to save store data. Error: {ex.Message}");
                }
            } 
            else
            {
                endUser.ErrorLocStr("No store found. Please be looking at the store while calling this command.");
                return;
            }
        }

        [ChatSubCommand(
            "easymarket",
            "Lists the available store templates.",
            ChatAuthorizationLevel.Admin)]
        public static async void list(IChatClient chat)
        {
            var fs = await Eco.Core.PluginManager.Controller.ConfigStorage
                .GetOrCreateDirectoryAsync("EasyMarket");
            var templateNames = (await fs.GetFileNamesAsync())
                .Select(x => x.Replace(".json", ""));
            var msg = string.Join("<br>    - ", templateNames);
            chat.MsgLocStr($"Templates:<br>    - {msg}");
        }
    }
}