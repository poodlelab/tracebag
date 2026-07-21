using Docker.DotNet;

namespace Tracebag.Api.Docker;

public sealed class DockerClientFactory : IDisposable
{
    private readonly Lazy<DockerClient> _client = new(() =>
    {
        var dockerUri = Environment.GetEnvironmentVariable("DOCKER_HOST");
        var uri = string.IsNullOrWhiteSpace(dockerUri)
            ? new Uri("unix:///var/run/docker.sock")
            : new Uri(dockerUri);

        return new DockerClientConfiguration(uri).CreateClient();
    });

    public DockerClient Client => _client.Value;

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
