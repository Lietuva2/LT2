using System;
using Data.ViewModels.Account;

namespace Services.Exceptions
{
    public class AdditionalUniqueInfoRequiredException : Exception
    {
        public AdditionalUniqueInfoModel AdditionalInfo { get; set; }

        public AdditionalUniqueInfoRequiredException(AdditionalUniqueInfoModel model)
            : base("Additional unique info required")
        {
            AdditionalInfo = model;
        }
    }
}
