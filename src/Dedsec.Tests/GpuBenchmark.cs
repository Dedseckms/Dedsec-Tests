using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
namespace Dedsec.Tests;
public static class GpuBenchmark
{
    public static List<string> Adapters()
    {
        var names=new List<string>(); try { using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>(); for(uint i=0;factory.EnumAdapters1(i,out var adapter).Success;i++) using(adapter) names.Add(adapter.Description1.Description.TrimEnd('\0')); }catch{} return names;
    }
    public static unsafe Measurement Run(int index,double seconds,CancellationToken token)
    {
        using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>(); factory.EnumAdapters1((uint)index,out var adapter).CheckError(); using var selected=adapter;
        if((adapter.Description1.Flags & AdapterFlags.Software)!=0) return new("gpu_compute",0,"Miter/s","skipped","Программный адаптер не участвует в GPU benchmark",0);
        D3D11.D3D11CreateDevice(adapter,DriverType.Unknown,DeviceCreationFlags.None,new[]{FeatureLevel.Level_11_0},out ID3D11Device device,out ID3D11DeviceContext context).CheckError();
        using(device) using(context)
        {
            const string source="RWStructuredBuffer<uint> output : register(u0); [numthreads(64,1,1)] void main(uint3 id:SV_DispatchThreadID) { uint x=id.x+1; [loop] for(uint i=0;i<256;i++){ x ^= x << 13; x ^= x >> 17; x ^= x << 5; } output[id.x]=x; }";
            var code=Compiler.Compile(source,"main","dedsec-v1.hlsl","cs_5_0",ShaderFlags.OptimizationLevel3);
            using var shader=device.CreateComputeShader(code.Span);
            using var buffer=device.CreateBuffer(65536*4,BindFlags.UnorderedAccess,ResourceUsage.Default,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,4);
            using var view=device.CreateUnorderedAccessView(buffer);
            using var staging=device.CreateBuffer(65536*4,BindFlags.None,ResourceUsage.Staging,CpuAccessFlags.Read);
            using var query=device.CreateQuery(new QueryDescription(QueryType.Event));
            context.CSSetShader(shader); context.CSSetUnorderedAccessView(0,view);
            void DispatchAndWait() { token.ThrowIfCancellationRequested(); context.Dispatch(1024,1,1); context.End(query); context.Flush(); var deadline=Stopwatch.StartNew(); while(!context.GetData(query,out int done) || done==0) { token.ThrowIfCancellationRequested(); if(deadline.Elapsed.TotalSeconds>8) throw new TimeoutException("GPU completion timed out"); Thread.Sleep(1); } }
            DispatchAndWait(); var clock=Stopwatch.StartNew(); long passes=0; while(clock.Elapsed.TotalSeconds<seconds){DispatchAndWait();passes++;} clock.Stop();
            context.CSSetUnorderedAccessView(0,null); context.CopyResource(staging,buffer); var map=context.Map(staging,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
            try { var values=new ReadOnlySpan<uint>((void*)map.DataPointer,65536); for(uint i=0;i<65536;i++){uint expected=i+1;for(int j=0;j<256;j++){expected^=expected<<13;expected^=expected>>17;expected^=expected<<5;}if(values[(int)i]!=expected)throw new InvalidOperationException("GPU integer output verification failed");} } finally{context.Unmap(staging,0);}
            return new("gpu_compute",Math.Round(passes*65536d*256/clock.Elapsed.TotalSeconds/1e6,2),"Miter/s","ok",$"{adapter.Description1.Description.TrimEnd('\0')}; D3D11 FL11; 65536 lanes × 256 xorshift32 iterations; integer output verified; not game FPS",clock.Elapsed.TotalSeconds);
        }
    }
}
