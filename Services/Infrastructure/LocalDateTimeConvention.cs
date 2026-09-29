using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Options;

namespace Services.Infrastructure
{
    public class LocalDateTimeSerializationConvention : ISerializationOptionsConvention
    {
        public IBsonSerializationOptions GetSerializationOptions(MemberInfo memberInfo)
        {
            if (memberInfo is PropertyInfo && ((memberInfo as PropertyInfo).PropertyType == typeof(DateTime) || (memberInfo as PropertyInfo).PropertyType == typeof(DateTime?)))
            {
                return new DateTimeSerializationOptions(DateTimeKind.Local);
            }

            return null;
        }
    }
}
