// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

// Opt-in authenticated, loopback-only, read-only route export. No control acquisition.
using System.Text;
using System.Text.Json;
using AgOpenWeb.Services.PathPlanning;
namespace AgOpenWeb.RemoteServer;
public record RouteSnapshot(RouteBasis Basis,bool Idle);
public record RouteExportRequest(int SchemaVersion,string FieldName,string TrackName,RouteOptions Options);
public static class FieldRoutePreviewEndpoint
{
    private static readonly TimeSpan HostLoopWait=TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim Gate=new(1,1);
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    /// <param name="capture">Reads the host's field, track and machine state: it runs on the host loop
    /// through <paramref name="onHostLoop"/>, which owns that state, never on the HTTP thread.</param>
    public static void Register(SimpleWebServer server,Func<RouteSnapshot> capture,string token,Func<Action,Task> onHostLoop,string engineBuild)
    {
        RouteSnapshot Capture(){
            RouteSnapshot? snapshot=null;
            onHostLoop(()=>snapshot=capture()).WaitAsync(HostLoopWait).GetAwaiter().GetResult();
            return snapshot!;
        }
        server.MapProtectedLoopbackGetPrefix("/atlas-route/v1/",token,encoded=>{
            if(!Gate.Wait(0))return new(409,"application/json",Encoding.UTF8.GetBytes("{\"code\":\"ROUTE_PLANNER_BUSY\"}"));
            try{
                if(encoded.Length>2800)throw new InvalidOperationException("INVALID_ROUTE_REQUEST");
                var bytes=Convert.FromBase64String(encoded.Replace('-','+').Replace('_','/')+new string('=',(4-encoded.Length%4)%4));
                var request=JsonSerializer.Deserialize<RouteExportRequest>(bytes,Json)??throw new InvalidOperationException("INVALID_ROUTE_REQUEST");
                var before=Capture();
                if(request.SchemaVersion!=1||!before.Idle||before.Basis.FieldName!=request.FieldName||before.Basis.TrackName!=request.TrackName)throw new InvalidOperationException("ROUTE_ENGINE_STATE_CHANGED");
                var result=FieldRoutePreview.Generate(before.Basis,request.Options,engineBuild);
                var after=Capture();
                if(!after.Idle||JsonSerializer.Serialize(before.Basis,Json)!=JsonSerializer.Serialize(after.Basis,Json))throw new InvalidOperationException("ROUTE_ENGINE_STATE_CHANGED");
                var body=JsonSerializer.SerializeToUtf8Bytes(result,Json);
                if(body.Length>1500000)throw new InvalidOperationException("ROUTE_TOO_COMPLEX");
                return new(200,"application/json",body,new[]{("Cache-Control","no-store")});
            }catch(Exception e){
                var allowed=new[]{"INVALID_ROUTE_REQUEST","ROUTE_ENGINE_STATE_CHANGED","INVALID_ROUTE_OPTIONS","ROUTE_GEOMETRY_UNSUPPORTED","INVALID_ROUTE_GEOMETRY","ROUTE_CONFIGURATION_UNSUPPORTED","ROUTE_TOO_COMPLEX","ROUTE_PLANNING_TIMEOUT","ROUTE_INSUFFICIENT_PASSES","ROUTE_NO_CONNECTOR"};
                var code=e is TimeoutException?"ROUTE_HOST_BUSY":allowed.Contains(e.Message)?e.Message:"ROUTE_EXPORT_FAILED";
                return new(409,"application/json",JsonSerializer.SerializeToUtf8Bytes(new{code},Json));
            }finally{Gate.Release();}
        });
    }
}
