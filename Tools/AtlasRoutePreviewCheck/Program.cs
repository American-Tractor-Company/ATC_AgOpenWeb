// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AgOpenWeb.Services.PathPlanning;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var fixture = JsonSerializer.Deserialize<Input>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "synthetic-field.json")), json)!;
var route = FieldRoutePreview.Generate(fixture.Basis, fixture.Options);
if (route.PassCount != 11 || route.TurnCount != 10 || route.Issues.Length != 0 ||
    !route.PlanningOnly || route.ExecutionEnabled)
    throw new InvalidOperationException("SYNTHETIC_ROUTE_CHECK_FAILED");
Console.WriteLine("SYNTHETIC_ROUTE_CHECK_PASSED: 11 passes, 10 turns, preview-only");
record Input(RouteBasis Basis, RouteOptions Options);
