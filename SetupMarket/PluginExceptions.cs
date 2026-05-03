using Eco.Shared.Math;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using static Eco.Shared.Utils.LimitMapper;

namespace EasyMarket
{
    public class FailedToCreateCurrency : Exception 
    {
        public FailedToCreateCurrency(string name, string reason) : base(
            $"Failed to create currency {name}." +
            $"Reason: {reason}"
        ) { }
    }

    public class CurrencyMissing : Exception
    {
        public CurrencyMissing(string name) : base(
            $"Expected currency {name} to exist but couldn't find it."
        ) { }
    }

    public class UsernameConflict : Exception
    {
        public UsernameConflict(string name) : base(
            $"Found pre-existing player with the same name: {name}. " +
            $"Please change this mods {nameof(EasyMarketConfig.OwnerName)} property in the configs to something different."
        ) { }
    }

    public class MissingRequiredConfig : Exception
    {
        public MissingRequiredConfig(string fieldName) : base(
            $"Config value {fieldName} has not been set. This mod will not work till you update that in the configs."
        ) { }
    }

    public class DeedMissing : Exception
    {
        public DeedMissing(int x, int z) : base(
            $"No deed exists here. X: {x}, Z: {z}"
        ) { }
    }

    public class NeedsAccountEx : Exception
    {
        public NeedsAccountEx(string name) : base(
            $"The EasyMarket account with name {name} has not been created. Run `/easymarket createAccount` to create it."
        ) { }
    }

    public class NeedsCurrencyEx : Exception
    {
        public NeedsCurrencyEx(string name) : base(
            $"The currency {name} has not been created. Run '/easymarket createCurrency` to create it."
        ) { }
    }
}
