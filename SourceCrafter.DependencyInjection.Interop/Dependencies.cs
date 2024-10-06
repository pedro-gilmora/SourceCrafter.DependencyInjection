using Microsoft.CodeAnalysis;

using SourceCrafter.DependencyInjection.Interop;

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceCrafter.DependencyInjection;

enum DepsOps
{
    Get,
    MarkAsResolved
}

public class Dependencies
{
    internal static Dependencies Server = null!;
    static object _lock = new();

    //private readonly TcpListener server;
    //private readonly CancellationTokenSource cancellationTokenSource;
    internal Dictionary<string, DependencyMap> _containers = null!;

    //internal DependenciesServer()
    //{
    //    cancellationTokenSource = new CancellationTokenSource();
    //    //server = TcpListener.Create(9995);
    //    server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    //    Start();
    //}

    //internal void Start()
    //{
    //    try
    //    {
    //        server.Start();
    //        StartAsync();
    //    }
    //    catch (IOException)
    //    {
    //    }

    //    async void StartAsync()
    //    {
    //        while (!cancellationTokenSource.IsCancellationRequested)
    //        {
    //            ProcessRequests(await server.AcceptTcpClientAsync());
    //        }
    //    }

    //    void ProcessRequests(TcpClient client)
    //    {
    //        using (client)
    //        {
    //            using var stream = client.GetStream();
    //            using var reader = new BinaryReader(stream);
    //            using var writer = new BinaryWriter(stream);

    //            switch ((DepsOps)reader.ReadByte())
    //            {
    //                case DepsOps.Get:
    //                    GetDependency(reader, writer);
    //                    break;
    //                case DepsOps.MarkAsResolved:
    //                    break;
    //                default:
    //                    writer.Write(false);
    //                    break;
    //            }
    //        }
    //    }
    //}

    //private void GetDependency(BinaryReader reader, BinaryWriter writer)
    //{
    //    try
    //    {
    //        var containerFullType = reader.ReadString();
    //        var lifetime = (Lifetime)reader.ReadByte();
    //        var type = reader.ReadString();
    //        var key = reader.ReadString();

    //        if (_containers.TryGetValue(containerFullType, out var map) && map.TryGetValue((lifetime, type, key), out var serviceDescriptor))
    //        {
    //            StringBuilder sb = new();

    //            serviceDescriptor.BuildValue(sb);

    //            writer.Write(BuildChunk(bw =>
    //            {
    //                bw.Write(true);
    //                bw.Write(sb.ToString());
    //                bw.Write((byte)serviceDescriptor.Disposability);
    //                bw.Write((byte)serviceDescriptor.ServiceContainer.disposability);
    //            }));
    //        }
    //        else
    //        { 
    //            writer.Write(false);
    //        }
    //    }
    //    catch (IOException)
    //    {

    //    }
    //}

    //internal void Stop()
    //{
    //    try { server.Server.Disconnect(true); } catch {}
    //    try { server.Stop(); } catch {}
    //}

    public static ServiceDescriptor? GetDependency(string containerTypeName, Lifetime lifetime, string typeName, string key)
    {
        return Server?._containers is { } _containers
            && _containers.TryGetValue(containerTypeName, out var map) && map.
            TryGetValue((lifetime, typeName, key), out var serviceDescriptor)
                ? serviceDescriptor
                : null;
    }

    //private static byte[] BuildChunk(Action<BinaryWriter> action)
    //{
    //    using MemoryStream ms = new();
    //    using BinaryWriter bw = new(ms);

    //    action(bw);

    //    return ms.ToArray();
    //}

    internal static void Clear()
    {
        Server?._containers.Clear();
    }

    internal static bool EnsureDependenciesServer(SourceProductionContext p, Dictionary<string, DependencyMap> containers, out string error)
    {
        int attempts = 2;
        error = null!;

        while (attempts-- > -1)
            try
            {
                if (Server is null)
                    lock (_lock) (Server ??= new())._containers = containers;
                else
                    Server._containers = containers;

                return true;
            }
            catch (Exception ex)
            {
                if (attempts == 0)
                {
                    error = ex.ToString();
                    return false;
                }
            }

        return false;
    }
}

//public record DependencyResult(string Invocation, Disposability Disposability, Disposability ContainerDisposability);
//public static class DependenciesClient
//{
//    public static DependencyResult? GetDependency(string containerTypeName, Lifetime lifetime, string type, string? key)
//    {
//        try
//        {
//            using TcpClient client = new();

//            client.Connect(new IPEndPoint(IPAddress.Loopback, 9995));

//            using NetworkStream stream = client.GetStream();
//            using BinaryReader reader = new(stream);
//            using BinaryWriter writer = new(stream);           

//            // Write the length of the data and the data itself
//            writer.Write(BuildChunk(bw =>
//            {
//                bw.Write((byte)DepsOps.Get);
//                bw.Write(containerTypeName);
//                bw.Write((byte)lifetime);
//                bw.Write(type);
//                bw.Write(key);
//            }));

//            // Read response
//            return reader.ReadBoolean() ? new(reader.ReadString(), (Disposability)reader.ReadByte(), (Disposability)reader.ReadByte()) : null;
//        }
//        catch (IOException)
//        {
//            return null;
//        }
//        catch (Exception)
//        {
//            return null;
//        }
//    }

//    internal static byte[] BuildChunk(Action<BinaryWriter> action)
//    {
//        using MemoryStream ms = new();
//        using BinaryWriter bw = new(ms);

//        action(bw);

//        return ms.ToArray();
//    }
//}
