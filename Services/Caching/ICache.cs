using System;
using System.Collections.Generic;

namespace Services.Caching
{
    public interface ICache
    {
        // Methods
        bool GetItem(string key, out object value);
        void InvalidateItem(string key);
        void InvalidateSets(IEnumerable<string> entitySets);
        void PutItem(string key, object value, IEnumerable<string> dependentEntitySets, TimeSpan slidingExpiration, DateTime absoluteExpiration);
    }
}
