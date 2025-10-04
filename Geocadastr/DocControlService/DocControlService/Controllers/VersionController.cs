using Microsoft.AspNetCore.Mvc;
using DocControlService.Services;

namespace DocControlService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VersionController : ControllerBase
    {
        private readonly VersionControlService _vcs;

        public VersionController()
        {
            // тут можна винести в appsettings.json
            _vcs = new VersionControlService(@"G:\ФОП ТКАЧЕНКО 14.08.2025\РОБОТА");
        }

        [HttpPost("commit")]
        public IActionResult Commit([FromQuery] string? message)
        {
            _vcs.CommitAll(message ?? "Manual commit");
            return Ok(new { status = "commit done", message });
        }

        [HttpGet("log")]
        public IActionResult Log()
        {
            _vcs.ShowLog();
            return Ok(new { status = "log printed to console" });
        }
    }
}
