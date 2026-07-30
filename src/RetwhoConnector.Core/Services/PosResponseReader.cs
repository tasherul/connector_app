using System.IO.Compression;
using System.Text;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using ZstdSharp;

namespace RetwhoConnector.Core.Services;

public sealed class PosResponseReader : IPosResponseReader
{
    public async Task<PosHttpResponse> ReadAsync(
        HttpResponseMessage response,
        int maximumDecompressedBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (maximumDecompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDecompressedBytes));
        }

        PosResponseMetadata metadata = CaptureMetadata(response);
        if (metadata.ContentLength > maximumDecompressedBytes)
        {
            throw TooLarge();
        }

        await using Stream source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        Stream decoded = source;
        try
        {
            foreach (string encoding in metadata.ContentEncodings.Reverse())
            {
                decoded = encoding.Trim().ToLowerInvariant() switch
                {
                    "gzip" => new GZipStream(decoded, CompressionMode.Decompress),
                    "deflate" => new DeflateStream(decoded, CompressionMode.Decompress),
                    "br" => new BrotliStream(decoded, CompressionMode.Decompress),
                    "zstd" => new DecompressionStream(decoded),
                    "identity" or "" => decoded,
                    _ => throw new PosResponseException(
                        "POS_UNSUPPORTED_CONTENT_ENCODING",
                        "The POS returned an unsupported content encoding."),
                };
            }

            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int total = 0;
            while (true)
            {
                int read = await decoded.ReadAsync(
                    buffer,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maximumDecompressedBytes)
                {
                    throw TooLarge();
                }

                output.Write(buffer, 0, read);
            }

            string body;
            try
            {
                body = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(output.ToArray());
            }
            catch (DecoderFallbackException exception)
            {
                throw new PosResponseException(
                    "POS_INVALID_RESPONSE",
                    "The POS response is not valid UTF-8 text.",
                    exception);
            }

            return new PosHttpResponse
            {
                Metadata = metadata,
                Body = body,
            };
        }
        finally
        {
            if (!ReferenceEquals(decoded, source))
            {
                await decoded.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static PosResponseMetadata CaptureMetadata(
        HttpResponseMessage response) =>
        new()
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = Normalize(response.ReasonPhrase),
            ContentType = Normalize(
                response.Content.Headers.ContentType?.ToString()),
            ContentLength = response.Content.Headers.ContentLength,
            ContentEncodings = response.Content.Headers.ContentEncoding
                .Select(value => Normalize(value) ?? string.Empty)
                .ToArray(),
            Date = response.Headers.Date,
            Server = Normalize(string.Join(" ", response.Headers.Server)),
            Connection = Normalize(string.Join(", ", response.Headers.Connection)),
            RetryAfter = response.Headers.RetryAfter?.Delta,
            HasSetCookieHeader = response.Headers.Contains("Set-Cookie"),
            HasWwwAuthenticateHeader =
                response.Headers.WwwAuthenticate.Count > 0,
        };

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = new(
            value.Where(character =>
                !char.IsControl(character) ||
                character == '\t').ToArray());
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static PosResponseException TooLarge() =>
        new(
            "POS_INVALID_RESPONSE",
            "The POS response exceeded the configured size limit.");
}
