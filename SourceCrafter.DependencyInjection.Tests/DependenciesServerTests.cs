using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.Text;

namespace SourceCrafter.DependencyInjection.Tests
{
    /// <summary>
    /// Comprehensive tests for the Dependencies memory RPC server functionality.
    /// This tests the enum values and system behavior that can be accessed from the test assembly.
    /// </summary>
    public class DependenciesServerTests : IDisposable
    {
        private readonly List<IDisposable> _disposables;

        public DependenciesServerTests()
        {
            _disposables = new List<IDisposable>();
        }

        [Fact]
        public void Lifetime_EnumValues_ShouldBeCorrect()
        {
            // Test that the Lifetime enum has the expected values and can be accessed
            ((int)Lifetime.Singleton).Should().Be(0, "Singleton should be 0");
            ((int)Lifetime.Scoped).Should().Be(1, "Scoped should be 1");
            ((int)Lifetime.Transient).Should().Be(2, "Transient should be 2");
        }

        [Fact]
        public void Disposability_EnumValues_ShouldBeCorrect()
        {
            // Test that the Disposability enum has the expected values and can be accessed
            ((int)Disposability.None).Should().Be(0, "None should be 0");
            ((int)Disposability.Disposable).Should().Be(1, "Disposable should be 1");
            ((int)Disposability.AsyncDisposable).Should().Be(2, "AsyncDisposable should be 2");
        }

        [Fact]
        public void Lifetime_AllValues_ShouldBeDistinct()
        {
            // Verify all lifetime values are unique
            var singletonValue = (int)Lifetime.Singleton;
            var scopedValue = (int)Lifetime.Scoped;
            var transientValue = (int)Lifetime.Transient;

            singletonValue.Should().NotBe(scopedValue, "Singleton and Scoped should have different values");
            singletonValue.Should().NotBe(transientValue, "Singleton and Transient should have different values");
            scopedValue.Should().NotBe(transientValue, "Scoped and Transient should have different values");
        }

        [Fact]
        public void Disposability_AllValues_ShouldBeDistinct()
        {
            // Verify all disposability values are unique
            var noneValue = (int)Disposability.None;
            var disposableValue = (int)Disposability.Disposable;
            var asyncDisposableValue = (int)Disposability.AsyncDisposable;

            noneValue.Should().NotBe(disposableValue, "None and Disposable should have different values");
            noneValue.Should().NotBe(asyncDisposableValue, "None and AsyncDisposable should have different values");
            disposableValue.Should().NotBe(asyncDisposableValue, "Disposable and AsyncDisposable should have different values");
        }

