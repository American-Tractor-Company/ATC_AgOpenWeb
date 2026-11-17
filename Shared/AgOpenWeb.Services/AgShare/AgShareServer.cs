// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;

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

        /// <summary>
        /// A server the key may be sent to: HTTPS to any host, or plain HTTP to this machine or a
        /// private network address (a farm's own AgShare server on the LAN). Anything else would
        /// carry the key in clear text across the internet, so no request is made.
        /// </summary>
        public static bool IsAllowed(string? url)
        {
            if (!Uri.TryCreate(Effective(url), UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.UserInfo))
                return false;
            if (u.Scheme == Uri.UriSchemeHttps) return true;
            return u.Scheme == Uri.UriSchemeHttp && IsLocalHost(u);
        }

        private static bool IsLocalHost(Uri u)
        {
            var host = u.IdnHost.TrimEnd('.');
            if (u.IsLoopback || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        /// <summary>The stored key when the server is unchanged, otherwise none.</summary>
        public static string KeyFor(string? storedServer, string? newServer, string? storedKey) =>
            Same(storedServer, newServer) ? storedKey ?? "" : "";
    }
}
