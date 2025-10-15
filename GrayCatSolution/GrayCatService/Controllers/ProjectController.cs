using GrayCat.Service.Services;
using GrayCat.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrayCat.Service.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController : ControllerBase
    {
        private readonly ProjectService _projectService;

        public ProjectController(ProjectService projectService)
        {
            _projectService = projectService;
        }

        [HttpPost("generate")]
        public IActionResult GenerateProject([FromBody] ProjectGenerationRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new GenerationResult { Success = false, Message = "Invalid request model." });
            }

            var result = _projectService.GenerateProject(request);

            if (result.Success)
            {
                return Ok(result);
            }

            // Повертаємо 400 Bad Request або 500 Internal Server Error залежно від типу помилки
            return BadRequest(result);
        }
    }
}