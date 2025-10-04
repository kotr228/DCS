using Microsoft.AspNetCore.Mvc;

namespace DocControlService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { status = "Service is running" });
        }
    }
}
