namespace ZipProcessor.Admin.Models;

public class ServiceDefinitionOLD
{
    public string Name { get; set; } = default!;
  
    public string Type { get; set; } = "Docker";
  
    public string? ExePath { get; set; }
    public string? DefaultArgs { get; set; }
    public int? DefaultPort { get; set; }
  
    public string? Image { get; set; }

  
    public List<InputDefinition>? Inputs { get; set; }
}



public class ServiceDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Exe";
    public string? ExePath { get; set; }
    public string? Image { get; set; }
    public int? DefaultPort { get; set; }
    public string? DefaultArgs { get; set; }
    public string? InputsJson { get; set; }

    public List<ServiceInputDefinition> Inputs { get; set; } = new();
}




public class ServiceInputDefinition
{
    public string Key { get; set; } = "";
    public string? Label { get; set; }
    public string? Type { get; set; }
    public string? Default { get; set; }
    public string? Placeholder { get; set; }
}
