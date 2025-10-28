namespace ZipProcessing.WebApi.Models
{
    public class JobRequest
    {
        public string RequestId { get; set; } = default!;
        public string ZipPath { get; set; } = default!;
    }

}
