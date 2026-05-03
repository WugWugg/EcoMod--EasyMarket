using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Server;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Utils;
using StrangeCloud.Service.Client.Contracts;
using System.Runtime.Versioning;
using System.Text.Json;

namespace EasyMarket
{
    [Localized]

    [SupportedOSPlatform("windows7.0")]
    public class EasyMarketConfig : Singleton<EasyMarketConfig>
    {
        [LocDescription("Required. The name of the currency to be used.")]
        public string CurrencyName { get; set; } = string.Empty;

        [LocDescription("Required, but has default. The name of the user account who owns the markets.")]
        public string OwnerName { get; set; } = string.Empty;
    }

    [SupportedOSPlatform("windows7.0")]
    public partial class EasyMarketPlugin: Singleton<EasyMarketPlugin>, IModKitPlugin, IConfigurablePlugin, IInitializablePlugin, IShutdownablePlugin
    {
        /// <summary>
        /// Version of the mod. Keep in sync with release version/numbers.
        /// Use in /EasyMarket chat command so server admins can know what version of the mod they have installed.
        /// </summary>
        public const string VERSION = "v0.0.1";

        /// <summary>
        /// This is the UUID of the account that this mod creates. It is purposefully hardcoded so that it can tell if a user is this system user or a real player who
        /// just happens to be using the exact same name.
        /// </summary>
        static readonly Guid UUID = Guid.Parse("9a306b6b-bf59-4fcf-b47e-23c211beeb05");

        /// <summary>
        /// JSON serializer options.
        /// Currently, this will pretty-prinand converts property names from `NameOfProperty` to 'nameOfProperty`.
        /// <br/>
        /// Example: <code>JsonSerializer.Serialize(payload, EasyMarketPlugin.JsonOptions);</code>
        /// </summary>
        public static JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        #region IModKitPlugin
        public string GetCategory() => "Mods";

        private EasyMarketStatus status = new EasyMarketStatus.Init();
        public string GetStatus() => EasyMarketStatus.PrintStatus(status);
        #endregion

        #region IConfigurablePlugin
        readonly PluginConfig<EasyMarketConfig> config = new("EasyMarket");
        public ThreadSafeAction<object, string> ParamChanged { get; set; } = new();
        public IPluginConfig PluginConfig => this.config;
        public object GetEditObject() => this.config.Config;
        public void OnEditObjectChanged(object o, string param)
        {            
            // Notify downstream
            ParamChanged.Invoke(o, param);
            // Write changes to config file
            this.SaveConfig();
            if (param == nameof(config.Config.CurrencyName) ||
                param == nameof(config.Config.OwnerName))
            {
                Initialize(new TimedTask(Localizer.DoStr($"Re-initializing {Logger.NAME}")));
            }
        }
        #endregion

        #region IInitializablePlugin
        public void Initialize(TimedTask _timer)
        {
            try
            {
                CheckConfig();
                if (PluginManager.Obj.Initialized)
                {
                    SetStatus();
                } else
                {
                    PluginManager.Obj.InitComplete += SetStatus;
                }
            }
            catch (Exception ex) 
            {
                status = new EasyMarketStatus.Error($"Initialization failed: {ex.Message}");
            }
        }

        private void SetStatus()
        {
            var username = config.Config.OwnerName;
            var user = FindUser(username);
            if (user == null)
            {
                status = new EasyMarketStatus.NeedsAccount();
                return;
            }
            else if (!IsEasyMarketUser(user))
            {
                status = new EasyMarketStatus.Error(new UsernameConflict(username).Message);
                return;
            }
            var currencyName = config.Config.CurrencyName;
            Currency currency;
            try
            {
                currency = FindCurrency(currencyName);
            } catch (CurrencyMissing)
            {
                status = new EasyMarketStatus.NeedsCurrency(user);
                return;
            }
            status = new EasyMarketStatus.Running(user);
        }

        /// <summary>
        /// Checks for missing or malformed config fields.
        /// </summary>
        /// <exception cref="MissingRequiredConfig">Thrown in a required config value has not been set.</exception>
        private void CheckConfig()
        {
            if (config.Config.CurrencyName == string.Empty || config.Config.CurrencyName == null)
                throw new MissingRequiredConfig(nameof(EasyMarketConfig.CurrencyName));
        }
        #endregion

        #region IShutdownablePlugin
        public Task ShutdownAsync()
        {
            status = new EasyMarketStatus.Stopped();
            return Task.CompletedTask;
        }
        #endregion

        #region User
        private bool IsEasyMarketUser(User user)
        {
            return Guid.Parse(user.StrangeId) == EasyMarketPlugin.UUID;
        }

