// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

// Read-only ATLAS full-field route preview.
// Uses the engine's curve-offset and Dubins geometry. Never writes guidance or actuator state.
using System;
using System.Linq;
using System.Collections.Generic;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Guidance;
namespace AgOpenWeb.Services.PathPlanning;

public record RouteBasis(string FieldName,string TrackName,double OriginLat,double OriginLon,
    double[][] Boundary,double[][] Reference,int TrackType,double WidthM,double OverlapM,
    double OffsetM,int SkipRows,int SkipMode,int TurnStyle,double RadiusM,double ClearanceM,
    double ExtensionM,int Smoothing,double WheelbaseM,double TrackWidthM);
public record RouteOptions(int SchemaVersion,double EnvelopeRadiusM);
public record RouteSegment(int Order,string Kind,int PassIndex,string Solver,double[][] Points);
public record FieldRouteExport(int SchemaVersion,string PlannerVersion,string BaseEngineHash,
    bool PlanningOnly,bool ExecutionEnabled,RouteBasis Basis,RouteOptions Options,
    RouteSegment[] Segments,string[] Issues,int PassCount,int TurnCount);

public static class FieldRoutePreview
{
    public const string Version="atlas-field-route-1";
    private static double Dist(Vec3 a,Vec3 b)=>double.Hypot(a.Easting-b.Easting,a.Northing-b.Northing);
    private static double[] Pair(Vec3 p)=>new[]{p.Easting,p.Northing};
    private static Vec3 Point(double[] p)=>new(p[0],p[1],0);
    private static double Heading(Vec3 a,Vec3 b)=>Math.Atan2(b.Easting-a.Easting,b.Northing-a.Northing);
    public static double EdgeDistance(Vec3 p,IReadOnlyList<Vec3> ring)
    {
        double best=double.PositiveInfinity;
        for(int i=0;i<ring.Count;i++){
            var a=ring[i];var b=ring[(i+1)%ring.Count];double x=b.Easting-a.Easting,y=b.Northing-a.Northing;
            double t=Math.Clamp(((p.Easting-a.Easting)*x+(p.Northing-a.Northing)*y)/Math.Max(1e-20,x*x+y*y),0,1);
            best=Math.Min(best,double.Hypot(p.Easting-a.Easting-t*x,p.Northing-a.Northing-t*y));
        }return best;
    }
    public static bool Inside(Vec3 p,IReadOnlyList<Vec3> ring)
    {
        bool yes=false;
        for(int i=0,j=ring.Count-1;i<ring.Count;j=i++){
            var a=ring[i];var b=ring[j];
            if((a.Northing>p.Northing)!=(b.Northing>p.Northing)&&p.Easting<(b.Easting-a.Easting)*(p.Northing-a.Northing)/(b.Northing-a.Northing)+a.Easting)yes=!yes;
        }return yes;
    }
    private static List<Vec3> Dense(IReadOnlyList<Vec3> points,double spacing=.5)
    {
        var result=new List<Vec3>();
        for(int i=0;i<points.Count-1;i++){
            int n=Math.Max(1,(int)Math.Ceiling(Dist(points[i],points[i+1])/spacing));
            if(result.Count+n>12000)throw new InvalidOperationException("ROUTE_TOO_COMPLEX");
            for(int k=0;k<n;k++){double t=(double)k/n;result.Add(new(points[i].Easting+(points[i+1].Easting-points[i].Easting)*t,points[i].Northing+(points[i+1].Northing-points[i].Northing)*t,0));}
        }result.Add(points[^1]);return result;
    }
    private static List<List<Vec3>> Clip(IReadOnlyList<Vec3> points,IReadOnlyList<Vec3> ring,double inset)
    {
        var dense=Dense(points);var chunks=new List<List<Vec3>>();List<Vec3>? current=null;
        bool Good(Vec3 p)=>Inside(p,ring)&&EdgeDistance(p,ring)>=inset;
        Vec3 Cross(Vec3 a,Vec3 b,bool ga){for(int k=0;k<24;k++){var m=new Vec3((a.Easting+b.Easting)/2,(a.Northing+b.Northing)/2,0);if(Good(m)==ga)a=m;else b=m;}return ga?a:b;}
        for(int i=0;i<dense.Count;i++){
            bool good=Good(dense[i]);
            if(good){
                if(current==null){current=new();if(i>0)current.Add(Cross(dense[i-1],dense[i],false));}
                if(current.Count==0||Dist(current[^1],dense[i])>1e-6)current.Add(dense[i]);
            }else if(current!=null){
                current.Add(Cross(dense[i-1],dense[i],true));if(current.Count>=2&&Dist(current[0],current[^1])>1)chunks.Add(current);current=null;
            }
        }if(current!=null&&current.Count>=2&&Dist(current[0],current[^1])>1)chunks.Add(current);
        return chunks;
    }
    /// <param name="engineBuild">The running app's version and commit, reported as BaseEngineHash.</param>
    public static FieldRouteExport Generate(RouteBasis b,RouteOptions options,string engineBuild="unknown")
    {
        if(options.SchemaVersion!=1||!double.IsFinite(options.EnvelopeRadiusM)||options.EnvelopeRadiusM<b.WidthM/2||options.EnvelopeRadiusM>50)throw new InvalidOperationException("INVALID_ROUTE_OPTIONS");
        if(b.Boundary.Length<3||b.Boundary.Length>512||b.Reference.Length<2||b.Reference.Length>1024||b.TrackType!=2&&b.TrackType!=4)throw new InvalidOperationException("ROUTE_GEOMETRY_UNSUPPORTED");
        if(b.Boundary.Concat(b.Reference).Any(p=>p.Length!=2||p.Any(v=>!double.IsFinite(v)||Math.Abs(v)>100000)))throw new InvalidOperationException("INVALID_ROUTE_GEOMETRY");
        double spacing=b.WidthM-b.OverlapM;
        if(spacing<1||spacing>b.WidthM+1e-9||b.OffsetM!=0||b.RadiusM<1||b.RadiusM>50||b.ClearanceM<0||b.ClearanceM>100||b.ExtensionM<0||b.ExtensionM>100||b.SkipRows<0||b.SkipRows>9||b.SkipMode<0||b.SkipMode>2)throw new InvalidOperationException("ROUTE_CONFIGURATION_UNSUPPORTED");
        var ring=b.Boundary.Select(Point).ToList();var reference=CurveProcessing.CalculateHeadings(b.Reference.Select(Point).ToList());
        double diagonal=double.Hypot(ring.Max(p=>p.Easting)-ring.Min(p=>p.Easting),ring.Max(p=>p.Northing)-ring.Min(p=>p.Northing));
        int limit=(int)Math.Ceiling(diagonal/spacing)+2;
        if(diagonal>2000||limit>128||(long)reference.Count*reference.Count*(limit*2+1)>100000000)throw new InvalidOperationException("ROUTE_TOO_COMPLEX");
        var timer=System.Diagnostics.Stopwatch.StartNew();var issues=new HashSet<string>();var passes=new List<(int Index,List<Vec3> Points)>();
        double inset=b.ClearanceM+options.EnvelopeRadiusM+.05;
        for(int k=-limit;k<=limit;k++){
            if(timer.ElapsedMilliseconds>15000)throw new InvalidOperationException("ROUTE_PLANNING_TIMEOUT");
            List<Vec3> offset;
            if(k==0)offset=reference;
            else {var pair=CurveProcessing.CreateOffsetCurveWithInfo(reference,k*spacing);offset=pair.Item1;}
            if(offset.Count<2)continue;
            offset=CurveProcessing.ExtendCurveEnds(offset,diagonal+20,2);
            var chunks=Clip(offset,ring,inset);
            if(chunks.Count>1)issues.Add("SPLIT_PASS_UNSUPPORTED");
            foreach(var chunk in chunks)passes.Add((k,chunk));
        }
        if(passes.Count<2)throw new InvalidOperationException("ROUTE_INSUFFICIENT_PASSES");
        if(passes.Count>128||passes.Sum(p=>p.Points.Count)>18000)throw new InvalidOperationException("ROUTE_TOO_COMPLEX");
        // Explicit offline skip-and-fill order. Runtime YouTurn does not consume this plan.
        var ordered=new List<(int Index,List<Vec3> Points)>();int step=b.SkipRows+1;
        for(int phase=0;phase<step;phase++)for(int i=phase;i<passes.Count;i+=step)ordered.Add(passes[i]);
        if(b.SkipMode==2)issues.Add("WORKED_COVERAGE_NOT_AVAILABLE");
        if(b.TurnStyle!=0)issues.Add("K_STYLE_PREVIEW_UNSUPPORTED");
        var segments=new List<RouteSegment>();int turnCount=0;
        for(int i=0;i<ordered.Count;i++){
            var work=new List<Vec3>(ordered[i].Points);if(i%2==1)work.Reverse();
            if(i>0){
                var previous=segments[^1].Points.Select(Point).ToList();
                var start=previous[^1];start.Heading=Heading(previous[^2],start);
                var end=work[0];end.Heading=Heading(work[0],work[1]);
                var candidates=new DubinsPathService(b.RadiusM).GeneratePreviewCandidates(start,end);
                bool Good(List<Vec3> candidate)=>candidate.All(p=>Inside(p,ring)&&EdgeDistance(p,ring)>=options.EnvelopeRadiusM+.025&&EdgeDistance(p,ring)<=inset+.5);
                var chosen=candidates.FirstOrDefault(Good);
                if(chosen==null){issues.Add("HEADLAND_TURN_INFEASIBLE");chosen=candidates.FirstOrDefault();}
                if(chosen==null||chosen.Count<2)throw new InvalidOperationException("ROUTE_NO_CONNECTOR");
                if(chosen.Count>2048)throw new InvalidOperationException("ROUTE_TOO_COMPLEX");
                segments.Add(new(segments.Count,"headland-turn",ordered[i].Index,"native-dubins-preview",chosen.Select(Pair).ToArray()));turnCount++;
            }
            segments.Add(new(segments.Count,"working-pass",ordered[i].Index,"native-curve-offset",work.Select(Pair).ToArray()));
        }
        if(segments.Sum(s=>s.Points.Length)>24000)throw new InvalidOperationException("ROUTE_TOO_COMPLEX");
        return new(1,Version,engineBuild,true,false,b,options,segments.ToArray(),issues.OrderBy(x=>x).ToArray(),passes.Count,turnCount);
    }
}
