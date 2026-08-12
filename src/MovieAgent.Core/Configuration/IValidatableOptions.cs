using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MovieAgent.Core.Configuration;

/// <summary>
/// Options that can check themselves. Keeps validation next to the settings it describes
/// instead of scattered through DI registration code.
/// </summary>
public interface IValidatableOptions
{
    /// <summary>Returns one message per problem; an empty sequence means valid.</summary>
    IEnumerable<string> GetValidationErrors();
}

/// <summary>Adapts <see cref="IValidatableOptions"/> to the options validation pipeline.</summary>
public sealed class ValidatableOptionsValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : class, IValidatableOptions
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var errors = options.GetValidationErrors().ToArray();
        return errors.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public static class ValidatableOptionsBuilderExtensions
{
    /// <summary>
    /// Validates the bound options via <see cref="IValidatableOptions.GetValidationErrors"/>,
    /// failing at host start rather than at first use.
    /// </summary>
    public static OptionsBuilder<TOptions> ValidateSelf<TOptions>(this OptionsBuilder<TOptions> builder)
        where TOptions : class, IValidatableOptions
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>, ValidatableOptionsValidator<TOptions>>();
        return builder.ValidateOnStart();
    }
}
