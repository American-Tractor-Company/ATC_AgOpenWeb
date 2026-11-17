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
}
