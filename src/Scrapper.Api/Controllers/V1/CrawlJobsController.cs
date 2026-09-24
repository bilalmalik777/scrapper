using Microsoft.AspNetCore.Mvc;
using Scrapper.Models.DTOs;
using Scrapper.Services.Interfaces;

namespace Scrapper.Api.Controllers.V1;

/// <summary>
/// Long-running, checkpointed multi-page crawl jobs — distinct from
/// <see cref="ScraperController.Scrape"/>'s single synchronous scrape. Starting or resuming a
/// job returns immediately (202); the job itself runs in the background and can be polled,
/// resumed, or have its failed pages retried at any time, including after an API restart, since
/// its checkpoint lives on disk.
/// </summary>
[Route("api/v1/crawl-jobs")]
public class CrawlJobsController(ICrawlJobRunner jobRunner, ICrawlStateStore stateStore) : BaseApiController
{
    [HttpPost]
    public async Task<ActionResult<Models.Common.ApiResponse<string>>> Start(
        [FromBody] StartCrawlJobRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Config.Url) || request.Config.Fields.Count == 0)
        {
            return Fail<string>("A URL and at least one field are required.");
        }

        var jobId = await jobRunner.StartAsync(request.JobId, request.Config, request.Fresh, cancellationToken);
        return Ok(jobId, "Crawl job started.");
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<Models.Common.ApiResponse<CrawlJobStateDto>>> GetStatus(
        string jobId, CancellationToken cancellationToken)
    {
        var state = await jobRunner.GetStateAsync(jobId, cancellationToken);
        return state is null ? Fail<CrawlJobStateDto>("No crawl job found with that id.", 404) : Ok(state);
    }

    [HttpPost("{jobId}/resume")]
    public async Task<ActionResult<Models.Common.ApiResponse<string>>> Resume(
        string jobId, CancellationToken cancellationToken)
    {
        var state = await jobRunner.GetStateAsync(jobId, cancellationToken);
        if (state is null)
        {
            return Fail<string>("No crawl job found with that id.", 404);
        }

        await jobRunner.ResumeAsync(jobId, cancellationToken);
        return Ok(jobId, "Crawl job resumed.");
    }

    [HttpPost("{jobId}/retry-failed")]
    public async Task<ActionResult<Models.Common.ApiResponse<string>>> RetryFailed(
        string jobId, CancellationToken cancellationToken)
    {
        var state = await jobRunner.GetStateAsync(jobId, cancellationToken);
        if (state is null)
        {
            return Fail<string>("No crawl job found with that id.", 404);
        }

        await jobRunner.RetryFailedAsync(jobId, cancellationToken);
        return Ok(jobId, "Retrying failed pages.");
    }

    [HttpGet("{jobId}/records")]
    public async Task<ActionResult<Models.Common.ApiResponse<CrawlJobRecordsResponseDto>>> GetRecords(
        string jobId, CancellationToken cancellationToken)
    {
        var state = await jobRunner.GetStateAsync(jobId, cancellationToken);
        if (state is null)
        {
            return Fail<CrawlJobRecordsResponseDto>("No crawl job found with that id.", 404);
        }

        var records = await stateStore.LoadRecordsAsync(jobId, cancellationToken);
        return Ok(new CrawlJobRecordsResponseDto { FieldNames = state.FieldNames, Records = records });
    }
}
