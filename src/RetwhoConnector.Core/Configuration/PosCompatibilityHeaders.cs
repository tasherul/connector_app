namespace RetwhoConnector.Core.Configuration;

public static class PosCompatibilityHeaders
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/150.0.0.0 Safari/537.36";

    public const string AcceptEncoding = "gzip, deflate, br, zstd";
    public const string AcceptLanguage = "en-US,en;q=0.9,bn;q=0.8";
    public const string SecFetchDest = "empty";
    public const string SecFetchMode = "cors";
    public const string SecFetchSite = "same-origin";
    public const string SecChUa =
        "\"Not;A=Brand\";v=\"8\", \"Chromium\";v=\"150\", " +
        "\"Google Chrome\";v=\"150\"";
    public const string SecChUaMobile = "?0";
    public const string SecChUaPlatform = "\"Windows\"";
}
