using Microsoft.AspNetCore.Mvc;
using DocControlService.Data;

namespace DocControlService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DirectoryController : ControllerBase
    {
        private readonly DirectoryRepository _repository;

        public DirectoryController(DirectoryRepository repository)
        {
            _repository = repository;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            var dirs = _repository.GetAllDirectories();
            return Ok(dirs);
        }

        [HttpPost]
        public IActionResult Add([FromBody] DirectoryDto dto)
        {
            _repository.AddDirectory(dto.Name, dto.Browse);
            return Ok(new { message = "Directory added" });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            _repository.DeleteDirectory(id);
            return Ok(new { message = $"Directory {id} deleted" });
        }
    }

    public class DirectoryDto
    {
        public string Name { get; set; }
        public string Browse { get; set; }
    }
}
