using Microsoft.CodeAnalysis;

using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace SourceCrafter.DependencyInjection;

enum DepsOps
{
    Get,
    MarkAsResolved
}

internal sealed class Dependencies
{


#if DISG_HOST
    internal static bool TryBroadcastDependencies(CancellationToken token, AssemblyIdentity contextId, DependencyMapDictionary containers, out string error)
    {
        int attempts = 2;
        error = null!;

        while (attempts-- > -1)
            try
            {
                _ = System.Threading.Tasks.Task.Run(() => ServeDependencies(contextId, containers, token), token);

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

    static void ServeDependencies(AssemblyIdentity asemblyInfo, DependencyMapDictionary containers, CancellationToken token)
    {
        #region Create server signal
        var contextId = $"{asemblyInfo.Name}:{asemblyInfo.PublicKey}";

        using var serverSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"DiSGen:{contextId}");
        using var requestHeaderMmf = MemoryMappedFile.CreateOrOpen($"DiSGenEvt:{contextId}", 1024);

        #endregion

        while (!token.IsCancellationRequested)
        {
            using var requestHeaderAccesor = requestHeaderMmf.CreateViewAccessor();

            if(!serverSignal.WaitOne()) continue;

            try
            {
                var requestHeaderBuffer = new byte[1024];

                requestHeaderAccesor.ReadArray(0, requestHeaderBuffer, 0, 1024);

                var requestHeader = Encoding.Default.GetString(requestHeaderBuffer).TrimEnd('\0').Split(['|'], StringSplitOptions.RemoveEmptyEntries);
                var requestId = requestHeader[0];
                var requestLength = int.Parse(requestHeader[1]);
                var requestBuffer = new byte[requestLength];
#if DEBUG_SG
                Trace.TraceInformation($"Opening DiSGenReq:{requestId}");
#endif
                using var requestMemoryMappedFile = MemoryMappedFile.OpenExisting($"DiSGenReq:{requestId}");
                using var requestViewAccessor = requestMemoryMappedFile.CreateViewAccessor();
                using var clientSignal = EventWaitHandle.OpenExisting($"DiSGenReqEvt:{requestId}");

                requestViewAccessor.ReadArray(0, requestBuffer, 0, requestLength);

                var request = Encoding.Default.GetString(requestBuffer).Split(['|'], StringSplitOptions.RemoveEmptyEntries);
                string containerFullType = request[0];
                Lifetime lifetime = Enum.TryParse(request[1], out Lifetime _lt) ? _lt : Lifetime.Transient;
                string typeName = request[2];
                string key = request.Length is 4 ? request[3] : "";

#if DEBUG_SG
                Trace.TraceInformation($"Create DiSGenRes:{requestId}");
#endif
                if (containers.TryGetValue(containerFullType, out var containerServices) && containerServices.TryGetValue((lifetime, typeName, key), out var serviceDescriptor))
                {
                    StringBuilder invocation = new();

                    serviceDescriptor.BuildAsExternalValue(invocation);

                    var responseBuffer = Encoding.Default.GetBytes($"{invocation}|{serviceDescriptor.Disposability}|{serviceDescriptor.ContainerDisposability}");

                    using var responseMemoryMappedFile = MemoryMappedFile.CreateOrOpen($"DiSGenRes:{requestId}", responseBuffer.Length);
                    using var responseViewAccessor = responseMemoryMappedFile.CreateViewAccessor();

                    // Send response header (length) and data
                    requestViewAccessor.WriteArray(0, BitConverter.GetBytes(responseBuffer.Length), 0, 4);
                    responseViewAccessor.WriteArray(0, responseBuffer, 0, responseBuffer.Length);
                    clientSignal.Set();
                    clientSignal.WaitOne();
                }
                else
                {
                    using var responseMemoryMappedFile = MemoryMappedFile.CreateOrOpen($"DiSGenRes:{requestId}", 1);
                    using var responseViewAccessor = responseMemoryMappedFile.CreateViewAccessor();

                    // Sends not found
                    requestViewAccessor.WriteArray(0, BitConverter.GetBytes(1), 0, 4);
                    responseViewAccessor.WriteArray(0, [0], 0, 1);
                    clientSignal.Set();
                    clientSignal.WaitOne();
                }

#if DEBUG_SG
                Trace.TraceInformation($"Server closing {requestId}DiSGenReq");
#endif
            }
            catch(Exception e)
            {
#if DEBUG_SG
                Trace.TraceError($"Server error: {e}");
#endif
            }
        }
    }

#else

    internal static DependencyResult? GetDependency(
        AssemblyIdentity asemblyInfo,
        string containerFullType,
        Lifetime lifetime,
        string typeName,
        string key)
    {
        var contextId = $"{asemblyInfo.Name}:{asemblyInfo.PublicKey}";
        using var serverSignal = EventWaitHandle.OpenExisting($"DiSGen:{contextId}");

        try
        {
            #region Write header

            using var requestHeader = MemoryMappedFile.OpenExisting($"DiSGenEvt:{contextId}");
            var requestHeaderBuffer = new byte[1024];
            var requestId = Guid.NewGuid();
            var request = $"{containerFullType}|{lifetime}|{typeName}|{key}";
            var requestMessage = Encoding.Default.GetBytes(request);

            Encoding.Default.GetBytes($"{requestId}|{requestMessage.Length}").CopyTo(requestHeaderBuffer, 0);

            using var serverViewAccessor = requestHeader.CreateViewAccessor();

            serverViewAccessor.WriteArray(0, requestHeaderBuffer, 0, 1024);

            var bufferLength = Math.Max(requestMessage.Length, 4);

            #endregion

            #region Send request

#if DEBUG_SG
            Trace.TraceInformation($"Client creating DiSGenReq:{requestId}");
#endif
            using var requestMemoryMappedFile = MemoryMappedFile.CreateOrOpen($"DiSGenReq:{requestId}", bufferLength);
            using var clientSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"DiSGenReqEvt:{requestId}");

            using var requestViewAccessor = requestMemoryMappedFile.CreateViewAccessor();

            requestViewAccessor.WriteArray(0, requestMessage, 0, requestMessage.Length);

            serverSignal.Set();

            if (!clientSignal.WaitOne()) return null;

            #endregion

            #region Get response

            try 
	        {
                var responseLengthBuffer = new byte[4];

                requestViewAccessor.ReadArray(0, responseLengthBuffer, 0, 4);

                var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);

                var responseBuffer = new byte[responseLength];	 
                var responseId = $"DiSGenRes:{requestId}";
#if DEBUG_SG
                Trace.TraceInformation($"Opening {responseId}");
#endif
                using var responseMemoryMappedFile = MemoryMappedFile.OpenExisting(responseId);
                using var responseViewAccessor = responseMemoryMappedFile.CreateViewAccessor();
                
                responseViewAccessor.ReadArray(0, responseBuffer, 0, responseLength);

                if(responseLength == 1 && !responseViewAccessor.ReadBoolean(0))
                {
#if DEBUG_SG
                Trace.TraceInformation($"Not Found [{request}]");
#endif
                    return null;
                }

                var response = Encoding.Default.GetString(responseBuffer).Split(['|'], StringSplitOptions.RemoveEmptyEntries);

                #endregion

#if DEBUG_SG
                Trace.TraceInformation($"Found [{string.Join(", ", response)}]");
#endif

                return new(response[0], 
                    Enum.TryParse(response[1], out Disposability _lt) ? _lt : Disposability.None, 
                    Enum.TryParse(response[2], out Disposability _lt2) ? _lt2 : Disposability.None);
	        }
	        catch (Exception e)
	        {
#if DEBUG_SG
                Trace.TraceError($"Client error: {e}");
#endif
	        }
            finally
            {
#if DEBUG_SG
                Trace.TraceInformation($"Client closing DiSGenReq:{requestId}");
#endif
                clientSignal.Set();
            }
        }
	    catch (Exception e)
	    {
#if DEBUG_SG
            Trace.TraceError($"Client error: {e}");
#endif
	    }

        return null;
    }

#endif
}

internal class DependencyResult(string invocation, Disposability disposability, Disposability containerDisposability)
{
    public readonly string Invocation = invocation;
    public readonly Disposability Disposability = disposability;
    public readonly Disposability ContainerDisposability = containerDisposability;
}
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
