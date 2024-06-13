namespace ProjectCreator.Models
{
    public class CreatorEntity
    {
        public string ProjectName { get; set; }
        public string[] ModelNames { get; set; }
        public IFormFile ExcelFile { get; set; }
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string ID { get; set; }
        public string Password { get; set; }
    }
    public class TableProperty
    {
        public string PropertyName { get; set; } = "";
        public string DataType { get; set; } = "";
    }
}
