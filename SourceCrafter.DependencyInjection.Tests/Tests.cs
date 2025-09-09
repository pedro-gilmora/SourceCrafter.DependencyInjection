using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests
{
    public enum Test { Element }
    public class Tests
    {
        //[Fact]
        //public async Task Test2()
        //{
        //    await using Server serverContainer = new();

        //    serverContainer.GetDatabase().TrySave(out var setting1);

        //    setting1.Should().Be("Value1");

        //    await using var scope = serverContainer.CreateScope();

        //    var database = serverContainer.GetDatabase();
        //    var employeeService2 = await serverContainer.GetEmployeeServiceAsync();
        //    var authService = await scope.GetAuthServiceAsync();

        //    authService.Database.TrySave(out setting1);

        //    setting1.Should().Be("Value1");
        //}
    }
}