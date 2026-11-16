using System.Linq.Expressions;
using System.Reflection;

namespace CorePlasma.BAL.Services.Common;

/// <summary>
/// Declares that a code/number property is filled from a document sequence when left empty on create.
/// </summary>
/// <param name="SequenceCode">Row in DocumentSequences.</param>
/// <param name="CodeProperty">e.g. x => x.CustomerCode</param>
/// <param name="DateSelector">Business date for date-based numbering (vouchers); null = today.</param>
public sealed record AutoCodeOptions<TEntity>(
    string SequenceCode,
    Expression<Func<TEntity, string>> CodeProperty,
    Func<TEntity, DateTime?>? DateSelector = null)
{
    private readonly PropertyInfo _property =
        (CodeProperty.Body as MemberExpression)?.Member as PropertyInfo
        ?? throw new ArgumentException("CodeProperty must be a simple property access, e.g. x => x.CustomerCode.");

    public string? GetValue(TEntity entity) => (string?)_property.GetValue(entity);

    public void SetValue(TEntity entity, string value) => _property.SetValue(entity, value);
}
