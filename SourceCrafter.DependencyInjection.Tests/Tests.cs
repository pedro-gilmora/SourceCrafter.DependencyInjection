using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests
{
    public enum Test { Element }
    public class Tests
    {
        [Fact]
        public async Task _0_RawGeneratedResolverMembers()
        {
            await using Server serverContainer = new();

            serverContainer.B.IAs[1].Should().Be(serverContainer.AA);

            var db = await serverContainer.GetDatabaseAsync();

            db.TrySave(out var setting1);

            setting1.Should().Be("Value1");

            await using var scope = serverContainer.CreateScope();

            var id = await scope.GetCountAsyncCached(TestContext.Current.CancellationToken);

            id.Should().Be(1);

            var database = await serverContainer.GetDatabaseAsync();

            var authService = await scope.GetAuthServiceAsync(TestContext.Current.CancellationToken);

            authService.Database.TrySave(out setting1);

            setting1.Should().Be("Value1");
        }

        [Fact]
        public async Task _1_IServiceProviderGeneratedInterceptors()
        {
            await using Server serverContainer = new();

            var ias = serverContainer.GetRequiredServices<IA>();
            var iasFromB = serverContainer.B.IAs;

            ias.Should().HaveCount(2);
            iasFromB.Should().HaveCount(2);

            ias[0].Should().Be(iasFromB[0]);
            ias[1].Should().Be(iasFromB[1]);

            //Uncommenting this will complain with SCDI11: No dependency resolver was found for 'global::System.DateTime' at 'Server' container
            //var _ = serverContainer.GetRequiredService<DateTime>();

            var db = await serverContainer.GetRequiredServiceAsync<IDatabase>();

            db.TrySave(out var setting1);

            setting1.Should().Be("Value1");

            await using var scope = serverContainer.CreateScope();

            //Uncommenting this will complain with SCDI11: No dependency resolver was found for 'global::System.DateTime' at 'Server' container scope
            //var __ = scope.GetRequiredService<DateTime>();

            var id = await scope.GetRequiredKeyedServiceAsync<int>("count", TestContext.Current.CancellationToken);

            id.Should().Be(1);

            var ids = await scope.GetRequiredServicesAsync<int>(TestContext.Current.CancellationToken);

            var database = await scope.GetRequiredServiceAsync<IDatabase>();

            var authService = await scope.GetRequiredServiceAsync<IAuthService>(TestContext.Current.CancellationToken);

            authService.Database.TrySave(out setting1);

            setting1.Should().Be("Value1");
        }
    }
}