using System.Text.RegularExpressions;

namespace GrayCat.Service.Validation
{
    /// <summary>
    /// Handles all pre-generation validation for project requests.
    /// </summary>
    public class ProjectValidator
    {
        private readonly ILogger<ProjectValidator> _logger;
        private readonly List<string> _forbiddenPaths;

        public ProjectValidator(ILogger<ProjectValidator> logger)
        {
            _logger = logger;
            // A list of system directories where project creation is disallowed for security.
            _forbiddenPaths = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToUpperInvariant(),
                Environment.GetFolderPath(Environment.SpecialFolder.System).ToUpperInvariant(),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToUpperInvariant(),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToUpperInvariant()
            };
        }

        /// <summary>
        /// Validates the project name and output path.
        /// </summary>
        /// <returns>A tuple indicating if the request is valid and an error message if it's not.</returns>
        public (bool IsValid, string ErrorMessage) ValidateRequest(string projectName, string outputPath)
        {
            _logger.LogInformation("Starting validation for project '{ProjectName}' at '{OutputPath}'", projectName, outputPath);

            // 1. Validate Project Name
            if (string.IsNullOrWhiteSpace(projectName) || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _logger.LogWarning("Validation failed: Project name is empty or contains invalid characters.");
                return (false, "Project name is empty or contains invalid characters.");
            }

            // 2. Validate Output Path
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                _logger.LogWarning("Validation failed: Output path is empty.");
                return (false, "Output path cannot be empty.");
            }
            try
            {
                if (!Directory.Exists(Path.GetPathRoot(outputPath)))
                {
                    _logger.LogWarning("Validation failed: The drive for the output path does not exist.");
                    return (false, "The drive for the output path does not exist.");
                }
            }
            catch
            {
                _logger.LogWarning("Validation failed: Output path is malformed.");
                return (false, "Output path is malformed.");
            }

            // 3. Check for forbidden system directories
            if (_forbiddenPaths.Any(forbiddenPath => outputPath.ToUpperInvariant().StartsWith(forbiddenPath)))
            {
                _logger.LogWarning("Validation failed: Attempted to create project in a system directory.");
                return (false, "Cannot create project in a system directory for security reasons.");
            }

            // 4. Check for write permissions in the output directory
            try
            {
                // Ensure the base directory exists before trying to write to it.
                Directory.CreateDirectory(outputPath);
                string testFilePath = Path.Combine(outputPath, Guid.NewGuid().ToString() + ".tmp");
                File.WriteAllText(testFilePath, "test");
                File.Delete(testFilePath);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogError("Validation failed: No write permissions for the selected directory '{OutputPath}'.", outputPath);
                return (false, "No write permissions for the selected directory.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed: An unexpected I/O error occurred while checking permissions.");
                return (false, $"An I/O error occurred: {ex.Message}");
            }

            _logger.LogInformation("Validation successful.");
            return (true, string.Empty);
        }
    }
}