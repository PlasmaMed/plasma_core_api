using System.Net.Mail;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public static class ValidatorExtensions
{
    public static IRuleBuilderOptions<T, string?> ValidOptionalEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(150)
            .Must(value => string.IsNullOrWhiteSpace(value) || IsValidEmail(value))
            .WithMessage("'{PropertyName}' is not a valid email address.");

    public static IRuleBuilderOptions<T, string?> ValidOptionalPhone<T>(this IRuleBuilder<T, string?> rule, int maximumLength = 20) =>
        rule.MaximumLength(maximumLength)
            .Matches(@"^[0-9+\-\s().]*$")
            .WithMessage("'{PropertyName}' contains invalid characters.");

    /// <summary>
    /// 3-50 chars: letters, digits, dot, underscore, hyphen, @.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ValidUsername<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Length(3, 50)
            .Matches(@"^[A-Za-z0-9._@-]+$")
            .WithMessage("'{PropertyName}' may only contain letters, digits and . _ @ -");

    /// <summary>
    /// At least 8 chars with an upper-case letter, a lower-case letter and a digit.
    /// </summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Length(8, 100)
            .Matches("[A-Z]").WithMessage("'{PropertyName}' must contain an upper-case letter.")
            .Matches("[a-z]").WithMessage("'{PropertyName}' must contain a lower-case letter.")
            .Matches("[0-9]").WithMessage("'{PropertyName}' must contain a digit.");

    private static bool IsValidEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value.Trim();

    /// <summary>
    /// Foreign key must reference an existing (non-deleted) record when provided.
    /// </summary>
    public static IRuleBuilderOptions<T, Guid?> MustExistIfProvided<T, TEntity>(
        this IRuleBuilder<T, Guid?> rule,
        IRepository<TEntity> repository,
        string entityName) where TEntity : BaseEntity =>
        rule.Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("'{PropertyName}' must be a valid id.")
            .MustAsync(async (id, ct) => id is null || await repository.ExistsAsync(e => e.Id == id.Value, ct))
            .WithMessage($"{entityName} does not exist.");
}
