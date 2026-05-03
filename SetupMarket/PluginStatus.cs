using Eco.Gameplay.Players;
using System.Runtime.Versioning;

namespace EasyMarket
{

    [SupportedOSPlatform("windows7.0")]
    public abstract record EasyMarketStatus
    {
        private EasyMarketStatus() { }

        public static string PrintStatus(EasyMarketStatus status)
        {
            switch (status)
            {
                case Error x:
                    return $"Error: {x.msg}";
                case Init:
                    return "Waiting to start...";
                case NeedsAccount:
                    return "Run the '/easymarket createAccount' command to create the EasyMarket account.";
                case NeedsCurrency:
                    return "Run the '/easymarket createCurrency` command to create the currency.";
                case Running x:
                    return $"Running. Account: {x.user.Name}";
                case Stopped:
                    return "Stopped";
                default:
                    throw new NotImplementedException(); // never because EasyMarketStatus is a private constructor
            }
        }


        public sealed record Error : EasyMarketStatus
        {
            public string msg;

            public Error(string msg)
            {
                this.msg = msg;
                Logger.Info($"Error state: {msg}");
            }
        }
        public sealed record Init : EasyMarketStatus;
        public sealed record NeedsAccount: EasyMarketStatus;
        public sealed record NeedsCurrency(User user): EasyMarketStatus;
        public sealed record Running(User user) : EasyMarketStatus;
        public sealed record Stopped : EasyMarketStatus;


        /// <summary>
        /// Gets the EasyMarket user.
        /// </summary>
        /// <param name="status">Status of the plugin.</param>
        /// <returns></returns>
        /// <exception cref="Exception">Throws if in an invalid state to get a user.</exception>
        /// <exception cref="NotImplementedException">Should never throw this. If it did, a new state was added to the mod and needs added to this method.</exception>
        public static User GetUser(EasyMarketStatus status)
        {
            switch(status)
            {
                case Error err:
                    throw new Exception($"This mod is not working because of an error. Check server logs for details.");
                case NeedsAccount:
                    throw new NeedsAccountEx(EasyMarketConfig.Obj.OwnerName);
                case NeedsCurrency:
                    throw new NeedsCurrencyEx(EasyMarketConfig.Obj.CurrencyName);
                case Init:
                    throw new Exception($"This mod has not been started. Check server logs for errors.");
                case Running x:
                    return x.user;
                case Stopped:
                    throw new Exception($"This mod has been stopped. Check server logs for errors.");
                default:
                    throw new NotImplementedException($"This mod won't work. It's in an unknown state."); //never
        }
        }
    }

}
