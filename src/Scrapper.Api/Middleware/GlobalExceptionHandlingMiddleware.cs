using System.Net;
using Microsoft.AspNetCore.Mvc;
using Scrapper.Models.Common;

namespace Scrapper.Api.Middleware;

public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            ValidationException => (HttpStatusCode.BadRequest, "Validation failed"),
            FluentValidation.ValidationException => (HttpStatusCode.BadRequest, "Validation failed"),
            SsrfViolationException => (HttpStatusCode.BadRequest, "Blocked URL"),
            ScrapeException => (HttpStatusCode.BadRequest, "Unable to complete the scrape"),
            OperationCanceledException => (HttpStatusCode.RequestTimeout, "The request timed out"),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        var isServerError = statusCode == HttpStatusCode.InternalServerError;

        logger.LogError(exception, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = isServerError && !environment.IsDevelopment()
                ? "An unexpected error occurred. Please try again later."
                : exception.Message,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
