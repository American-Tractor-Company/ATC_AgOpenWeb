// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AgOpenWeb.Services.PathPlanning;
using NUnit.Framework;

namespace AgOpenWeb.Services.Tests.PathPlanning;

/// <summary>The ATLAS route preview plans the whole synthetic field, reports the build it ran on,
/// never offers itself for execution, and refuses options it can't honour.</summary>
[TestFixture]
public class FieldRoutePreviewTests
{
    private record Input(RouteBasis Basis, RouteOptions Options);

    private static Input Fixture() => JsonSerializer.Deserialize<Input>(
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "atlas-route-synthetic-field.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Test]
    public void SyntheticField_PlansEveryPassAndTurn_AsAPreviewOnly()
    {
        var input = Fixture();
        var route = FieldRoutePreview.Generate(input.Basis, input.Options, "26.7.0+abc1234-atlas-route-v1");

        Assert.Multiple(() =>
        {
            Assert.That(route.PassCount, Is.EqualTo(11));
            Assert.That(route.TurnCount, Is.EqualTo(10));
            Assert.That(route.Issues, Is.Empty);
            Assert.That(route.PlanningOnly, Is.True);
            Assert.That(route.ExecutionEnabled, Is.False);
            Assert.That(route.BaseEngineHash, Is.EqualTo("26.7.0+abc1234-atlas-route-v1"));
            Assert.That(route.Segments.Select(s => s.Order), Is.EqualTo(Enumerable.Range(0, route.Segments.Length)).Or.EqualTo(Enumerable.Range(1, route.Segments.Length)));
        });
    }

    [TestCase(2, 6.0)]
    [TestCase(1, 0.1)]
    [TestCase(1, 51.0)]
    [TestCase(1, double.NaN)]
    public void UnusableOptions_AreRefused(int schema, double envelopeRadius)
    {
        var input = Fixture();
        var e = Assert.Throws<InvalidOperationException>(() =>
            FieldRoutePreview.Generate(input.Basis, new RouteOptions(schema, envelopeRadius)));
        Assert.That(e!.Message, Is.EqualTo("INVALID_ROUTE_OPTIONS"));
    }
}
