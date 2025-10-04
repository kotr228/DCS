namespace DocControlUI.Models
{
    public class FolderObject
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public bool IsFile { get; set; }
        public int? ParentId { get; set; }
    }
}
