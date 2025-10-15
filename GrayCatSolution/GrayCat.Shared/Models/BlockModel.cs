using System;

namespace GrayCat.Shared.Models
{
    public class BlockModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Type { get; set; } = "Text"; // e.g., "Header", "Text", "Image"
        public string Content { get; set; } = "Some default content";
        public double PositionX { get; set; }
        public double PositionY { get; set; }
    }
}