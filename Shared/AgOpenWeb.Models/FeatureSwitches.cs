// AgOpenWeb
// Copyright (C) 2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace AgOpenWeb.Models;

/// <summary>Features a host can switch off. The bits go to every browser in the Hello frame.</summary>
[Flags]
public enum SwitchedFeature : byte
{
    None = 0,
    /// <summary>The ISOBUS virtual terminal (VT cards), with the gateway browsing.</summary>
    VirtualTerminal = 1,
    /// <summary>Web cards, and with them the card pane.</summary>
    WebCards = 2,
    /// <summary>AgShare upload and download.</summary>
    AgShare = 4,
    All = VirtualTerminal | WebCards | AgShare,
}

/// <summary>
/// Which of the switchable features this host runs. Upstream switches them all off for now; this
/// fork runs them all. A hidden feature is off: its controls are gone from every browser, the host
/// does not start its service (no VT stack, no gateway browsing) and refuses its commands. The
/// settings made for it are kept. <c>AGOPENWEB_FEATURES</c>, when set, replaces the default: a comma
/// list of <c>vt</c>, <c>webcards</c>, <c>agshare</c>, <c>all</c>, or <c>none</c>.
/// </summary>
public static class FeatureSwitches
{
    /// <summary>What a build runs when <c>AGOPENWEB_FEATURES</c> is not set: all of them.</summary>
    public const SwitchedFeature Default = SwitchedFeature.All;

    public const string EnvironmentVariable = "AGOPENWEB_FEATURES";

    private static SwitchedFeature? _enabled;

    /// <summary>The features on in this process (<c>AGOPENWEB_FEATURES</c>, or the default when it is not set), read once.</summary>
    public static SwitchedFeature Enabled
    {
        get => _enabled ??= Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable));
        set => _enabled = value; // tests
    }

    /// <summary>The features a value of <c>AGOPENWEB_FEATURES</c> turns on: the default when unset or blank.</summary>
    public static SwitchedFeature Resolve(string? list) =>
        string.IsNullOrWhiteSpace(list) ? Default : Parse(list);

    public static bool VirtualTerminal => (Enabled & SwitchedFeature.VirtualTerminal) != 0;
    public static bool WebCards => (Enabled & SwitchedFeature.WebCards) != 0;
    public static bool AgShare => (Enabled & SwitchedFeature.AgShare) != 0;

    /// <summary>The features a comma list names; unknown names (and <c>none</c>) add nothing.</summary>
    public static SwitchedFeature Parse(string? list)
    {
        var on = SwitchedFeature.None;
        if (string.IsNullOrWhiteSpace(list)) return on;
        foreach (var raw in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            on |= raw.ToLowerInvariant() switch
            {
                "vt" => SwitchedFeature.VirtualTerminal,
                "webcards" => SwitchedFeature.WebCards,
                "agshare" => SwitchedFeature.AgShare,
                "all" => SwitchedFeature.All,
                _ => SwitchedFeature.None,
            };
        }
        return on;
    }

    /// <summary>
    /// The feature a web command belongs to, or <see cref="SwitchedFeature.None"/> for the rest:
    /// <c>vt.*</c>, <c>webcard.*</c>, <c>agshare.*</c> and the AgShare settings
    /// (<c>config.set|conn.agShare…</c>).
    /// </summary>
    public static SwitchedFeature FeatureOf(string cmd, string arg)
    {
        if (cmd.StartsWith("vt.", StringComparison.Ordinal)) return SwitchedFeature.VirtualTerminal;
        if (cmd.StartsWith("webcard.", StringComparison.Ordinal)) return SwitchedFeature.WebCards;
        if (cmd.StartsWith("agshare.", StringComparison.Ordinal)) return SwitchedFeature.AgShare;
        if (cmd == "config.set" && arg.StartsWith("conn.agShare", StringComparison.Ordinal)) return SwitchedFeature.AgShare;
        return SwitchedFeature.None;
    }

    /// <summary>Is this web command allowed with these features on?</summary>
    public static bool Allows(string cmd, string arg, SwitchedFeature enabled)
    {
        var f = FeatureOf(cmd, arg);
        return f == SwitchedFeature.None || (enabled & f) != 0;
    }
}
