using System.Linq.Expressions;
using CorePlasma.Common.Helpers;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services.Common;

internal static class DatabaseSearch
{
    private const string SqlServerContainsCollation = "Latin1_General_100_CI_AI_SC";
    private const string SqlServerExactCollation = "Latin1_General_100_CI_AS_SC";

    public static IQueryable<T> WhereContainsAny<T>(this IQueryable<T> query, bool isSqlServer, string? term,
        params Expression<Func<T, string?>>[] fields) =>
        term is null ? query : query.Where(MatchAny(isSqlServer, Pattern(term, false, isSqlServer), false, fields));

    public static IQueryable<T> WhereContainsAnyOr<T>(this IQueryable<T> query, bool isSqlServer, string? term,
        Expression<Func<T, bool>> additional, params Expression<Func<T, string?>>[] fields)
    {
        if (term is null) return query;
        var match = MatchAny(isSqlServer, Pattern(term, false, isSqlServer), false, fields);
        var extra = new ReplaceParameter(additional.Parameters[0], match.Parameters[0]).Visit(additional.Body)!;
        return query.Where(Expression.Lambda<Func<T, bool>>(Expression.OrElse(match.Body, extra), match.Parameters));
    }

    public static IQueryable<T> WhereExactAny<T>(this IQueryable<T> query, bool isSqlServer, string? term,
        params Expression<Func<T, string?>>[] fields) =>
        term is null ? query : query.Where(MatchAny(isSqlServer, Pattern(term, true, isSqlServer), true, fields));

    private static string Pattern(string term, bool exact, bool isSqlServer)
    {
        var pattern = exact ? SearchPattern.Exact(term) : SearchPattern.Contains(term);
        // SQL Server LIKE treats [...] as a character class; PostgreSQL treats it literally.
        return isSqlServer ? pattern.Replace("[", @"\[") : pattern;
    }

    private static Expression<Func<T, bool>> MatchAny<T>(bool isSqlServer, string pattern, bool exact,
        Expression<Func<T, string?>>[] fields)
    {
        var entity = Expression.Parameter(typeof(T), "entity");
        Expression? combined = null;
        Expression<Func<string?, bool>> match = isSqlServer
            ? exact
                ? value => EF.Functions.Like(EF.Functions.Collate(value!, SqlServerExactCollation), pattern, @"\")
                : value => EF.Functions.Like(EF.Functions.Collate(value!, SqlServerContainsCollation), pattern, @"\")
            : exact
                ? value => EF.Functions.ILike(value!, pattern)
                : value => EF.Functions.ILike(EF.Functions.Unaccent(value!), pattern);

        foreach (var field in fields)
        {
            var value = new ReplaceParameter(field.Parameters[0], entity).Visit(field.Body)!;
            var predicate = new ReplaceParameter(match.Parameters[0], value).Visit(match.Body)!;
            combined = combined is null ? predicate : Expression.OrElse(combined, predicate);
        }

        return Expression.Lambda<Func<T, bool>>(combined!, entity);
    }

    private sealed class ReplaceParameter(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}
