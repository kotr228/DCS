using Microsoft.AspNetCore.Mvc;
using System;
using System.Diagnostics;

namespace DocControlService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShareController : ControllerBase
    {
        /// <summary>
        /// Відкрити доступ до папки у локальній мережі
        /// </summary>
        [HttpPost("open")]
        public IActionResult OpenShare([FromBody] ShareRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.ShareName))
                    return BadRequest(new { error = "Path і ShareName обов’язкові" });

                var psi = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = $"share {request.ShareName}=\"{request.Path}\" /grant:Everyone,full",
                    Verb = "runas", // потрібні права адміна
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                    return Ok(new { message = "✅ Шар створено", output });

                return StatusCode(500, new { message = "❌ Помилка при створенні шару", output, error });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Закрити доступ до папки
        /// </summary>
        [HttpPost("close")]
        public IActionResult CloseShare([FromBody] ShareRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ShareName))
                    return BadRequest(new { error = "ShareName обов’язкове" });

                var psi = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = $"share {request.ShareName} /delete",
                    Verb = "runas",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                    return Ok(new { message = "✅ Шар видалено", output });

                return StatusCode(500, new { message = "❌ Помилка при видаленні шару", output, error });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }

    public class ShareRequest
    {
        public string Path { get; set; }      // шлях до папки
        public string ShareName { get; set; } // ім’я шару (як буде видно у мережі)
    }
}
