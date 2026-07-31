using System.Xml.Linq;
using RetwhoConnector.Core.Exceptions;

namespace RetwhoConnector.Core.Services;

internal sealed record PosXmlFaultDetails(
    string RootName,
    string? FaultCode,
    string? FaultString,
    string? Message)
{
    public bool IsLoginRequired =>
        string.Equals(
            FaultCode?.Trim(),
            "CGIPortal.LoginRequired",
            StringComparison.OrdinalIgnoreCase);
}

internal static class PosXmlFaultInspector
{
    public static bool TryInspect(
        string xml,
        out PosXmlFaultDetails? details)
    {
        try
        {
            details = Inspect(SecureXml.Parse(xml));
            return true;
        }
        catch (PosResponseException)
        {
            details = null;
            return false;
        }
    }

    public static PosXmlFaultDetails Inspect(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        XElement root = document.Root
            ?? throw new PosResponseException(
                "POS_INVALID_XML",
                "The POS XML response has no root element.");
        return new PosXmlFaultDetails(
            root.Name.LocalName,
            FindValue(root, "faultCode"),
            FindValue(root, "faultString"),
            FindValue(root, "message"));
    }

    private static string? FindValue(
        XElement root,
        string localName)
    {
        string? value = root
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(
                    localName,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
