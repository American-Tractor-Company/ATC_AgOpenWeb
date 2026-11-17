// AgOpenWeb
// Copyright (C) 2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using AgOpenWeb.Models;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Configuration;
using AgOpenWeb.Models.Pipeline;
using AgOpenWeb.Services.Geometry;
using AgOpenWeb.Services.YouTurn;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace AgOpenWeb.Services.Tests.YouTurn;

/// <summary>
/// A reference nudge, smoothing or an end edit replaces the track's points on the same Track. The
/// end of the pass ahead is cached per track: it must be found again on the edited line, not kept
/// from the old one, or the turn is gated on the old line's end while it is built on the new one.
/// </summary>
[TestFixture]
[NonParallelizable] // ConfigurationStore is a singleton.
public class PassEndAfterTrackEditTests
{
    private const double Shift = 40;

    [SetUp]
    public void SetUp()
    {
        ConfigurationStore.SetInstance(new ConfigurationStore());
        var c = ConfigurationStore.Instance;
        c.Vehicle.Wheelbase = 2.5;
        c.Vehicle.MaxSteerAngle = 35;
        c.Tool.Overlap = 0;
        c.NumSections = 1;
        c.Tool.SetSectionWidth(0, 800);
        c.Guidance.UTurnRadius = 4.0;
        c.Guidance.UTurnExtension = 16.0;
        c.Guidance.UTurnDistanceFromBoundary = 2.0;
        c.Guidance.UTurnStyle = 0;
    }

    private static YouTurnStateMachine NewStateMachine()
    {
        var c = ConfigurationStore.Instance;
        return new YouTurnStateMachine(
            new YouTurnCreationService(NullLogger<YouTurnCreationService>.Instance, new PolygonOffsetService(), c),
            new YouTurnPathingService(NullLogger<YouTurnPathingService>.Instance, c),
            NullLogger<YouTurnStateMachine>.Instance, c);
    }

    private static List<Vec3> LineNorth(double easting)
    {
        var pts = new List<Vec3>();
        for (double n = -200; n <= 300; n += 5) pts.Add(new Vec3(easting, n, 0));
        return pts;
    }

    private static double DistanceAhead(YouTurnStateMachine sm, Models.Track.Track track, Boundary boundary,
        List<Vec3> turnLine, double easting)
    {
        var ctx = new YouTurnStateMachine.TickContext(
            new Position { Easting = easting, Northing = 0, Heading = 0, Speed = 2.78 },
            track, boundary, turnLine,
            UTurnSkipRows: 0, IsSkipWorkedMode: false,
            HeadlandCalculatedWidth: 10.0, HeadlandDistance: 10.0,
            IsAlternateSkipMode: false);
        var guidance = new GuidanceWorkingState { HowManyPathsAway = 0, IsHeadingSameWay = true };
        var turn = new YouTurnWorkingState { IsEnabled = true };
        sm.Tick(ctx, guidance, turn);
        return turn.DistanceToHeadland;
    }

    [Test]
    public void Moving_the_reference_line_finds_the_end_of_the_moved_pass()
    {
        // A field whose north end slants up to the east (northing 100 + 0.4 easting): a pass 40 m
        // east of the first ends some 16 m farther north.
        var ring = new List<Vec2> { new(-100, -100), new(100, -100), new(100, 140), new(-100, 60) };
        var offset = new PolygonOffsetService();
        var turnLine = offset.CalculatePointHeadings(offset.CreateInwardOffset(ring, 10)!);
        var boundary = new Boundary
        {
            OuterBoundary = new BoundaryPolygon { Points = ring.Select(p => new BoundaryPoint(p.Easting, p.Northing, 0)).ToList() },
        };
        var track = Models.Track.Track.FromCurve("North", LineNorth(0));

        var sm = NewStateMachine();
        double before = DistanceAhead(sm, track, boundary, turnLine, 0);
        track.Points = LineNorth(Shift); // the same Track, its points replaced, as RefNudgeSelectedTrack does
        double after = DistanceAhead(sm, track, boundary, turnLine, Shift);
        double fresh = DistanceAhead(NewStateMachine(), track, boundary, turnLine, Shift);

        Assert.That(before, Is.LessThan(1000), "the pass has an end ahead");
        Assert.That(fresh - before, Is.GreaterThan(10), "the moved pass ends farther north");
        Assert.That(after, Is.EqualTo(fresh).Within(0.01), "the end is that of the moved pass, not the cached one");
    }
}
