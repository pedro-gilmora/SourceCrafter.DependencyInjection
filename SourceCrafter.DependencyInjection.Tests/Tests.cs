using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests
{
    public enum Test { Element }
    public class Tests
    {
        [Fact]
        public async Task Test2()
        {
            await using Server serverContainer = new();

            var e = serverContainer.GetRequiredService<AppSettings>();

            var db = await serverContainer.GetDatabaseAsync();

            db.TrySave(out var setting1);

            setting1.Should().Be("Value3");

            await using var scope = serverContainer.CreateScope();

            var id = await scope.GetRequiredKeyedService<Task<int>>("count");

            id.Should().Be(1);

            var database = await serverContainer.GetDatabaseAsync();

            var employeeService2 = await scope.GetRequiredService<Task<EmployeeController>>();

            var authService = await scope.GetAuthServiceAsync();

            employeeService2 = await scope.GetRequiredService<Task<EmployeeController>>();

            authService.Database.TrySave(out setting1);

            setting1.Should().Be("Value3");
        }
    }
}