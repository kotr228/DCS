namespace GrayCat.Service.Services;

using System.Collections.Concurrent;
using GrayCat.Core.Generation;
using GrayCat.Core.Interfaces;
using GrayCat.Core.Models.Generation;

/// <summary>HTTP-facing wrapper around <see cref="ProjectGenerator"/> that also tracks request status.</summary>
public class ProjectGenerationService : IGenerationService
{
    private readonly ILogger<ProjectGenerationService> _logger;
    private readonly ConcurrentDictionary<string, GenerationStatus> _statuses = new();

    public ProjectGenerationService(ILogger<ProjectGenerationService> logger)
    {
        _logger = logger;
    }

    public async Task<GenerationResult> GenerateProjectAsync(GenerationRequest request)
    {
        var requestId = Guid.NewGuid().ToString("N");
        _statuses[requestId] = GenerationStatus.InProgress;
        TrimStatuses();

        _logger.LogInformation("Generating {Type} project '{Name}' in {Path} (overwrite: {Overwrite})",
            request.Type, request.ProjectName, request.OutputPath, request.Overwrite);

        var result = await ProjectGenerator.GenerateAsync(request);
        _statuses[requestId] = result.Success ? GenerationStatus.Completed : GenerationStatus.Failed;

        if (result.Success)
            _logger.LogInformation("Generated {Files} files in {Duration} ms at {Path}", result.FilesCreated, result.DurationMs, result.ProjectPath);
        else
            _logger.LogWarning("Generation failed: {Message}", result.Message);

        result.RequestId = requestId;
        return result;
    }

    public Task<GenerationResult> ValidateBeforeGenerationAsync(GenerationRequest request)
    {
        var errors = GenerationValidator.Validate(request);
        return Task.FromResult(new GenerationResult
        {
            Success = errors.Count == 0,
            Errors = errors,
            Message = errors.Count == 0 ? "Validation passed" : string.Join("; ", errors)
        });
    }

    public Task<string> GetGenerationStatusAsync(string requestId) =>
        Task.FromResult(_statuses.TryGetValue(requestId, out var status) ? status.ToString() : "Unknown");

    private void TrimStatuses()
    {
        // Keep memory bounded on a long-running service.
        if (_statuses.Count <= 500) return;
        foreach (var key in _statuses.Keys.Take(_statuses.Count - 500))
            _statuses.TryRemove(key, out _);
    }

    private enum GenerationStatus
    {
        InProgress,
        Completed,
        Failed
    }
}