        public User? FindUser(string name)
        {
            return UserManager.FindUserByName(name);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="UsernameConflict">Throws if a user already exists and doesn't match the expected UUID.</exception>
        public User GetOrCreateUser(string name)
        {
            var maybeUser = FindUser(name);
            if (maybeUser != null)
            {
                if (!IsEasyMarketUser(maybeUser))
                    throw new UsernameConflict(name);
                else
                    return maybeUser;
            }
            return CreateEasyMarketUser(name);
        }

        public static User CreateEasyMarketUser(string name)
        {
            var account = new StrangeUser()
            {
                Id = EasyMarketPlugin.UUID,
                Username = name
            };
            Logger.Info($"Creating new user with username: {name}");
            return UserManager.Obj.CreateNewUser(account, name);
        }
        #endregion

        #region Bank & Money
        /// <summary>
        /// Spawns money and gives it to the user.
        /// </summary>
        /// <param name="user">User to give the money to.</param>
        /// <param name="currency">The currency to spawn.</param>
        /// <param name="amount">Amount of money to spawn.</param>
        /// <returns>The balance of this user's account after funding.</returns>
        private static float FundUser(User user, Currency currency, float amount)
        {
            BankAccountManager.Obj.SpawnMoney(currency, user, amount);
            var bal = user.BankAccount.GetCurrencyHoldingVal(currency);
            Logger.Info($"Funded {amount} of {currency.Name} to {user.Name}. Current Balance: {bal}");
            return bal;
        }

        /// <summary>
        /// If the user has no money of this currency, this returns true. Otherwise false.
        /// </summary>
        /// <param name="user">User to check.</param>
        /// <param name="currency">Currency to check for.</param>
        /// <returns></returns>
        public static bool ShouldFund(User user, Currency currency)
        {
            return user.BankAccount.GetCurrencyHoldingVal(currency) <= 0;
        }

        /// <summary>
        /// Gets the currency. If it doesn't exists it makes it.
        /// </summary>
        /// <param name="user">User to create the currency with (if needed).</param>
        /// <param name="name">Currency's name.</param>
        /// <returns></returns>
        public static Currency GetOrCreateCurrency(User user, string name)
        {
            try
            {
                return FindCurrency(name);
            } 
            catch (CurrencyMissing)
            {
                return CreateCurrency(user, name);
            }
        }

        /// <summary>
        /// Get's the currency object with the given name.
        /// </summary>
        /// <param name="name">The currency's name</param>
        /// <returns></returns>
        /// <exception cref="CurrencyMissing">Throws if currency has not been created.</exception>
        public static Currency FindCurrency(string name)
        {
            var currency = CurrencyManager.Currencies.FirstOrDefault(x => x.Name == name);
            if (currency == null)
                throw new CurrencyMissing(name);
            return currency;
        }

        /// <summary>
        /// Creates a new currency.
        /// </summary>
        /// <param name="user">The user who is creating the currency.</param>
        /// <param name="name">The currency's name.</param>
        /// <returns></returns>
        public static Currency CreateCurrency(User user, string name)
        {
            Logger.Info($"Creating new currency with name: {name}");
            // Do not use CurrencyType.Credit! Eco expects only one Credit currency per user which is automatically created for every user.
            // Creating a second credit currency for a user will effectively corrupt the world.
            return CurrencyManager.AddCurrency(user, name, Eco.Shared.Items.CurrencyType.Backed); 
        }
        #endregion
        public static Deed FindDeed(Vector2i pos)
        {
            if (!PropertyManager.Initializer.Initialized)
                throw new Exception("PropertyManager is not initialized yet!");
            var deed = PropertyManager.GetDeedWorldPos(pos);
            if (deed == null)
                throw new DeedMissing(pos.X, pos.Y);
            return deed;
        }

        private Task<T> RunIfOrWhenInitializedAsync<T>(PluginManager pluginManager, Func<T> action)
        {
            if (pluginManager.Initialized)
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            } else
            {
                var tcs = new TaskCompletionSource<T>();
                pluginManager.InitComplete += () =>
                {
                    try
                    {
                        T result = action();
                        tcs.SetResult(result);
                    }
                    catch (Exception ex) 
                    {
                        tcs.SetException(ex);
                    }
                };
                return tcs.Task;
            }
        }

        /// <summary>
        /// Async wrapper function for <see cref="Initializer"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="initializer">Eco initializer.</param>
        /// <param name="action">Action to run once the initializer is done.</param>
        /// <returns></returns>
        private Task<T> RunIfOrWhenInitializedAsync<T>(Initializer initializer, Func<T> action)
        {
            if (initializer.Initialized)
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }

            }
            var tcs = new TaskCompletionSource<T>();
            initializer.RunIfOrWhenInitialized(() => {
                try
                {
                    T result = action();
                    tcs.SetResult(result);
                }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        public EasyMarketStatus GetStatusObject() { return status; }
    }
}
