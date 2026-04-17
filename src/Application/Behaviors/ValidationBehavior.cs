using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace BinanceBot.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
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

        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var payloadType = responseType.GetGenericArguments()[0];
            var invalidMethod = typeof(Result)
                .GetMethods()
                .Single(m => m.Name == nameof(Result.Invalid)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(List<ValidationError>))
                .MakeGenericMethod(payloadType);

            var errors = failures
                .Select(f => new ValidationError(f.PropertyName, f.ErrorMessage, f.ErrorCode, ValidationSeverity.Error))
                .ToList();

            return (TResponse)invalidMethod.Invoke(null, new object[] { errors })!;
        }

        if (responseType == typeof(Result))
        {
            var errors = failures
                .Select(f => new ValidationError(f.PropertyName, f.ErrorMessage, f.ErrorCode, ValidationSeverity.Error))
                .ToList();
            return (TResponse)(object)Result.Invalid(errors);
        }

        throw new ValidationException(failures);
    }
}
