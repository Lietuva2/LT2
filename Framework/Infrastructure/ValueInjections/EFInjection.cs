using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Omu.ValueInjecter;
using Omu.ValueInjecter.Injections;

namespace Framework.Infrastructure.ValueInjections
{
    public class EfInjection : LoopInjection
    {
        protected override bool MatchTypes(Type sourceType, Type targetType)
        {
            if (sourceType.Name == "EntityReference`1")
            {
                return false;
            }

            return sourceType == targetType;
        }
    }
}
