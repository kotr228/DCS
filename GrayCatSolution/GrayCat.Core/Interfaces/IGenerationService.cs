namespace GrayCat.Core.Interfaces;

using GrayCat.Core.Models.Generation;

public interface IGenerationService
{
    Task<GenerationResult> GenerateProjectAsync(GenerationRequest request);
    Task<GenerationResult> ValidateBeforeGenerationAsync(GenerationRequest request);
    Task<string> GetGenerationStatusAsync(string requestId);
}