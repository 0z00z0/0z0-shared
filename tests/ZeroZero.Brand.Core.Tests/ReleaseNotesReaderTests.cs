using System.Net;
using Xunit;
using ZeroZero.Brand.WinUI;

namespace ZeroZero.Brand.Core.Tests;

public class ReleaseNotesReaderTests
{
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task ALongBodyIsCutAtTheCapAndSaysSo()
    {
        string notes = await Fetch(HttpStatusCode.OK, new string('x', ReleaseNotesReader.MaxCharacters * 3));

        Assert.Equal(new string('x', ReleaseNotesReader.MaxCharacters) + "…", notes);
    }

    [Fact]
    public async Task AShortBodyComesBackTrimmedAndAnEmptyOneSaysSo()
    {
        Assert.Equal("Fixed a thing.", await Fetch(HttpStatusCode.OK, "Fixed a thing.\n\n"));
        Assert.Equal(ReleaseNotesReader.NothingToShow, await Fetch(HttpStatusCode.OK, "  \n"));
    }

    [Fact]
    public async Task AFailedRequestThrows() =>
        await Assert.ThrowsAsync<HttpRequestException>(() => Fetch(HttpStatusCode.NotFound, "missing"));

    private static async Task<string> Fetch(HttpStatusCode status, string body)
    {
        using var http = new HttpClient(new Answering(status, body));
        return await ReleaseNotesReader.FetchAsync(http, "https://example.test/notes.txt", CancellationToken.None);
    }

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
