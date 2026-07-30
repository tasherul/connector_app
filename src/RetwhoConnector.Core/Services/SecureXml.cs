using System.Xml;
using System.Xml.Linq;
using RetwhoConnector.Core.Exceptions;

namespace RetwhoConnector.Core.Services;

internal static class SecureXml
{
    public static XDocument Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        try
        {
            using var textReader = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(
                textReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 2 * 1024 * 1024,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = false,
                });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new PosResponseException(
                "POS_INVALID_XML",
                "The POS returned malformed or unsafe XML.",
                exception);
        }
    }

    public static string RequiredValue(XElement root, string localName)
    {
        string? value = root
            .DescendantsAndSelf()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(
                    localName,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
        return string.IsNullOrEmpty(value)
            ? throw new PosResponseException(
                "POS_INVALID_RESPONSE",
                $"The POS response is missing required {localName} data.")
            : value;
    }
}
