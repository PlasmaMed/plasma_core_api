using System.Linq.Expressions;
using System.Reflection;
using CorePlasma.Common.Enums;

namespace CorePlasma.Common.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate) =>
        condition ? query.Where(predicate) : query;

    /// <summary>
    /// Sorts by a property name taken from the request. Only names in <paramref name="allowedProperties"/>
    /// are accepted; anything else falls back to <paramref name="defaultProperty"/>.
    /// </summary>
    public static IQueryable<T> ApplySorting<T>(
        this IQueryable<T> query,
        string? sortBy,
        SortDirection direction,
        IReadOnlyCollection<string> allowedProperties,
        string defaultProperty,
        SortDirection defaultDirection = SortDirection.Asc)
    {
        var requested = allowedProperties.FirstOrDefault(p => string.Equals(p, sortBy, StringComparison.OrdinalIgnoreCase));
        var propertyName = requested ?? defaultProperty;
        if (requested is null)
        {
            direction = defaultDirection;
        }

        var property = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? throw new InvalidOperationException($"Property '{propertyName}' does not exist on {typeof(T).Name}.");

        var parameter = Expression.Parameter(typeof(T), "x");
        var body = Expression.Property(parameter, property);
        var lambda = Expression.Lambda(body, parameter);

        var methodName = direction == SortDirection.Desc ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        var call = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(T), property.PropertyType],
            query.Expression,
            Expression.Quote(lambda));

        return query.Provider.CreateQuery<T>(call);
    }
}
