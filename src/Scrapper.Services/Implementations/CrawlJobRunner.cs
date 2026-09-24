using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// See <see cref="ICrawlJobRunner"/>. A crawl job outlives the HTTP request that started it, so
/// each run gets its own <see cref="IServiceScopeFactory"/> scope (for the scoped
/// <see cref="IPagedCrawlService"/> and its dependencies) and its own <see cref="CancellationTokenSource"/>,
/// tracked here only while the job's background task is actually executing.
/// </summary>
public class CrawlJobRunner(
    IServiceScopeFactory scopeFactory,
    ICrawlStateStore stateStore,
    ILogger<CrawlJobRunner> logger) : ICrawlJobRunner
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public async Task<string> StartAsync(string? jobId, PagedCrawlConfigDto config, bool fresh, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(jobId) ? Guid.NewGuid().ToString("n") : jobId;

        if (fresh)
        {
            await stateStore.ResetAsync(id, cancellationToken);
        }

        var existing = await stateStore.LoadAsync(id, cancellationToken);
        if (existing is null || fresh)
        {
            var state = new CrawlJobStateDto
            {
                JobId = id,
                Config = config,
                TotalPages = config.TotalPages,
                FieldNames = config.Fields.Select(f => f.Name).ToList(),
                Status = CrawlJobStatus.NotStarted,
            };
            await stateStore.SaveAsync(state, cancellationToken);
        }

        Run(id, (engine, ct) => engine.RunAsync(id, ct));
        return id;
    }

    public Task ResumeAsync(string jobId, CancellationToken cancellationToken)
    {
        Run(jobId, (engine, ct) => engine.RunAsync(jobId, ct));
        return Task.CompletedTask;
    }

    public Task RetryFailedAsync(string jobId, CancellationToken cancellationToken)
    {
        Run(jobId, (engine, ct) => engine.RetryFailedAsync(jobId, ct));
        return Task.CompletedTask;
    }

    public Task<CrawlJobStateDto?> GetStateAsync(string jobId, CancellationToken cancellationToken) =>
        stateStore.LoadAsync(jobId, cancellationToken);

    public void StopAll()
    {
        foreach (var cts in _running.Values)
        {
            cts.Cancel();
        }
    }

    private void Run(string jobId, Func<IPagedCrawlService, CancellationToken, Task> action)
    {
        var cts = new CancellationTokenSource();
        if (!_running.TryAdd(jobId, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"Crawl job '{jobId}' is already running.");
        }

        // Deliberately NOT the caller's request CancellationToken — that's tied to the HTTP
        // request/connection that started or resumed the job, and would cancel this background
        // work the moment the controller returns. This token is owned by the runner itself and
        // only cancelled by StopAll() (app shutdown) or a future explicit stop.
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IPagedCrawlService>();

            try
            {
                await action(engine, cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Crawl job {JobId} stopped unexpectedly", jobId);
            }
            finally
            {
                _running.TryRemove(jobId, out _);
                cts.Dispose();
            }
        });
    }
}
