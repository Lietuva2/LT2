using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;

namespace Services.Caching
{
    public class DefaultCache : ICache
    {
        private readonly MemoryCache _cache;

        public DefaultCache()
            : this(MemoryCache.Default)
        {
        }

        public DefaultCache(MemoryCache cache)
        {
            _cache = cache;
        }

        public bool GetItem(string key, out object value)
        {
            value = _cache.Get(key);
            return (value != null);
        }

        public void InvalidateItem(string key)
        {
            _cache.Remove(key);
        }

        public void InvalidateSets(IEnumerable<string> entitySets)
        {
            foreach (var key in entitySets)
            {
                InvalidateItem(key);
            }
        }

        public void PutItem(string key, object value, IEnumerable<string> dependentEntitySets, TimeSpan slidingExpiration,
                            DateTime absoluteExpiration)
        {
            _cache.Set(key, value, new CacheItemPolicy { SlidingExpiration = slidingExpiration });
        }
    }
}
