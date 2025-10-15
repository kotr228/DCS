using System.Collections.Generic;

namespace GrayCat.Shared.Models
{
    public class ProjectModel
    {
        public string ProjectName { get; set; } = "Untitled";
        public string Author { get; set; } = "Unknown";
        public List<BlockModel> Blocks { get; set; } = new List<BlockModel>();
    }
}
