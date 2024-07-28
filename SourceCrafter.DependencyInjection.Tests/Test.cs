//using Microsoft.Extensions.Configuration;

//namespace SourceCrafter.DependencyInjection.Tests;

//public partial class Server : global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
//{
//    private static global::Microsoft.Extensions.Configuration.IConfiguration? __appConfig__;

//    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection.MsConfiguration", "1.24.208.12")]
//    private static global::Microsoft.Extensions.Configuration.IConfiguration? __APPCONFIG__
//    {
//        get
//        {
//            if (__appConfig__ is null)
//                lock (__lock)
//                    return __appConfig__ ??= new ConfigurationBuilder()
//                        .SetBasePath(global::System.IO.Directory.GetCurrentDirectory())
//                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
//                        .Build();

//            return __appConfig__;
//        }
//    }

//    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection.MsConfiguration", "1.24.208.12")]
//    global::Microsoft.Extensions.Configuration.IConfiguration
//            global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
//                .GetService()
//    {
//        return __APPCONFIG__;
//    }

//    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection.MsConfiguration", "1.24.208.12")]
//    private static global::SourceCrafter.DependencyInjection.Tests.AppSettings? __appConfig__SETTING1;

//    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection.MsConfiguration", "1.24.208.12")]
//    private static global::SourceCrafter.DependencyInjection.Tests.AppSettings __APPCONFIG__SETTING1
//    {
//        get
//        {
//            if (__appConfig__SETTING1 is null)
//                lock (__lock)
//                    return __appConfig__SETTING1 ??= BuildSetting();

//            return __appConfig__SETTING1;

//            global::SourceCrafter.DependencyInjection.Tests.AppSettings BuildSetting()
//            {
//                var setting = new global::SourceCrafter.DependencyInjection.Tests.AppSettings();

//                __APPCONFIG__.GetSection("Setting1").Bind(setting);

//                return setting;
//            }
//        }
//    }

//}