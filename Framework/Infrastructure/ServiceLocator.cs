using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Infrastructure
{
    public class ServiceLocator
    {
        private static Func<Type, object> resolver;

        public static Func<Type, object> Resolver
        {
            set
            {
                resolver = value;
            }
        }

        public static T Resolve<T>()
        {
            return (T)resolver(typeof(T));
        }

        public static object Resolve(Type type)
        {
            return resolver(type);
        }
    }
}
