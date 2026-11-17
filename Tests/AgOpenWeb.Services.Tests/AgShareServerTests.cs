// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using AgOpenWeb.Services.AgShare;

namespace AgOpenWeb.Services.Tests;

/// <summary>The stored AgShare API key goes only to the server it was entered for.</summary>
[TestFixture]
public class AgShareServerTests
{
    [TestCase("https://agshare.agopengps.com", "https://agshare.agopengps.com")]
    [TestCase("https://agshare.agopengps.com", " HTTPS://AgShare.AgOpenGPS.com/ ")]
    [TestCase("", "https://agshare.agopengps.com")]
    [TestCase(null, "")]
    public void TheSameServer_KeepsTheStoredKey(string? stored, string next)
    {
        Assert.That(AgShareServer.KeyFor(stored, next, "secret-key"), Is.EqualTo("secret-key"));
    }

    [TestCase("https://agshare.agopengps.com", "https://attacker.example")]
    [TestCase("https://agshare.agopengps.com", "http://agshare.agopengps.com")]
    [TestCase("", "https://agshare.agopengps.com.attacker.example")]
    [TestCase("https://my.agshare.lan", "")]
    public void AnotherServer_GetsNoKey(string? stored, string next)
    {
        Assert.That(AgShareServer.KeyFor(stored, next, "secret-key"), Is.Empty);
    }

    [Test]
    public void NoServer_MeansTheDefault()
    {
        Assert.That(AgShareServer.Effective(null), Is.EqualTo(AgShareServer.Default));
        Assert.That(AgShareServer.Effective("  "), Is.EqualTo(AgShareServer.Default));
        Assert.That(AgShareServer.Effective(" https://my.agshare.lan/ "), Is.EqualTo("https://my.agshare.lan"));
    }

    [TestCase("")]
    [TestCase("https://agshare.agopengps.com")]
    [TestCase("https://my.agshare.example:8443/")]
    [TestCase("http://localhost:5000")]
    [TestCase("http://127.0.0.1")]
    [TestCase("http://[::1]:8080")]
    [TestCase("http://192.168.1.20")]
    [TestCase("http://10.0.0.5:8080")]
    [TestCase("http://172.16.4.2")]
    [TestCase("http://172.31.255.1")]
    [TestCase("http://[fd12:3456::1]")]
    [TestCase("http://agshare.local")]
    public void HttpsOrALocalServer_IsAllowed(string url)
    {
        Assert.That(AgShareServer.IsAllowed(url), Is.True);
    }

    [TestCase("http://agshare.agopengps.com")]
    [TestCase("http://attacker.example")]
    [TestCase("http://8.8.8.8")]
    [TestCase("http://172.32.0.1")]
    [TestCase("http://192.168.1.20.attacker.example")]
    [TestCase("http://local.attacker.example")]
    [TestCase("https://user:pass@agshare.agopengps.com")]
    [TestCase("ftp://agshare.agopengps.com")]
    [TestCase("file:///etc/passwd")]
    [TestCase("agshare.agopengps.com")]
    [TestCase("not a url")]
    public void APlainHttpInternetServer_IsRefused(string url)
    {
        Assert.That(AgShareServer.IsAllowed(url), Is.False);
    }
}
