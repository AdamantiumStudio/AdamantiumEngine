using System.Net.Http.Headers;

namespace Adamantium.Pulsar.Live;

/// <summary>A station's stream over HTTP, asked for with its metadata (<c>Icy-MetaData: 1</c>): its sound, with the
/// metadata taken out where the station puts it in, and what the station says of itself in its headers.</summary>
internal sealed class LiveConnection : IDisposable
{
    // Once connected, as long as a station may say nothing before it is a drop.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient Client = new(new SocketsHttpHandler { ConnectTimeout = ConnectTimeout })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("Adamantium.Pulsar", "1") } },
    };

    private readonly HttpResponseMessage _response;

    private LiveConnection(HttpResponseMessage response, Stream sound, string station, int bitrate)
    {
        _response = response;
        Sound = sound;
        Station = station;
        Bitrate = bitrate;
    }

    /// <summary>The bytes of its sound, read only forward.</summary>
    public Stream Sound { get; }

    /// <summary>Its name, as its <c>icy-name</c> says; null where it says none.</summary>
    public string Station { get; }

    /// <summary>Bits a second, as its <c>icy-br</c> says; 0 where it says none.</summary>
    public int Bitrate { get; }

    /// <summary>Connects, and reads what the station says of itself.</summary>
    /// <param name="titleChanged">Told each new title the stream says, on the thread that reads it.</param>
    /// <exception cref="HttpRequestException">It could not be reached, or answered that it would not send.</exception>
    /// <exception cref="InvalidDataException">It sends what is not played: AAC, a list of other addresses.</exception>
    /// <exception cref="OperationCanceledException">It was cancelled, or took too long to answer.</exception>
    public static LiveConnection Open(Uri address, Action<string> titleChanged, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Add("Icy-MetaData", "1");
        HttpResponseMessage response;
        using (var answer = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            answer.CancelAfter(ConnectTimeout);
            response = Client.Send(request, HttpCompletionOption.ResponseHeadersRead, answer.Token);
        }

        try
        {
            response.EnsureSuccessStatusCode();
            CheckPlayable(response.Content.Headers.ContentType?.MediaType);
            Stream sound = new TimedReadStream(response.Content.ReadAsStream(cancellation), ReadTimeout, cancellation);
            if (NumberOf(response, "icy-metaint") is > 0 and var interval)
            {
                sound = new IcyStream(sound, interval, titleChanged);
            }

            var station = TextOf(response, "icy-name");
            return new LiveConnection(response, sound, string.IsNullOrWhiteSpace(station) ? null : station.Trim(),
                Math.Max(0, NumberOf(response, "icy-br")) * 1000);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Sound.Dispose();
        _response.Dispose();
    }

    // What a station sends is said by its type; one that says none, or what any stream is, is found by its bytes.
    private static void CheckPlayable(string type)
    {
        switch (type?.ToLowerInvariant())
        {
            case "audio/aac" or "audio/aacp" or "audio/x-aac" or "audio/mp4":
                throw new InvalidDataException("The station sends AAC, which is not played.");
            case "audio/x-mpegurl" or "audio/mpegurl" or "application/vnd.apple.mpegurl" or "audio/x-scpls" or
                "application/pls+xml":
                throw new InvalidDataException("It is a list of addresses, not a station's stream.");
        }
    }

    private static string TextOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static int NumberOf(HttpResponseMessage response, string name) =>
        int.TryParse(TextOf(response, name)?.Split(',')[0], out var number) ? number : -1;
}
