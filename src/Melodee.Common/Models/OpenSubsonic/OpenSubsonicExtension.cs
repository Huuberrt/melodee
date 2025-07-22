using Melodee.Common.Extensions;

namespace Melodee.Common.Models.OpenSubsonic;

public record OpenSubsonicExtension(string Name, int[] Versions) : IOpenSubsonicToXml
{
    public virtual string ToXml(string? nodeName = null)
    {
        return $"name=\"{Name.ToSafeXmlString()}\" versions=\"{ Versions.ToCsv() } \"/>";
    }
}
