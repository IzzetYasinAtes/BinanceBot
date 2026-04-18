using System.Collections.Concurrent;
using System.Reflection;
using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace BinanceBot.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> InvalidMethodCache = new();

    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var ctx = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var v in _validators)
        {
            var result = await v.ValidateAsync(ctx, cancellationToken);
            failures.AddRange(result.Errors.Where(f => f is not null));
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        var errors = failures
            .Select(f => new ValidationError(f.PropertyName, f.ErrorMessage, f.ErrorCode, ValidationSeverity.Error))
            .ToList();

        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            // Ardalis.Result 10.x: Result<T>.Invalid(IEnumerable<ValidationError>) — non-generic Result.Invalid<T>(...) artik yok.
            var invalidMethod = InvalidMethodCache.GetOrAdd(responseType, ResolveInvalidMethod);
            return (TResponse)invalidMethod.Invoke(null, new object[] { errors })!;
        }

        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Invalid(errors);
        }

        throw new ValidationException(failures);
    }

    private static MethodInfo ResolveInvalidMethod(Type closedResultType)
    {
        // Sirasiyla denenecek imzalar (en spesifikten en geneline).
        Type[][] candidateSignatures =
        {
            new[] { typeof(IEnumerable<ValidationError>) },
            new[] { typeof(ValidationError[]) },
        };

        foreach (var signature in candidateSignatures)
        {
            var method = closedResultType.GetMethod(
                nameof(Result.Invalid),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: signature,
                modifiers: null);

            if (method is not null)
            {
                return method;
            }
        }

        throw new InvalidOperationException(
            $"Ardalis.Result type '{closedResultType.FullName}' uzerinde uygun bir Invalid(...) overload'u bulunamadi.");
    }
}
