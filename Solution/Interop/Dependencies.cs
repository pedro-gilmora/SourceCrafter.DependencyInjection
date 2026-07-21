using Microsoft.CodeAnalysis;
using SourceCrafter.DependencyInjection.Constants;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Reflection.PortableExecutable;
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

internal sealed class Dependencies //: IDisposable
{
#if DISG_HOST

    static void Broadcast(ServiceContainer container)
    {
        string handleName = $"Globals\\{container.CompilationId}\\{container.ProviderId}";

#if DISG
        Trace.WriteLine($"TryOpen for {handleName}");
#endif

        using NamedPipeServerStream serverStream = new(handleName, PipeDirection.InOut);

#if DISG
        Trace.WriteLine($"Waiting for {handleName}");
#endif

        using BinaryWriter writer = new(serverStream);
        using BinaryReader reader = new(serverStream);

        try
        {
            CancellationTokenSource cancelSrc = new(30000);
            serverStream.WaitForConnectionAsync(cancelSrc.Token).GetAwaiter().GetResult();

            while (reader.ReadByte() is > 0 and var len)
            {
                //Span<(int, string, string)> items = stackalloc (int, int, int)[len];
                //for (int i = 0; i < len; i++)
                //{
                //    var item = items[i] = (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                //    container.ServicesMap.TryGetValue()
                //}
            }

        }
        catch
#if DISG
        (Exception ex)
#endif
        {
#if DISG
            Trace.TraceError($"Server error: {ex}");
#endif
        }
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

    //#else

    public static ServiceMetadata GetDependencies(
        int compilationId,
        int containerTypeId,
        Lifetime lifetime,
        int typeId,
        string key)
    {
        string handleName = $"Globals\\{compilationId}\\{containerTypeId}";
#if DISG
        Trace.WriteLine($"TryOpen for {handleName}");
#endif
        using NamedPipeClientStream client = new(handleName);
#if DISG
        Trace.WriteLine($"Waiting for {handleName}");
#endif
        client.Connect(5000);

        using BinaryReader reader = new(client);

        return ReadServiceMetadata(reader);
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

            // Read strings
            Key = reader.ReadString(),
            FullTypeName = reader.ReadString(),
            ResolverMethodName = reader.ReadString(),
            CacheField = reader.ReadString(),
            ExportTypeName = reader.ReadString()
        };
    }

#endif
}