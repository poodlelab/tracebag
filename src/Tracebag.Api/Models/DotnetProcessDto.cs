namespace Tracebag.Api.Models;

public sealed record DotnetProcessDto(int Pid, string Name, string CommandLine);
