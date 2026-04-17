using Ardalis.Result;
using Microsoft.AspNetCore.Http;

namespace BinanceBot.Api.Infrastructure;

public static class ResultExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(result.Value),
            ResultStatus.NotFound => Results.NotFound(new { errors = result.Errors }),
            ResultStatus.Invalid => Results.BadRequest(new
            {
                errors = result.ValidationErrors.Select(v => new
                {
                    identifier = v.Identifier,
                    error = v.ErrorMessage,
                    severity = v.Severity.ToString(),
                }),
            }),
            ResultStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.StatusCode(StatusCodes.Status401Unauthorized),
            ResultStatus.Conflict => Results.Conflict(new { errors = result.Errors }),
            ResultStatus.CriticalError => Results.Problem(
                detail: string.Join(";", result.Errors),
                statusCode: StatusCodes.Status500InternalServerError),
            ResultStatus.Error => Results.Problem(
                detail: string.Join(";", result.Errors),
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
