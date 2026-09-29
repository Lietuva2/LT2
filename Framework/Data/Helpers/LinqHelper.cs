using System;
using System.Linq;
using System.Linq.Expressions;
using LinqKit;

namespace Framework.Data.Helpers
{
    /// <summary>
    /// The linq helper class.
    /// </summary>
    public static class LinqHelper
    {
        /// <summary>
        /// Applies the appropriate LIKE condition to a sequence of values, based on the * symbol in begining of the filter.
        /// </summary>
        /// <typeparam name="T">The type of the entity.</typeparam>
        /// <param name="queryableData">The queryable data.</param>
        /// <param name="property">The property.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>The queryable data with the like expression added.</returns>
        public static IQueryable<T> Like<T>(this IQueryable<T> queryableData, Expression<Func<T, string>> property, string filter)
        {
            var lambda = LikeExpression(property, filter);
            return queryableData.Where(lambda);
        }

        /// <summary>
        /// Gets the appropriate LIKE expression, based on the * symbol in begining of the filter.
        /// </summary>
        /// <typeparam name="T">The type of the entity.</typeparam>
        /// <param name="property">The property.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>
        /// The like expression.
        /// </returns>
        public static Expression<Func<T, bool>> LikeExpression<T>(Expression<Func<T, string>> property, string filter)
        {
            var arg = Expression.Parameter(typeof(T), "t");
            var prop = Expression.Invoke(property, arg);
            var exp = GetLikeExpression(property, filter, arg, prop);
            return Expression.Lambda<Func<T, bool>>(exp, arg);
        }

        /// <summary>
        /// Applies the appropriate LIKE condition to a sequence of values, based on the * symbol in begining of the filter.
        /// </summary>
        /// <typeparam name="T">The type of the entity.</typeparam>
        /// <param name="property">The property.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>
        /// The queryable data with the like expression added.
        /// </returns>
        public static Expression<Func<T, bool>> StartLikeExpression<T>(Expression<Func<T, string>> property, string filter)
        {
            var predicate = PredicateBuilder.False<T>();
            var lambda = LikeExpression(property, filter);
            return predicate.Or(lambda);
        }

        /// <summary>
        /// Starts the expression.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Expression<Func<T, bool>> StartOrExpression<T>()
        {
            return PredicateBuilder.False<T>();
        }

        /// <summary>
        /// Starts the expression.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Expression<Func<T, bool>> StartAndExpression<T>()
        {
            return PredicateBuilder.True<T>();
        }

        /// <summary>
        /// Applies the appropriate OR LIKE condition to a sequence of values, based on the * symbol in begining of the filter.
        /// </summary>
        /// <typeparam name="T">The type of the entity.</typeparam>
        /// <param name="predicate">The predicate.</param>
        /// <param name="property">The property.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>
        /// The queryable data with the like expression added.
        /// </returns>
        public static Expression<Func<T, bool>> OrLikeExpression<T>(this Expression<Func<T, bool>> predicate, Expression<Func<T, string>> property, string filter)
        {
            var lambda = LikeExpression(property, filter);
            return predicate.Or(lambda);
        }

        public static Expression<Func<T, bool>> OrExpression<T>(this Expression<Func<T, bool>> predicate, Expression<Func<T, bool>> lambda)
        {
            return predicate.Or(lambda);
        }

        public static Expression<Func<T, bool>> AndExpression<T>(this Expression<Func<T, bool>> predicate, Expression<Func<T, bool>> lambda)
        {
            return predicate.And(lambda);
        }

        /// <summary>
        /// Gets the like expression.
        /// </summary>
        /// <typeparam name="T">The type of the entity.</typeparam>
        /// <param name="property">The property.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="arg">The arg.</param>
        /// <param name="prop">The prop.</param>
        /// <returns>The like expression.</returns>
        private static Expression GetLikeExpression<T>(Expression<Func<T, string>> property, string filter, ParameterExpression arg, InvocationExpression prop)
        {
            Expression exp = null;

            if (filter.StartsWith("*"))
            {
                exp = Expression.Call(prop, "Contains", null, Expression.Constant(filter.Substring(1)));
            }
            else
            {
                exp = Expression.Call(prop, "StartsWith", null, Expression.Constant(filter));
            }

            return exp;
        }
    }
}