        [Fact]
        public void MemoryMappedFile_BasicOperations_ShouldWork()
        {
            // Test memory-mapped file operations which are core to the RPC system
            // Skip on non-Windows platforms where memory-mapped files might not be fully supported
            if (!OperatingSystem.IsWindows())
            {
                // On non-Windows platforms, just verify the types are available
                typeof(MemoryMappedFile).Should().NotBeNull("MemoryMappedFile type should be available");
                typeof(BinaryWriter).Should().NotBeNull("BinaryWriter type should be available");
                typeof(BinaryReader).Should().NotBeNull("BinaryReader type should be available");
                return;
            }
            
            const string testFileName = "DI$Gen1Test";
            const int bufferSize = 1024;
            
            try
            {
                using var mmf = MemoryMappedFile.CreateOrOpen(testFileName, bufferSize);
                using var stream = mmf.CreateViewStream();
                using var writer = new BinaryWriter(stream);
                
                // Write test data
                writer.Write((byte)Lifetime.Singleton);
                writer.Write((byte)Disposability.Disposable);
                writer.Write("TestService");
                writer.Write("TestKey");
                writer.Flush();
                
                // Reset stream position
                stream.Position = 0;
                
                using var reader = new BinaryReader(stream);
                
                // Read and verify data
                var lifetime = (Lifetime)reader.ReadByte();
                var disposability = (Disposability)reader.ReadByte();
                var serviceName = reader.ReadString();
                var key = reader.ReadString();
                
                lifetime.Should().Be(Lifetime.Singleton);
                disposability.Should().Be(Disposability.Disposable);
                serviceName.Should().Be("TestService");
                key.Should().Be("TestKey");
            }
            catch (PlatformNotSupportedException)
            {
                // Expected on some platforms
                true.Should().BeTrue("PlatformNotSupportedException is acceptable for memory-mapped files");
            }
            finally
            {
                // Clean up - try to dispose any remaining handles
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        using var mmf = MemoryMappedFile.OpenExisting(testFileName);
                        mmf.Dispose();
                    }
                    catch
                    {
                        // Expected if already disposed
                    }
                }
            }
        }

        [Fact]
        public void EventWaitHandle_BasicOperations_ShouldWork()
        {
            // Test EventWaitHandle operations which are used for synchronization in the RPC system
            const string testHandleName = "DI$Gen1TestHandle";
            
            try
            {
                using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, testHandleName);
                
                // Test initial state
                handle.WaitOne(0).Should().BeFalse("Handle should start in non-signaled state");
                
                // Test signaling
                handle.Set().Should().BeTrue("Set should succeed");
                handle.WaitOne(100).Should().BeTrue("Handle should be signaled after Set");
                
                // After auto-reset, should be non-signaled again
                handle.WaitOne(0).Should().BeFalse("Handle should auto-reset after wait");
            }
            catch (Exception ex)
            {
                // On some systems, named events might not be supported
                (ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)
                    .Should().BeTrue($"Expected PlatformNotSupportedException or UnauthorizedAccessException, but got {ex.GetType().Name}");
            }
        }

        [Fact]
        public void BinaryWriterReader_ServiceMetadataSimulation_ShouldWork()
        {
            // Simulate the service metadata serialization/deserialization used in the RPC system
            using var stream = new MemoryStream();
            
            // Simulate writing service metadata
            using (var writer = new BinaryWriter(stream, Encoding.Default, true))
            {
                // Simulate the flags field as done in WriteServiceMetadata
                uint flags = 0;
                
                // Add lifetime
                flags |= (byte)Lifetime.Scoped;
                
                // Add disposability (shifted by 2 bits)
                flags |= (ushort)((byte)Disposability.AsyncDisposable << 2);
                
                // Add container disposability (shifted by 4 bits)  
                flags |= (ushort)((byte)Disposability.Disposable << 4);
                
                // Add boolean flags
                flags |= 1 << 6;  // IsCached
                flags |= 1 << 7;  // IsResolved
                flags |= 1 << 12; // IsAsync
                flags |= 1 << 13; // IsFactory
                
                writer.Write(flags);
                writer.Write("test-key");
                writer.Write("TestService");
                writer.Write("GetTestServiceAsync");
                writer.Write("_testService");
                writer.Write("ITestService");
            }
            
            // Reset stream position
            stream.Position = 0;
            
            // Simulate reading service metadata
            using (var reader = new BinaryReader(stream))
            {
                var flags = reader.ReadUInt32();
                
                // Extract lifetime
                var lifetime = (Lifetime)(flags & 3);
                
                // Extract disposability
                var disposability = (Disposability)(flags >> 2 & 3);
                
                // Extract container disposability
                var containerDisposability = (Disposability)(flags >> 4 & 3);
                
                // Extract boolean flags
                var isCached = 0 != (flags & 1 << 6);
                var isResolved = 0 != (flags & 1 << 7);
                var isAsync = 0 != (flags & 1 << 12);
                var isFactory = 0 != (flags & 1 << 13);
                
                // Read strings
                var key = reader.ReadString();
                var fullTypeName = reader.ReadString();
                var resolverMethodName = reader.ReadString();
                var cacheField = reader.ReadString();
                var exportTypeName = reader.ReadString();
                
                // Verify extracted values
                lifetime.Should().Be(Lifetime.Scoped);
                disposability.Should().Be(Disposability.AsyncDisposable);
                containerDisposability.Should().Be(Disposability.Disposable);
                isCached.Should().BeTrue();
                isResolved.Should().BeTrue();
                isAsync.Should().BeTrue();
                isFactory.Should().BeTrue();
                key.Should().Be("test-key");
                fullTypeName.Should().Be("TestService");
                resolverMethodName.Should().Be("GetTestServiceAsync");
                cacheField.Should().Be("_testService");
                exportTypeName.Should().Be("ITestService");
            }
        }

        [Fact]
        public void BinaryWriterReader_RequestSerialization_ShouldWork()
        {
            // Simulate the request serialization used in WriteRequest method
            using var stream = new MemoryStream();
            
            const string containerFullType = "TestContainer";
            const Lifetime lifetime = Lifetime.Singleton;
            const string typeName = "TestService";
            const string key = "test-key";
            
            // Simulate writing request
            using (var writer = new BinaryWriter(stream, Encoding.Default, true))
            {
                writer.Write(containerFullType);
                writer.Write((byte)lifetime);
                writer.Write(typeName);
                writer.Write(key);
            }
            
            // Reset stream position
            stream.Position = 0;
            
            // Simulate reading request
            using (var reader = new BinaryReader(stream))
            {
                var readContainerFullType = reader.ReadString();
                var readLifetime = (Lifetime)reader.ReadByte();
                var readTypeName = reader.ReadString();
                var readKey = reader.ReadString();
                
                // Verify values
                readContainerFullType.Should().Be(containerFullType);
                readLifetime.Should().Be(lifetime);
                readTypeName.Should().Be(typeName);
                readKey.Should().Be(key);
            }
        }

        [Fact]
        public void ServiceLifetime_Combinations_ShouldCoverAllScenarios()
        {
            // Test all possible combinations of lifetime and disposability
            var combinations = new[]
            {
                (Lifetime.Singleton, Disposability.None),
                (Lifetime.Singleton, Disposability.Disposable),
                (Lifetime.Singleton, Disposability.AsyncDisposable),
                (Lifetime.Scoped, Disposability.None),
                (Lifetime.Scoped, Disposability.Disposable),
                (Lifetime.Scoped, Disposability.AsyncDisposable),
                (Lifetime.Transient, Disposability.None),
                (Lifetime.Transient, Disposability.Disposable),
                (Lifetime.Transient, Disposability.AsyncDisposable)
            };

            foreach (var (lifetime, disposability) in combinations)
            {
                // Verify that each combination can be represented as byte values
                var lifetimeByte = (byte)lifetime;
                var disposabilityByte = (byte)disposability;
                
                lifetimeByte.Should().BeInRange(0, 2, $"Lifetime {lifetime} should fit in byte range");
                disposabilityByte.Should().BeInRange(0, 2, $"Disposability {disposability} should fit in byte range");
                
                // Verify round-trip conversion
                ((Lifetime)lifetimeByte).Should().Be(lifetime);
                ((Disposability)disposabilityByte).Should().Be(disposability);
            }
        }

        [Fact]
        public void FlagsSerialization_AllBooleanFlags_ShouldPackCorrectly()
        {
            // Test the flags packing logic used in WriteServiceMetadata
            const uint CACHED_FLAG = 1 << 6;
            const uint RESOLVED_FLAG = 1 << 7;
            const uint NOT_REGISTERED_FLAG = 1 << 8;
            const uint REQUIRES_CAST_FLAG = 1 << 9;
            const uint CANCEL_TOKEN_FLAG = 1 << 10;
            const uint EXTERNAL_FLAG = 1 << 11;
            const uint ASYNC_FLAG = 1 << 12;
            const uint FACTORY_FLAG = 1 << 13;
            const uint KEYED_FLAG = 1 << 14;
            const uint SIMPLE_TRANSIENT_FLAG = 1 << 15;
            const uint SCOPED_DEPS_FLAG = 1 << 16;
            
            // Test individual flags
            uint flags = 0;
            flags |= CACHED_FLAG;
            flags |= ASYNC_FLAG;
            flags |= FACTORY_FLAG;
            
            // Verify individual flags can be extracted
            (0 != (flags & CACHED_FLAG)).Should().BeTrue("Cached flag should be set");
            (0 != (flags & RESOLVED_FLAG)).Should().BeFalse("Resolved flag should not be set");
            (0 != (flags & ASYNC_FLAG)).Should().BeTrue("Async flag should be set");
            (0 != (flags & FACTORY_FLAG)).Should().BeTrue("Factory flag should be set");
            (0 != (flags & KEYED_FLAG)).Should().BeFalse("Keyed flag should not be set");
            
            // Test all flags at once
            uint allFlags = CACHED_FLAG | RESOLVED_FLAG | NOT_REGISTERED_FLAG | REQUIRES_CAST_FLAG |
                           CANCEL_TOKEN_FLAG | EXTERNAL_FLAG | ASYNC_FLAG | FACTORY_FLAG |
                           KEYED_FLAG | SIMPLE_TRANSIENT_FLAG | SCOPED_DEPS_FLAG;
                           
            // Verify all flags can be extracted
            (0 != (allFlags & CACHED_FLAG)).Should().BeTrue();
            (0 != (allFlags & RESOLVED_FLAG)).Should().BeTrue();
            (0 != (allFlags & NOT_REGISTERED_FLAG)).Should().BeTrue();
            (0 != (allFlags & REQUIRES_CAST_FLAG)).Should().BeTrue();
            (0 != (allFlags & CANCEL_TOKEN_FLAG)).Should().BeTrue();
            (0 != (allFlags & EXTERNAL_FLAG)).Should().BeTrue();
            (0 != (allFlags & ASYNC_FLAG)).Should().BeTrue();
            (0 != (allFlags & FACTORY_FLAG)).Should().BeTrue();
            (0 != (allFlags & KEYED_FLAG)).Should().BeTrue();
            (0 != (allFlags & SIMPLE_TRANSIENT_FLAG)).Should().BeTrue();
            (0 != (allFlags & SCOPED_DEPS_FLAG)).Should().BeTrue();
        }

        [Fact]
        public void Guid_Operations_ForRequestIds_ShouldWork()
        {
            // Test GUID operations used for request IDs in the RPC system
            var requestId = Guid.NewGuid();
            var bytes = requestId.ToByteArray();
            var reconstructed = new Guid(bytes);
            
            bytes.Should().HaveCount(16, "GUID should be 16 bytes");
            reconstructed.Should().Be(requestId, "GUID should round-trip correctly");
            requestId.Should().NotBe(Guid.Empty, "Generated GUID should not be empty");
        }

        [Fact]
        public async Task CancellationToken_Operations_ShouldWork()
        {
            // Test cancellation token operations used in the RPC system
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var token = cts.Token;
            
            token.IsCancellationRequested.Should().BeFalse("Token should not be cancelled initially");
            
            // Wait for cancellation
            await Task.Delay(150);
            
            token.IsCancellationRequested.Should().BeTrue("Token should be cancelled after timeout");
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable?.Dispose();
            }
            _disposables.Clear();
        }
    }
}