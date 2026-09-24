using Microsoft.AspNetCore.Mvc;
using Scrapper.Models.Common;

namespace Scrapper.Api.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected ActionResult<ApiResponse<T>> Ok<T>(T data, string? message = null) =>
        base.Ok(ApiResponse<T>.Ok(data, message));

    protected ActionResult<ApiResponse<T>> Fail<T>(string message, int statusCode = 400) =>
        StatusCode(statusCode, ApiResponse<T>.Fail(message));
}
