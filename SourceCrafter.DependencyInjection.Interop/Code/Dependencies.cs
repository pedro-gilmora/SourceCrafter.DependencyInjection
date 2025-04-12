using Microsoft.CodeAnalysis;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceCrafter.DependencyInjection;

enum DepsOps
{
    Get,
    MarkAsResolved
}

internal sealed class Dependencies
{
    const string DiSuffix = "DI$Gen1";

#if DISG_HOST
    internal static bool TryBroadcastDependencies(CancellationToken token, AssemblyIdentity contextId, DependencyMapDictionary containers, out string error)
    {
        int attempts = 2;
        error = null!;

        while (attempts-- > -1)
            try
            {
                _ = Task.Run(() => ServeDependencies(contextId, containers, token), token);

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

    static void ServeDependencies(AssemblyIdentity asemblyInfo, DependencyMapDictionary servicesContainers, CancellationToken token)
    {
        var contextId = $"{asemblyInfo.Name}:{asemblyInfo.PublicKey}";

        #region Create server signal

        using var serverSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"{DiSuffix}:{contextId}");
        
        #endregion

        try
        {
            while (!serverSignal.SafeWaitHandle.IsClosed && !token.IsCancellationRequested)
            {
                using var requestHeaderMmf = MemoryMappedFile.CreateOrOpen($"{DiSuffix}Evt:{contextId}", 1024);
                
                if (!serverSignal.WaitOne(10)) continue;

                using var requestHeaderStream = requestHeaderMmf.CreateViewStream();
                using BinaryReader requestHeaderReader = new (requestHeaderStream);
                var requestId = new Guid(requestHeaderReader.ReadBytes(16));
                var requestLength = requestHeaderReader.ReadInt64();
                var requestBuffer = new byte[requestLength];
                using var clientSignal = EventWaitHandle.OpenExisting($"{DiSuffix}ReqEvt:{requestId}");

                try
                {
#if DEBUG_SG
                    Trace.TraceInformation($"Opening {DiSuffix}Req:{requestId}");
#endif
                    using var requestMemoryMappedFile = MemoryMappedFile.OpenExisting($"{DiSuffix}Req:{requestId}");
                    using var requestStream = requestMemoryMappedFile.CreateViewStream();
                    using BinaryReader requestReader = new(requestStream);

                    ReadRequest(requestReader, out var containerFullType, out var lifetime, out var typeName, out var key);

#if DEBUG_SG
                    Trace.TraceInformation($"Create {DiSuffix}Res:{requestId}");
#endif
                    if (servicesContainers.TryGetValue(containerFullType, out var servicesContainer) && servicesContainer.TryGetValue((lifetime, typeName, key), out var serviceDescriptor))
                    {
                        using BinaryWriter requestWriter = new(requestStream);
                        using BinaryWriter responseWriter = new(new MemoryStream(), Encoding.Default, false);

                        WriteServiceMetadata(responseWriter, serviceDescriptor);

                        var responseLength = responseWriter.BaseStream.Length;
                        using var responseMemoryMappedFile = MemoryMappedFile.CreateOrOpen($"{DiSuffix}Res:{requestId}", responseLength);

#if DEBUG_SG
                        Trace.TraceInformation($"Create {DiSuffix}Res:{requestId}");
#endif

                        requestWriter.Write(responseLength);
                        responseWriter.BaseStream.Position = 0;
                        responseWriter.BaseStream.CopyTo(responseMemoryMappedFile.CreateViewStream());

#if DEBUG_SG
                        Trace.TraceInformation($"Create {DiSuffix}Res:{requestId}");
#endif
                        clientSignal.Set();
                        clientSignal.WaitOne(10);
#if DEBUG_SG
                        Trace.TraceInformation($"Notified to server {DiSuffix}Res:{requestId}");
#endif
                    }
                    else
                    {
                        using BinaryWriter requestWriter = new(requestStream);
                        using var responseMemoryMappedFile = MemoryMappedFile.CreateOrOpen($"{DiSuffix}Res:{requestId}", 1);
                        using var responseStream = responseMemoryMappedFile.CreateViewStream();
                        using BinaryWriter responseWriter = new(responseStream);
                        
                        requestWriter.Write((long)1);
                        responseWriter.Write(false); // Not found flag


#if DEBUG_SG
                        Trace.TraceInformation($"Create {DiSuffix}Res:{requestId}");
#endif
                        clientSignal.Set();
                        clientSignal.WaitOne(10);
#if DEBUG_SG
                        Trace.TraceInformation($"Notified to server {DiSuffix}Res:{requestId}");
#endif
                    }
#if DEBUG_SG
                    Trace.TraceInformation($"Server closing {requestId}{DiSuffix}Req");
#endif
                }
			    finally
			    {
				    if (!clientSignal.SafeWaitHandle.IsClosed) clientSignal.Close();
			    }
            }

            serverSignal.Close();
        }
        catch (Exception e)
        {
#if DEBUG_SG
            Trace.TraceError($"Server error: {e}");
#endif
        }
        finally
        {
            serverSignal.Close();
        }
    }

    private static void ReadRequest(BinaryReader reader, out string containerFullType, out Lifetime lifetime, out string typeName, out string key)
    {
        containerFullType = reader.ReadString();
        lifetime = (Lifetime)reader.ReadByte();
        typeName = reader.ReadString();
        key = reader.ReadString();
    }

    private static void WriteServiceMetadata(BinaryWriter writer, ServiceDescriptor descriptor)
    {
        // Combinar enums y booleanos en un uint (2 bytes)
        uint flags = 0;

        // Enum
        flags |= (byte)descriptor.Lifetime;
        flags |= (ushort)((byte)descriptor.Disposability << 2);
        flags |= (ushort)((byte)descriptor.ContainerDisposability << 4);

        // Boolean
        if (descriptor.IsCached) flags |= 1 << 6;
        if (descriptor.IsResolved) flags |= 1 << 7;
        if (descriptor.NotRegistered) flags |= 1 << 8;
        if (descriptor.RequiresDisposabilityCast) flags |= 1 << 9;
        if (descriptor.IsCancelTokenParam) flags |= 1 << 10;
        if (descriptor.IsExternal) flags |= 1 << 11;
        if (descriptor.IsAsync) flags |= 1 << 12;
        if (descriptor.IsFactory) flags |= 1 << 13;
        if (descriptor.IsKeyed) flags |= 1 << 14;
        if (descriptor.IsSimpleTransient) flags |= 1 << 15;
        if (descriptor.HasScopedDependencies) flags |= 1 << 16;

        writer.Write(flags);

        // Leer strings
        writer.Write(descriptor.Key);
        writer.Write(descriptor.FullTypeName);

        StringBuilder sb = new();
        descriptor.BuildAsExternalValue(sb);
        writer.Write(sb.ToString()); //Write the resolver expression
        sb.Clear();

        writer.Write(descriptor.CacheField);
        writer.Write(descriptor.ExportTypeName);
    }

#else

    internal static ServiceMetadata? GetDependency(
        AssemblyIdentity asemblyInfo,
        string containerFullType,
        Lifetime lifetime,
        string typeName,
        string key)
    {
        var contextId = $"{asemblyInfo.Name}:{asemblyInfo.PublicKey}";
        using var serverSignal = EventWaitHandle.OpenExisting($"{DiSuffix}:{contextId}");

        try
        {
            #region Write header
            using var requestHeader = MemoryMappedFile.OpenExisting($"{DiSuffix}Evt:{contextId}");
            using var requestHeaderStream = requestHeader.CreateViewStream();
            using BinaryWriter requestHeaderWriter = new(requestHeaderStream);

            var requestId = Guid.NewGuid();

            requestHeaderWriter.Write(requestId.ToByteArray());
            requestHeaderWriter.Write(requestHeaderStream.Position);
            requestHeaderWriter.Flush();

            #endregion

            #region Send request
#if DEBUG_SG
            Trace.TraceInformation($"Client creating {DiSuffix}Req:{requestId}");
#endif
            using var requestMemoryMappedFile = MemoryMappedFile.CreateOrOpen($"{DiSuffix}Req:{requestId}", requestHeaderStream.Position);
            using var requestStream = requestMemoryMappedFile.CreateViewStream();
            using BinaryWriter requestWriter = new(requestStream);
            
            WriteRequest(requestWriter, containerFullType, lifetime, typeName, key);
            requestHeaderWriter.Flush();

            using var clientSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"{DiSuffix}ReqEvt:{requestId}");
            if (serverSignal.Set() && !clientSignal.WaitOne(10)) return default;

            #endregion

            #region Get response
            try
            {
                using var responseMemoryMappedFile = MemoryMappedFile.OpenExisting($"{DiSuffix}Res:{requestId}");
                using var responseStream = responseMemoryMappedFile.CreateViewStream();
                using BinaryReader requestReader = new(requestStream);
                using BinaryReader responseReader = new(responseStream);

                if (requestReader.ReadInt64() == 1 && !responseReader.ReadBoolean())
                {
#if DEBUG_SG
                    Trace.TraceInformation($"Service Not Found [{containerFullType}|{lifetime}|{typeName}|{key}]");
#endif
                    return null;
                }

                responseStream.Position = 0;

                var metadata = ReadServiceMetadata(responseReader);

#if DEBUG_SG
                Trace.TraceInformation($"Found method {metadata.ResolverMethodName}()");
#endif

                return metadata;
            }
            finally
            {
#if DEBUG_SG
                Trace.TraceInformation($"Client closing {DiSuffix}Req:{requestId}");
#endif
                clientSignal.Set();
            }
            #endregion
        }
        catch 
#if DEBUG_SG
        (Exception e)
#endif
        {
            serverSignal.Set();
#if DEBUG_SG
            Trace.TraceError($"Client error: {e}");
#endif
        }

        return default;
    }
    private static void WriteRequest(BinaryWriter writer, string containerFullType, Lifetime lifetime, string typeName, string key)
    {
        writer.Write(containerFullType);
        writer.Write((byte)lifetime);
        writer.Write(typeName);
        writer.Write(key);
    }

