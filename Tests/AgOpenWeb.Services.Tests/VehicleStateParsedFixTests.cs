// AgOpenWeb
// Copyright (C) 2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Reflection;
using System.Text;
using AgOpenWeb.Models;
using AgOpenWeb.Models.Configuration;
using AgOpenWeb.Services.Gps;

namespace AgOpenWeb.Services.Tests;

/// <summary>
/// While the simulator is the source, a receiver's fix is parsed into a copy and taken with
/// <see cref="VehicleState.CopyParsedFix"/>: every member the parser writes is taken, and the
/// control members (engagement, Free Drive, sections) set meanwhile are kept.
/// </summary>
[TestFixture]
public class VehicleStateParsedFixTests
{
    private static readonly FieldInfo[] Fields = typeof(VehicleState).GetFields(BindingFlags.Public | BindingFlags.Instance);

    private static string WithChecksum(string body)
    {
        byte checksum = 0;
        foreach (char c in body) checksum ^= (byte)c;
        return $"${body}*{checksum:X2}";
    }

    /// <summary>A state with every member set to a value derived from <paramref name="salt"/>.</summary>
    private static VehicleState Seeded(int salt)
    {
        object boxed = new VehicleState();
        int i = 0;
        foreach (var f in Fields)
        {
            i++;
            var t = f.FieldType;
            object v = t == typeof(bool) ? (salt % 2 == 1)
                : t.IsEnum ? Enum.ToObject(t, salt % 2 + 1)
                : Convert.ChangeType((salt * 37 + i) % 200, t);
            f.SetValue(boxed, v);
        }
        return (VehicleState)boxed;
    }

    private static VehicleState Parse(VehicleState state, string[] lines)
    {
        var epochs = new NmeaEpochAssembler();
        var store = new ConfigurationStore();
        foreach (var line in lines)
            NmeaParserServiceFast.TryParseIntoState(Encoding.ASCII.GetBytes(line), ref state, store, epochs, out _);
        return state;
    }

    private static readonly object[] Sentences =
    {
        new object[] { new[] { WithChecksum("PANDA,123456.00,4806.0000,N,01131.0000,E,4,12,0.9,100.0,0.0,5.5,900,12,0,0.00") } },
        new object[] { new[] { WithChecksum("PAOGI,162255.50,3924.90,N,00731.80,W,4,12,0.7,341.9,1.2,4.8,221.7,3.1,-1.2,0.5") } },
        new object[] { new[]
        {
            WithChecksum("GNGGA,123519.00,4807.038,N,01131.000,E,4,12,0.9,545.4,M,46.9,M,1.0,0000"),
            WithChecksum("GNVTG,054.7,T,034.4,M,005.5,N,010.2,K,A"),
            WithChecksum("GNGGA,123519.10,4807.039,N,01131.000,E,4,12,0.9,545.4,M,46.9,M,1.0,0000"),
        } },
    };

    [TestCaseSource(nameof(Sentences))]
    public void Every_member_the_parser_writes_is_taken_and_the_rest_are_kept(string[] lines)
    {
        VehicleState seedA = Seeded(3), seedB = Seeded(4);
        VehicleState parsedA = Parse(seedA, lines), parsedB = Parse(seedB, lines);
        var written = Fields.Where(f => !Equals(f.GetValue(parsedA), f.GetValue(seedA))
                                     || !Equals(f.GetValue(parsedB), f.GetValue(seedB))).ToArray();
        Assume.That(written.Any(f => f.Name == nameof(VehicleState.Latitude)), "the sentence parsed");

        var live = Seeded(6);
        live.CopyParsedFix(in parsedA);

        Assert.Multiple(() =>
        {
            foreach (var f in Fields)
            {
                if (written.Contains(f))
                    Assert.That(f.GetValue(live), Is.EqualTo(f.GetValue(parsedA)), $"{f.Name} is parsed but not taken");
                else if (f.Name is nameof(VehicleState.IsAutoSteerEngaged) or nameof(VehicleState.IsInFreeDriveMode)
                         or nameof(VehicleState.SectionStates) or nameof(VehicleState.SteerAngle))
                    Assert.That(f.GetValue(live), Is.EqualTo(f.GetValue(Seeded(6))), $"{f.Name} is a control member");
            }
        });
    }
}
