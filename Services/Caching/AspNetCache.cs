using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Caching;

namespace Services.Caching
{
    public class AspNetCache : ICache
{
    // Fields
    private const string DependentEntitySetPrefix = "dependent_entity_set_";
    private HttpContext httpContext;

    // Methods
    public AspNetCache() : this(null)
    {
    }

    public AspNetCache(HttpContext httpContext)
    {
        this.httpContext = httpContext;
    }

    private void EnsureEntryExists(string key)
    {
        Cache cache = this.HttpCache;
        if (cache.Get(key) == null)
        {
            try
            {
                cache.Insert(key, key, null, Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration);
            }
            catch (Exception)
            {
            }
        }
    }

    private static string GetCacheKey(string query)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(query);
        return Convert.ToBase64String(MD5.Create().ComputeHash(bytes));
    }

    public bool GetItem(string key, out object value)
    {
        key = GetCacheKey(key);
        value = this.HttpCache.Get(key);
        return (value != null);
    }

    public void InvalidateItem(string key)
    {
        key = GetCacheKey(key);
        this.HttpCache.Remove(key);
    }

    public void InvalidateSets(IEnumerable<string> entitySets)
    {
        foreach (string entitySet in entitySets)
        {
            this.HttpCache.Remove("dependent_entity_set_" + entitySet);
        }
    }

    public void PutItem(string key, object value, IEnumerable<string> dependentEntitySets, TimeSpan slidingExpiration, DateTime absoluteExpiration)
    {
        key = GetCacheKey(key);
        Cache cache = this.HttpCache;
        foreach (string entitySet in dependentEntitySets)
        {
            this.EnsureEntryExists("dependent_entity_set_" + entitySet);
        }
        try
        {
            CacheDependency cd = new CacheDependency(new string[0], dependentEntitySets.Select<string, string>(delegate (string c) {
                return ("dependent_entity_set_" + c);
            }).ToArray<string>());
            cache.Insert(key, value, cd, absoluteExpiration, slidingExpiration, CacheItemPriority.Normal, null);
        }
        catch (Exception)
        {
        }
    }

    // Properties
    private Cache HttpCache
    {
        get
        {
            if (this.httpContext != null)
            {
                return this.httpContext.Cache;
            }
            HttpContext context = HttpContext.Current;
            if (context == null)
            {
                throw new InvalidOperationException("Unable to determine HTTP context.");
            }
            return context.Cache;
        }
    }
}
}