    private static ServiceMetadata ReadServiceMetadata(BinaryReader reader)
    {
        var flags = reader.ReadUInt32();

        return new()
        {
            // Enum
            Lifetime = (Lifetime)(flags & 3),
            Disposability = (Disposability)(flags >> 2 & 3),
            ContainerDisposability = (Disposability)(flags >> 4 & 3),

            // Boolean
            IsCached = 0 != (flags & 1 << 6),
            IsResolved = 0 != (flags & 1 << 7),
            NotRegistered = 0 != (flags & 1 << 8),
            RequiresDisposabilityCast = 0 != (flags & 1 << 9),
            IsCancelTokenParam = 0 != (flags & 1 << 10),
            IsExternal = 0 != (flags & 1 << 11),
            IsAsync = 0 != (flags & 1 << 12),
            IsFactory = 0 != (flags & 1 << 13),
            IsKeyed = 0 != (flags & 1 << 14),
            IsSimpleTransient = 0 != (flags & 1 << 15),
            HasScopedDependencies = 0 != (flags & 1 << 16),

            // Leer strings
            Key = reader.ReadString(),
            FullTypeName = reader.ReadString(),
            ResolverMethodName = reader.ReadString(),
            CacheField = reader.ReadString(),
            ExportTypeName = reader.ReadString()
        };
    }

#endif
}