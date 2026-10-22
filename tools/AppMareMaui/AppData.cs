namespace AppMare.Converters;

public class SharedVariable
{
    public string Type { get; set; }
    public object Value { get; set; }
}

public static class AppData
{
    public static Dictionary<string, SharedVariable> SharedVariables = new();
}
