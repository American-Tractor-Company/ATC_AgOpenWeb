// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgOpenWeb.RemoteServer;
using AgOpenWeb.Services.PathPlanning;
using NUnit.Framework;

namespace AgOpenWeb.RemoteServer.Tests;

/// <summary>The ATLAS route preview answers only a loopback caller holding the token, reads the
/// field on the host loop (never the HTTP thread), and refuses to plan a field that is moving or
/// changes while it plans.</summary>
[TestFixture]
public class FieldRoutePreviewEndpointTests
{
    private const string Token = "atlas-route-preview-test-token-0123456789abcdef";
    private record Input(RouteBasis Basis, RouteOptions Options);

    private SimpleWebServer _server = null!;
    private HttpClient _http = null!;
    private Thread _hostLoop = null!;
    private BlockingCollection<Action> _queue = null!;
    private Input _field = null!;
    private Func<RouteSnapshot> _capture = null!;
    private readonly ConcurrentBag<int> _captureThreads = new();

    [SetUp]
    public async Task SetUp()
    {
        _field = JsonSerializer.Deserialize<Input>(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "atlas-route-synthetic-field.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        _capture = () => new RouteSnapshot(_field.Basis, true);
        _captureThreads.Clear();
        _queue = new BlockingCollection<Action>();
        _hostLoop = new Thread(() => { foreach (var a in _queue.GetConsumingEnumerable()) a(); }) { IsBackground = true };
        _hostLoop.Start();

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _server = new SimpleWebServer(port);
        FieldRoutePreviewEndpoint.Register(_server, () =>
        {
            _captureThreads.Add(Environment.CurrentManagedThreadId);
            return _capture();
        }, Token, OnHostLoop, "test-build");
        await _server.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    private Task OnHostLoop(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() => { try { action(); done.SetResult(); } catch (Exception e) { done.SetException(e); } });
        return done.Task;
    }

    [TearDown]
    public async Task TearDown()
    {
        _http.Dispose();
        await _server.StopAsync();
        _queue.CompleteAdding();
        _hostLoop.Join();
        _queue.Dispose();
    }

    private string RequestPath(string? fieldName = null)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            fieldName = fieldName ?? _field.Basis.FieldName,
            trackName = _field.Basis.TrackName,
            options = _field.Options,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return "/atlas-route/v1/" + Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path, string? bearer = Token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        return (resp.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Test]
    public async Task WithoutTheToken_TheRouteIsRefused_AndTheFieldIsNotRead()
    {
        var missing = await GetAsync(RequestPath(), bearer: null);
        var wrong = await GetAsync(RequestPath(), bearer: Token.Replace('a', 'b'));
        Assert.Multiple(() =>
        {
            Assert.That(missing.Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(wrong.Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(wrong.Body.GetProperty("code").GetString(), Is.EqualTo("ROUTE_ACCESS_DENIED"));
            Assert.That(_captureThreads, Is.Empty);
        });
    }

    [Test]
    public async Task WithTheToken_TheFieldIsReadOnTheHostLoop_AndAPreviewReturned()
    {
        var (status, body) = await GetAsync(RequestPath());
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), body.ToString());
            Assert.That(body.GetProperty("planningOnly").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("executionEnabled").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("baseEngineHash").GetString(), Is.EqualTo("test-build"));
            Assert.That(_captureThreads, Has.Count.EqualTo(2));
            Assert.That(_captureThreads, Is.All.EqualTo(_hostLoop.ManagedThreadId));
        });
    }

    [Test]
    public async Task AMovingOrDifferentField_IsNotPlanned()
    {
        var otherField = await GetAsync(RequestPath(fieldName: "SOME_OTHER_FIELD"));
        _capture = () => new RouteSnapshot(_field.Basis, false);
        var moving = await GetAsync(RequestPath());
        Assert.Multiple(() =>
        {
            Assert.That(otherField.Body.GetProperty("code").GetString(), Is.EqualTo("ROUTE_ENGINE_STATE_CHANGED"));
            Assert.That(moving.Status, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(moving.Body.GetProperty("code").GetString(), Is.EqualTo("ROUTE_ENGINE_STATE_CHANGED"));
        });
    }

    [Test]
    public async Task AFieldThatChangesWhilePlanning_IsRefused()
    {
        int reads = 0;
        _capture = () => new RouteSnapshot(
            Interlocked.Increment(ref reads) == 1 ? _field.Basis : _field.Basis with { WidthM = _field.Basis.WidthM + 1 }, true);
        var (status, body) = await GetAsync(RequestPath());
        Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("ROUTE_ENGINE_STATE_CHANGED"));
    }

    [Test]
    public void ATokenShorterThan256Bits_CannotProtectTheRoute()
    {
        var server = new SimpleWebServer(0);
        var e = Assert.Throws<InvalidOperationException>(() =>
            FieldRoutePreviewEndpoint.Register(server, _capture, "short", OnHostLoop, "test-build"));
        Assert.That(e!.Message, Is.EqualTo("ROUTE_TOKEN_REQUIRED"));
    }
}
