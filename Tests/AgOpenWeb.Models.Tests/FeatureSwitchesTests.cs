// AgOpenWeb
// Copyright (C) 2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using AgOpenWeb.Models;

namespace AgOpenWeb.Models.Tests;

/// <summary>
/// The VT cards, web cards and AgShare run by default in this fork (upstream switches them off for
/// now), <c>AGOPENWEB_FEATURES</c> can switch them off, and a feature switched off takes no commands.
/// </summary>
public class FeatureSwitchesTests
{
    [Test]
    public void Vt_web_cards_and_agshare_are_on_by_default()
        => Assert.That(FeatureSwitches.Default, Is.EqualTo(SwitchedFeature.All));

    [TestCase(null, SwitchedFeature.All)]
    [TestCase("  ", SwitchedFeature.All)]
    [TestCase("none", SwitchedFeature.None)]
    [TestCase("vt", SwitchedFeature.VirtualTerminal)]
    [TestCase("webcards,agshare", SwitchedFeature.WebCards | SwitchedFeature.AgShare)]
    public void The_environment_list_replaces_the_default(string? list, SwitchedFeature expected)
        => Assert.That(FeatureSwitches.Resolve(list), Is.EqualTo(expected));

    [TestCase(null, SwitchedFeature.None)]
    [TestCase("", SwitchedFeature.None)]
    [TestCase("vt", SwitchedFeature.VirtualTerminal)]
    [TestCase(" WebCards , agshare ", SwitchedFeature.WebCards | SwitchedFeature.AgShare)]
    [TestCase("all", SwitchedFeature.All)]
    [TestCase("vt,nonsense", SwitchedFeature.VirtualTerminal)]
    [TestCase("none", SwitchedFeature.None)]
    public void The_environment_list_turns_features_on(string? list, SwitchedFeature expected)
        => Assert.That(FeatureSwitches.Parse(list), Is.EqualTo(expected));

    [TestCase("vt.key", "0,s,5000,p")]
    [TestCase("vt.gateway", "192.168.5.10")]
    [TestCase("webcard.save", "-1\tHAT\thttp://{host}/")]
    [TestCase("webcard.delete", "0")]
    [TestCase("agshare.upload", "0\tField 1")]
    [TestCase("agshare.test", "")]
    [TestCase("config.set", "conn.agShareEnabled:1")]
    [TestCase("config.set", "conn.agShareApiKey:abc")]
    public void A_hidden_feature_takes_no_commands(string cmd, string arg)
    {
        Assert.That(FeatureSwitches.Allows(cmd, arg, SwitchedFeature.None), Is.False, "hidden");
        Assert.That(FeatureSwitches.Allows(cmd, arg, SwitchedFeature.All), Is.True, "turned on");
    }

    [TestCase("track.activate", "0")]
    [TestCase("config.set", "conn.ntripEnabled:1")]
    [TestCase("isobus.sectionControl", "")]
    [TestCase("field.close", "")]
    public void Everything_else_is_untouched(string cmd, string arg)
        => Assert.That(FeatureSwitches.Allows(cmd, arg, SwitchedFeature.None), Is.True);

    [Test]
    public void Each_feature_is_switched_on_its_own()
    {
        Assert.That(FeatureSwitches.Allows("vt.key", "", SwitchedFeature.WebCards), Is.False);
        Assert.That(FeatureSwitches.Allows("webcard.save", "", SwitchedFeature.AgShare), Is.False);
        Assert.That(FeatureSwitches.Allows("agshare.fetch", "", SwitchedFeature.VirtualTerminal), Is.False);
    }
}
