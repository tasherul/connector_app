using System.Globalization;
using System.Xml.Linq;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class VdatetimeXmlMapper : IVdatetimeXmlMapper
{
    public VdatetimeResult Parse(string xml, DateTimeOffset fetchedAtUtc)
    {
        XDocument document = SecureXml.Parse(xml);
        XElement root = document.Root
            ?? throw Invalid("The POS response has no root element.");
        if (!root.Name.LocalName.Equals(
            "sysDateTime",
            StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The POS returned an unexpected XML document.");
        }

        IReadOnlyList<TimeZoneInfoDto> timeZones = root
            .Descendants()
            .Where(element => element.Name.LocalName.Equals(
                "tZone",
                StringComparison.OrdinalIgnoreCase))
            .Select(ParseTimeZone)
            .ToArray();

        return new VdatetimeResult
        {
            SiteId = SecureXml.RequiredValue(root, "site"),
            SystemDateTime = SecureXml.RequiredValue(root, "sysDT"),
            SystemTimeZoneId = SecureXml.RequiredValue(root, "sysTzId"),
            TimeZones = timeZones,
            RawXml = xml,
            FetchedAtUtc = fetchedAtUtc.ToUniversalTime(),
        };
    }

    private static TimeZoneInfoDto ParseTimeZone(XElement element)
    {
        string offsetText = SecureXml.RequiredValue(element, "offset");
        string dstText = SecureXml.RequiredValue(element, "dstApplies");
        if (!int.TryParse(
            offsetText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int offset))
        {
            throw Invalid("A POS timezone offset is invalid.");
        }

        bool dst = dstText switch
        {
            "0" => false,
            "1" => true,
            _ => throw Invalid("A POS daylight-saving value is invalid."),
        };

        return new TimeZoneInfoDto
        {
            TimeZoneId = SecureXml.RequiredValue(element, "tzId"),
            OffsetMinutes = offset,
            DstApplies = dst,
        };
    }

    private static PosResponseException Invalid(string message) =>
        new("POS_INVALID_RESPONSE", message);
}
