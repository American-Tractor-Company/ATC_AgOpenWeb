// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using AgOpenWeb.RemoteServer;
using NUnit.Framework;

namespace AgOpenWeb.RemoteServer.Tests;

/// <summary>Who may reach the embedded server: a page on another site must not open the
/// WebSocket (cross-site WebSocket hijacking) or POST, and a request for a host name the server
/// isn't (DNS rebinding) is refused, while the host's own page and native clients still work.</summary>
[TestFixture]
public class SimpleWebServerOriginTests
{
    private SimpleWebServer _server = null!;
    private int _port;
    private int _sockets;

    [SetUp]
    public async Task SetUp()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _sockets = 0;
        _server = new SimpleWebServer(_port);
        _server.AllowedHostNames.Add("tractor.example");
        _server.MapWebSocket("/ws", async (socket, ct) =>
        {
            Interlocked.Increment(ref _sockets);
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct); } catch { }
        });
        _server.MapPost("/echo", 16, req => Task.FromResult(SimpleWebServer.Response.Bytes(req.Body, "text/plain")));
        _server.MapGet("/hello", () => SimpleWebServer.Response.Text("hi", "text/plain"));
        await _server.StartAsync();
    }

    [TearDown]
    public async Task TearDown() => await _server.StopAsync();

    private string Local => $"127.0.0.1:{_port}";

    private async Task<int> SendAsync(string head)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        var buf = new byte[256];
        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!sb.ToString().Contains("\r\n"))
        {
            int n = await stream.ReadAsync(buf, cts.Token);
            if (n == 0) break;
            sb.Append(Encoding.ASCII.GetString(buf, 0, n));
        }
        var status = sb.ToString().Split(' ');
        return status.Length > 1 ? int.Parse(status[1]) : -1;
    }

    private Task<int> UpgradeAsync(string? host, string? origin)
    {
        var sb = new StringBuilder("GET /ws HTTP/1.1\r\n");
        if (host is not null) sb.Append("Host: ").Append(host).Append("\r\n");
        sb.Append("Upgrade: websocket\r\nConnection: Upgrade\r\n");
        sb.Append("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n");
        if (origin is not null) sb.Append("Origin: ").Append(origin).Append("\r\n");
        return SendAsync(sb.Append("\r\n").ToString());
    }

    [Test]
    public async Task Upgrade_FromAnotherSite_Is403_AndNoSocketReachesTheHub()
    {
        Assert.That(await UpgradeAsync(Local, "https://evil.example"), Is.EqualTo(403));
        Assert.That(await UpgradeAsync(Local, "null"), Is.EqualTo(403));
        Assert.That(await UpgradeAsync(Local, $"http://127.0.0.1:{_port + 1}"), Is.EqualTo(403), "another port is another origin");
        Assert.That(await UpgradeAsync($"localhost:{_port}", $"http://127.0.0.1:{_port}"), Is.EqualTo(403));
        Assert.That(_sockets, Is.Zero);
    }

    [Test]
    public async Task Upgrade_FromTheHostsOwnPage_Is101()
    {
        Assert.That(await UpgradeAsync(Local, $"http://127.0.0.1:{_port}"), Is.EqualTo(101));
        Assert.That(await UpgradeAsync($"localhost:{_port}", $"http://localhost:{_port}"), Is.EqualTo(101));
        Assert.That(await UpgradeAsync($"[::1]:{_port}", $"http://[::1]:{_port}"), Is.EqualTo(101));
        Assert.That(await UpgradeAsync($"192.168.1.20:{_port}", $"http://192.168.1.20:{_port}"), Is.EqualTo(101), "a LAN browser");
        Assert.That(await UpgradeAsync($"Tractor.Example:{_port}", $"http://tractor.example:{_port}"), Is.EqualTo(101));
    }

    [Test]
    public async Task Upgrade_WithoutOrigin_IsANativeClient_Is101()
    {
        Assert.That(await UpgradeAsync(Local, null), Is.EqualTo(101));
    }

    [Test]
    public async Task Upgrade_ForAnotherHostName_IsDnsRebinding_Is403()
    {
        // The attacker's name resolves to this host, so the page is "same origin" with itself.
        Assert.That(await UpgradeAsync($"rebind.evil.example:{_port}", $"http://rebind.evil.example:{_port}"), Is.EqualTo(403));
        Assert.That(_sockets, Is.Zero);
    }

    [Test]
    public async Task Request_WithoutHost_Is400()
    {
        Assert.That(await UpgradeAsync(null, null), Is.EqualTo(400));
        Assert.That(await SendAsync("GET /hello HTTP/1.1\r\n\r\n"), Is.EqualTo(400));
    }

    [Test]
    public async Task Get_ChecksTheHost()
    {
        Assert.That(await SendAsync($"GET /hello HTTP/1.1\r\nHost: {Local}\r\n\r\n"), Is.EqualTo(200));
        Assert.That(await SendAsync($"GET /hello HTTP/1.1\r\nHost: rebind.evil.example:{_port}\r\n\r\n"), Is.EqualTo(403));
    }

    [Test]
    public async Task Post_ChecksTheHostAndTheOrigin()
    {
        string Post(string host, string? origin) =>
            $"POST /echo HTTP/1.1\r\nHost: {host}\r\n" + (origin is null ? "" : $"Origin: {origin}\r\n")
            + "Content-Length: 2\r\n\r\nhi";
        Assert.That(await SendAsync(Post(Local, $"http://127.0.0.1:{_port}")), Is.EqualTo(200));
        Assert.That(await SendAsync(Post(Local, null)), Is.EqualTo(200));
        Assert.That(await SendAsync(Post(Local, "https://evil.example")), Is.EqualTo(403));
        Assert.That(await SendAsync(Post($"rebind.evil.example:{_port}", null)), Is.EqualTo(403));
    }
}
