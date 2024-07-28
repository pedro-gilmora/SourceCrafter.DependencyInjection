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

            string value = e.GetService<string>("");
            //const string b = "a";
            //string value2 = e.GetService<string>(b);

            //await using var scope = e.CreateScope();
            //using var db = scope.GetService<AuthService>();
        }
    }

    public class AppSettings
    {
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
    }
}