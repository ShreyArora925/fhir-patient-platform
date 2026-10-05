using Fhir.Application.Hl7;
using Fhir.Infrastructure;
using Fhir.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Fhir.UnitTests.Messaging;

public class DirectHl7MessagePublisherTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public void AddHl7Publishing_WithoutServiceBusOutsideDevelopment_Throws()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddHl7Publishing(EmptyConfiguration, new StubEnvironment(Environments.Production)));

        Assert.Contains("ServiceBus:ConnectionString", ex.Message);
    }

    [Fact]
    public void AddHl7Publishing_WithoutServiceBusInDevelopment_UsesDirectPublisher()
    {
        var services = new ServiceCollection();

        var usesServiceBus = services.AddHl7Publishing(EmptyConfiguration, new StubEnvironment(Environments.Development));

        Assert.False(usesServiceBus);
        Assert.Contains(services, d => d.ImplementationType == typeof(DirectHl7MessagePublisher));
    }

    [Fact]
    public void Constructor_OutsideDevelopment_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => new DirectHl7MessagePublisher(new NoOpHandler(), new StubEnvironment(Environments.Production)));
    }

    private sealed class NoOpHandler : IHl7MessageHandler
    {
        public Task<Hl7HandleResult> HandleAsync(string rawMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
