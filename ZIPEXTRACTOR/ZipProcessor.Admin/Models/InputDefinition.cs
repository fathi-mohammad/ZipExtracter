namespace ZipProcessor.Admin.Models;



public class InputDefinition
{
    
    public string Key { get; set; } = default!;

    public string Label { get; set; } = default!;

   
    public string Type { get; set; } = "string";

   
    public string? Placeholder { get; set; }

   
    public string? Default { get; set; }
}