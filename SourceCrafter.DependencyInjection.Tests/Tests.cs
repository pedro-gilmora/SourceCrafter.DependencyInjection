﻿using FluentAssertions;

using Microsoft.Extensions.Configuration;

using System.ComponentModel;
using System;

namespace SourceCrafter.DependencyInjection.Tests
{
    public enum Test { Element }
    public class Tests
    {

        [Fact]
        public async Task Test1()
        {
            await using Server serverContainer = new();

            // Since IConfiguration is resolved externally we are limited to direct cast the container to get the equivalent generic call of it
            // Otherwise you'll get a "SCDI03 Type is not registered in container SourceCrafter.DependencyInjection.Tests.Server"
            // if Roslyn team get interested maybe they can solve this

#pragma warning disable SCDI03 // Type not registered in container
            serverContainer.GetService<IConfiguration>()
#pragma warning restore SCDI03 // Type not registered in container

                .GetSection("AppSettings")
                .Get<AppSettings>()!

                .Setting1.Should().Be("Value1");

            serverContainer.GetService<IDatabase>(Main.App).TrySave(out var setting1);

            setting1.Should().Be("Value1");

            // Checking non-static enum values ​​based on provided key
            serverContainer.GetService<string>(GetApplication()).Should().Be("Server::Name");

            int transientInt = await serverContainer.GetServiceAsync<int>(Main.App);

            await using var scope = serverContainer.CreateScope();

            // Async-resolved services
            using var authService = await scope.GetServiceAsync<AuthService>();

            authService.Database.TrySave(out setting1);

            setting1.Should().Be("Value1");

            static Main GetApplication()
            {
                return Main.App;
            }
        }
    }

    public class AppSettings
    {
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
    }
}