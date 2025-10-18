namespace GrayCat.Service.Controllers;

using Microsoft.AspNetCore.Mvc;
using GrayCat.Core.Interfaces;
using GrayCat.Core.Models.Generation;

[ApiController]
[Route("api/[controller]")]
public class GenerationController : ControllerBase
{
    private readonly ILogger<GenerationController> _logger;
    private readonly IGenerationService _generationService;

    public GenerationController(
        ILogger<GenerationController> logger,
        IGenerationService generationService)
    {
        _logger = logger;
        _generationService = generationService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateProject([FromBody] GenerationRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new GenerationResult
            {
                Success = false,
                Message = "Invalid request model"
            });
        }

        var result = await _generationService.GenerateProjectAsync(request);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateProject([FromBody] GenerationRequest request)
    {
        var result = await _generationService.ValidateBeforeGenerationAsync(request);
        return Ok(result);
    }

    [HttpGet("status/{requestId}")]
    public async Task<IActionResult> GetStatus(string requestId)
    {
        var status = await _generationService.GetGenerationStatusAsync(requestId);
        return Ok(new { requestId, status });
    }
}