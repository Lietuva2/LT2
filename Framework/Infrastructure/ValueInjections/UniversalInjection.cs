using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Framework.Infrastructure.Storage;
using Omu.ValueInjecter;

namespace Framework.Infrastructure.ValueInjections
{
    public class UniversalInjection : Omu.ValueInjecter.Injections.LoopInjection
    {
        protected override bool MatchTypes(Type sourceType, Type targetType)
        {
            if(sourceType == targetType)
            {
                return true;
            }

            if(sourceType == typeof(string) && targetType == typeof(MongoObjectId))
            {
                return false;
            }

            if (sourceType == typeof(MongoObjectId) && targetType == typeof(string))
            {
                return true;
            }

            var baseSourceType = Nullable.GetUnderlyingType(sourceType);
            var baseTargetType = Nullable.GetUnderlyingType(targetType);
            if (baseSourceType != null || baseTargetType != null)
            {
                if (baseSourceType == baseTargetType)
                {
                    return true;
                }

                if (baseSourceType == targetType)
                {
                    return true;
                }

                if (baseTargetType == sourceType)
                {
                    return true;
                }
            }

            return false;
        }

        protected override void SetValue(object source, object target, PropertyInfo sp, PropertyInfo tp)
        {
            object obj = sp.GetValue(source, (object[])null);
            if (sp.PropertyType == typeof(MongoObjectId) && tp.PropertyType == typeof(string) && obj != null)
            {
                tp.SetValue(target, obj.ToString(), null);
                return;
            }

            //if (this.SourcePropType == typeof(DateTime) && this.TargetPropType == typeof(DateTime))
            //{
            //    target = ((DateTime)sourcePropertyValue);
            //}

            //return base.SetValue(sourcePropertyValue);
            base.SetValue(source, target, sp, tp);
        }

        //protected override object SetValue(object sourcePropertyValue)
        //{
        //    if (this.SourcePropType == typeof(MongoObjectId) && this.TargetPropType == typeof(string) && sourcePropertyValue != null)
        //    {
        //        return sourcePropertyValue.ToString();
        //    }

        //    if (this.SourcePropType == typeof(DateTime) && this.TargetPropType == typeof(DateTime))
        //    {
        //        return ((DateTime)sourcePropertyValue);
        //    }

        //    return base.SetValue(sourcePropertyValue);
        //}
    }
}
