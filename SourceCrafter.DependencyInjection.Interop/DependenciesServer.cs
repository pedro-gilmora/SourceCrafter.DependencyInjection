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

internal class DependenciesServer
{
    private readonly TcpListener server;
    private readonly CancellationTokenSource cancellationTokenSource;
    internal Dictionary<string, DependencyMap> _containers = null!;

    internal DependenciesServer()
    {
        cancellationTokenSource = new CancellationTokenSource();
        server = TcpListener.Create(9995);
        server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        Start();
    }

    internal void Start()
    {
        try
        {
            server.Start();
            StartAsync();
        }
        catch (IOException)
        {
        }

        async void StartAsync()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                ProcessRequests(await server.AcceptTcpClientAsync());
            }
        }

        void ProcessRequests(TcpClient client)
        {
            using (client)
            {
                using var stream = client.GetStream();
                using var reader = new BinaryReader(stream);
                using var writer = new BinaryWriter(stream);

                switch ((DepsOps)reader.ReadByte())
                {
                    case DepsOps.Get:
                        GetDependency(reader, writer);
                        break;
                    case DepsOps.MarkAsResolved:
                        break;
                    default:
                        writer.Write(false);
                        break;
                }
            }
        }
    }

    private void GetDependency(BinaryReader reader, BinaryWriter writer)
    {
        try
        {
            var containerFullType = reader.ReadString();
            var lifetime = (Lifetime)reader.ReadByte();
            var type = reader.ReadString();
            var key = reader.ReadString();

            if (_containers.TryGetValue(containerFullType, out var map) && map.TryGetValue((lifetime, type, key), out var serviceDescriptor))
            {
                StringBuilder sb = new();

                serviceDescriptor.BuildValue(sb);

                writer.Write(DependenciesClient.BuildChunk(bw =>
                {
                    bw.Write(true);
                    bw.Write(sb.ToString());
                }));
            }
            else
            { 
                writer.Write(false);
            }
        }
        catch (IOException)
        {

        }
    }

    internal void Stop()
    {
        server.Stop();
    }
}
public static class DependenciesClient
{
    public static string? GetDependency(string containerTypeName, Lifetime lifetime, string type, string? key)
    {
        try
        {
            using TcpClient client = new();

            client.Connect(new IPEndPoint(IPAddress.Loopback, 9995));

            using NetworkStream stream = client.GetStream();
            using BinaryReader reader = new(stream);
            using BinaryWriter writer = new(stream);           

            // Write the length of the data and the data itself
            writer.Write(BuildChunk(bw =>
            {
                bw.Write((byte)DepsOps.Get);
                bw.Write(containerTypeName);
                bw.Write((byte)lifetime);
                bw.Write(type);
                bw.Write(key);
            }));

            // Read response
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static byte[] BuildChunk(Action<BinaryWriter> action)
    {
        using MemoryStream ms = new();
        using BinaryWriter bw = new(ms);
        
        action(bw);

        return ms.ToArray();
    }
}
