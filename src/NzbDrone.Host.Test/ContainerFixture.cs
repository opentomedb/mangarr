using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Host;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Readarr.Http.ErrorManagement;
using IServiceProvider = System.IServiceProvider;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class ContainerFixture : TestBase
    {
        private IServiceProvider _container;

        [SetUp]
        public void SetUp()
        {
            _container = CreateContainer(c => c.AddDummyDatabase()).GetServiceProvider();
        }

        private static IContainer CreateContainer(Func<IContainer, IContainer> addDatabase)
        {
            var args = new StartupContext("first", "second");

            var container = addDatabase(new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(Bootstrap.ASSEMBLIES)
                .AddNzbDroneLogger())
                .AddStartupContext(args);

            // set up a dummy broadcaster and lifetime to allow tests to resolve
            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AuthOptions>>(new Mock<IOptions<AuthOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<ServerOptions>>(new Mock<IOptions<ServerOptions>>().Object);
            container.RegisterInstance<IOptions<UpdateOptions>>(new Mock<IOptions<UpdateOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            return container;
        }

        // Server messages (2026-09-26, final review): Startup.Configure opens the databases through Lazy<> only
        // after logger init, the pid file and the single-instance check. Nothing it takes as a parameter may
        // open one while being resolved (ReadarrErrorPipeline's localizer pulls ConfigService -> IMainDatabase;
        // a Lazy<IServerMessageLocalizer> did not stop that, because DryIoc builds the singletons under it).
        [Test]
        public void startup_configure_parameters_resolve_without_opening_a_database()
        {
            var container = CreateContainer(c =>
            {
                c.RegisterDelegate<IMainDatabase>(_ => throw new InvalidOperationException("main database opened"));
                c.RegisterDelegate<ILogDatabase>(_ => throw new InvalidOperationException("log database opened"));
                c.RegisterDelegate<ICacheDatabase>(_ => throw new InvalidOperationException("cache database opened"));
                return c;
            });
            var provider = container.GetServiceProvider();

            var failures = new List<string>();

            foreach (var parameter in typeof(Startup).GetMethod(nameof(Startup.Configure)).GetParameters())
            {
                // The application builder comes from ASP.NET, and the container is the container itself.
                if (parameter.ParameterType == typeof(IApplicationBuilder) || parameter.ParameterType == typeof(IContainer))
                {
                    continue;
                }

                try
                {
                    provider.GetRequiredService(parameter.ParameterType);
                }
                catch (Exception ex)
                {
                    failures.Add($"{parameter.ParameterType.Name}: {ex.GetBaseException().Message}");
                }
            }

            failures.Should().BeEmpty();
        }

        // Server messages (2026-09-26, final review): the controllers that gained IServerMessageLocalizer, and the
        // error pipeline, resolve from the app's container. QueueController isn't listed: its constructor reads
        // the custom formats from the database (QualityProfileService.GetDefaultProfile), which the dummy
        // database can't answer.
        [TestCase(typeof(ReadarrErrorPipeline))]
        [TestCase(typeof(Readarr.Api.V1.Blocklist.BlocklistController))]
        [TestCase(typeof(Readarr.Api.V1.Commands.CommandController))]
        [TestCase(typeof(Readarr.Api.V1.History.HistoryController))]
        [TestCase(typeof(Readarr.Api.V1.Indexers.ReleaseController))]
        [TestCase(typeof(Readarr.Api.V1.Indexers.ReleasePushController))]
        [TestCase(typeof(Readarr.Api.V1.ManualImport.ManualImportController))]
        [TestCase(typeof(Readarr.Api.V1.Queue.QueueDetailsController))]
        public void server_message_consumers_resolve(Type type)
        {
            _container.GetRequiredService(type).Should().NotBeNull();
        }

        [Test]
        public void should_be_able_to_resolve_indexers()
        {
            _container.GetRequiredService<IEnumerable<IIndexer>>().Should().NotBeEmpty();
        }

        [Test]
        public void should_be_able_to_resolve_downloadclients()
        {
            _container.GetRequiredService<IEnumerable<IDownloadClient>>().Should().NotBeEmpty();
        }

        [Test]
        public void container_should_inject_itself()
        {
            var factory = _container.GetRequiredService<IServiceFactory>();

            factory.Build<IIndexerFactory>().Should().NotBeNull();
        }

        [Test]
        public void should_resolve_command_executor_by_name()
        {
            var genericExecutor = typeof(IExecute<>).MakeGenericType(typeof(RssSyncCommand));

            var executor = _container.GetRequiredService(genericExecutor);

            executor.Should().NotBeNull();
            executor.Should().BeAssignableTo<IExecute<RssSyncCommand>>();
        }

        [Test]
        public void should_return_same_instance_via_resolve_and_resolveall()
        {
            var first = (DownloadMonitoringService)_container.GetRequiredService<IHandle<TrackedDownloadsRemovedEvent>>();
            var second = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();

            first.Should().BeSameAs(second);
        }

        [Test]
        public void should_return_same_instance_of_singletons_by_same_interface()
        {
            var first = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();
            var second = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();

            first.Should().BeSameAs(second);
        }

        [Test]
        public void should_return_same_instance_of_singletons_by_different_interfaces()
        {
            var first = _container.GetServices<IHandle<BookGrabbedEvent>>().OfType<DownloadMonitoringService>().Single();
            var second = (DownloadMonitoringService)_container.GetRequiredService<IExecute<RefreshMonitoredDownloadsCommand>>();

            first.Should().BeSameAs(second);
        }
    }
}
