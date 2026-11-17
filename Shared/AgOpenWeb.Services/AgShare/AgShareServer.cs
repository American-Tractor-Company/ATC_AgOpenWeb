// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

namespace AgOpenWeb.Services.AgShare
{
    /// <summary>
    /// The AgShare server a stored API key was entered for. The key goes in the Authorization header
    /// of every request, so it is kept only while the server stays the same: a new server gets no key
    /// until one is typed for it (as a stored NTRIP password goes only to its own caster).
    /// </summary>
    public static class AgShareServer
    {
        public const string Default = "https://agshare.agopengps.com";

        /// <summary>The server the requests go to: the default when none is set.</summary>
        public static string Effective(string? url) =>
            string.IsNullOrWhiteSpace(url) ? Default : url.Trim().TrimEnd('/');

        public static bool Same(string? a, string? b) =>
            string.Equals(Effective(a), Effective(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>The stored key when the server is unchanged, otherwise none.</summary>
        public static string KeyFor(string? storedServer, string? newServer, string? storedKey) =>
            Same(storedServer, newServer) ? storedKey ?? "" : "";
    }
}
