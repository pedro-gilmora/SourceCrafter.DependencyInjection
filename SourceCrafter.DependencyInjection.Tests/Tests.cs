using FluentAssertions;

using Microsoft.Extensions.Configuration;

namespace SourceCrafter.DependencyInjection.Tests
{
    public class Tests
    {

        [Fact]
        public async Task Test1()
        {
            await using Server e = new();

            //var config = e.GetService<IConfiguration>();
            await using var scope = e.CreateScope();
            using var db = scope.GetService<AuthService>();
            //config.GetSection("AppSetings").Get<AppSettings>()!.Setting1.Should().Be("Value1");

            //appSettings.Setting1.Should().Be("Value1");
        }
    }

    public class AppSettings
    {
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
    }
}