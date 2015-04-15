﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using DeploymentHelpers;
using Microsoft.AspNet.Testing.xunit;
using Microsoft.Framework.Logging;
using Xunit;

namespace E2ETests
{
    // Uses ports ranging 5025 - 5039.
    public class PublishAndRunTests_OnX64
    {
        [ConditionalTheory, Trait("E2Etests", "E2Etests")]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono)]
        [InlineData(ServerType.WebListener, RuntimeFlavor.clr, RuntimeArchitecture.x64, "http://localhost:5025/", false)]
        //https://github.com/aspnet/KRuntime/issues/642
        //[InlineData(ServerType.Helios, RuntimeFlavor.CoreClr, RuntimeArchitecture.amd64, "http://localhost:5026/")]
        public void Publish_And_Run_Tests_On_AMD64(ServerType serverType, RuntimeFlavor runtimeFlavor, RuntimeArchitecture architecture, string applicationBaseUrl, bool noSource)
        {
            var testRunner = new PublishAndRunTests();
            testRunner.Publish_And_Run_Tests(serverType, runtimeFlavor, architecture, applicationBaseUrl, noSource);
        }
    }

    public class PublishAndRunTests_OnX86
    {
        [ConditionalTheory, Trait("E2Etests", "E2Etests")]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono)]
        [InlineData(ServerType.IISExpress, RuntimeFlavor.clr, RuntimeArchitecture.x86, "http://localhost:5027/", false)]
        [InlineData(ServerType.IISExpress, RuntimeFlavor.clr, RuntimeArchitecture.x86, "http://localhost:5028/", true)]
        public void Publish_And_Run_Tests_On_X86(ServerType serverType, RuntimeFlavor runtimeFlavor, RuntimeArchitecture architecture, string applicationBaseUrl, bool noSource)
        {
            var testRunner = new PublishAndRunTests();
            testRunner.Publish_And_Run_Tests(serverType, runtimeFlavor, architecture, applicationBaseUrl, noSource);
        }

        [ConditionalTheory, Trait("E2Etests", "E2Etests")]
        [FrameworkSkipCondition(RuntimeFrameworks.DotNet)]
        [InlineData(ServerType.Kestrel, RuntimeFlavor.mono, RuntimeArchitecture.x86, "http://localhost:5029/", false)]
        [InlineData(ServerType.Kestrel, RuntimeFlavor.mono, RuntimeArchitecture.x86, "http://localhost:5030/", true)]
        public void Publish_And_Run_Tests_On_Mono(ServerType serverType, RuntimeFlavor runtimeFlavor, RuntimeArchitecture architecture, string applicationBaseUrl, bool noSource)
        {
            var testRunner = new PublishAndRunTests();
            testRunner.Publish_And_Run_Tests(serverType, runtimeFlavor, architecture, applicationBaseUrl, noSource);
        }
    }

    public class PublishAndRunTests
    {
        public void Publish_And_Run_Tests(ServerType serverType, RuntimeFlavor runtimeFlavor, RuntimeArchitecture architecture, string applicationBaseUrl, bool noSource)
        {
            var logger = new LoggerFactory()
                            .AddConsole()
                            .CreateLogger(string.Format("Publish:{0}:{1}:{2}:{3}", serverType, runtimeFlavor, architecture, noSource));

            using (logger.BeginScope("Publish_And_Run_Tests"))
            {
                var stopwatch = Stopwatch.StartNew();

                logger.LogInformation("Variation Details : HostType = {hostType}, RuntimeFlavor = {flavor}, Architecture = {arch}, applicationBaseUrl = {appBase}",
                    serverType, runtimeFlavor, architecture, applicationBaseUrl);

                var musicStoreDbName = Guid.NewGuid().ToString().Replace("-", string.Empty);
                var connectionString = string.Format(DbUtils.CONNECTION_STRING_FORMAT, musicStoreDbName);
                logger.LogInformation("Pointing MusicStore DB to '{connString}'", connectionString);

                var deploymentParameters = new DeploymentParameters(Helpers.GetApplicationPath(), serverType, runtimeFlavor, architecture)
                {
                    ApplicationBaseUriHint = applicationBaseUrl,
                    PublishApplicationBeforeDeployment = true,
                    PublishWithNoSource = noSource,
                    UserAdditionalCleanup = parameters =>
                    {
                        if (!Helpers.RunningOnMono)
                        {
                            // Mono uses InMemoryStore
                            DbUtils.DropDatabase(musicStoreDbName, logger);
                        }
                    }
                };

                // Override the connection strings using environment based configuration
                deploymentParameters.EnvironmentVariables
                    .Add(new KeyValuePair<string, string>("SQLAZURECONNSTR_DefaultConnection", connectionString));

                bool testSuccessful = false;

                using (var deployer = ApplicationDeployerFactory.Create(deploymentParameters, logger))
                {
                    var deploymentResult = deployer.Deploy();
                    var httpClientHandler = new HttpClientHandler() { UseDefaultCredentials = true };
                    var httpClient = new HttpClient(httpClientHandler) { BaseAddress = new Uri(deploymentResult.ApplicationBaseUri) };

                    HttpResponseMessage response = null;

                    // Request to base address and check if various parts of the body are rendered & measure the cold startup time.
                    // Add retry logic since tests are flaky on mono due to connection issues
                    RetryHelper.RetryRequest(() =>
                    {
                        response = httpClient.GetAsync(string.Empty).Result;
                        return response;
                    }, logger: logger, cancellationToken: deploymentResult.HostShutdownToken);

                    logger.LogInformation("[Time]: Approximate time taken for application initialization : '{t}' seconds", stopwatch.Elapsed.TotalSeconds);

                    var validator = new Validator(httpClient, httpClientHandler, logger, deploymentResult);
                    validator.VerifyHomePage(response);

                    // Static files are served?
                    validator.VerifyStaticContentServed();

                    if (serverType != ServerType.IISExpress)
                    {
                        if (Directory.GetFiles(deploymentParameters.ApplicationPath, "*.cmd", SearchOption.TopDirectoryOnly).Length > 0)
                        {
                            throw new Exception("publishExclude parameter values are not honored.");
                        }
                    }

                    stopwatch.Stop();
                    logger.LogInformation("[Time]: Total time taken for this test variation '{t}' seconds.", stopwatch.Elapsed.TotalSeconds);
                    testSuccessful = true;
                }

                if (!testSuccessful)
                {
                    logger.LogError("Some tests failed.");
                }
            }
        }
    }
}