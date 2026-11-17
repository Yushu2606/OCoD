using System.Reflection;

namespace OCoD.Protocol;

public static class ComponentVersion
{
    public static string Of(Type componentType)
    {
        var version = componentType.Assembly.GetName().Version?.ToString(3);
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }
}