﻿using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Speckle.Connectors.Logging.Internal;

internal static class TracingBuilder
{
  public static IDisposable Initialize(SpeckleTracing? logConfiguration, ResourceBuilder resourceBuilder)
  {
    var tracerProviderBuilder = OpenTelemetry
      .Sdk.CreateTracerProviderBuilder()
      .AddSource(Consts.TRACING_SOURCE)
      .AddHttpClientInstrumentation();
    foreach (var tracing in logConfiguration?.Otel ?? [])
    {
      tracerProviderBuilder = tracerProviderBuilder.AddOtlpExporter(x => ProcessOptions(tracing, x));
    }

    if (logConfiguration?.Console ?? false)
    {
      throw new NotImplementedException(
        "Dependency on Console logging has been removed as it is not used, and causes a ILRepack warning"
      );
      // tracerProviderBuilder = tracerProviderBuilder.AddConsoleExporter();
    }

    tracerProviderBuilder = tracerProviderBuilder
      .SetResourceBuilder(resourceBuilder)
      .SetSampler<AlwaysOnSampler>()
      .AddProcessor(new ActivityScopeActivityProcessor());

    return tracerProviderBuilder.Build();
  }

  private static void ProcessOptions(SpeckleOtelTracing tracing, OtlpExporterOptions options)
  {
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    var headers = string.Join(",", tracing.Headers?.Select(x => x.Key + "=" + x.Value) ?? []);
    if (headers.Length != 0)
    {
      options.Headers = headers;
    }

    if (tracing.Endpoint is not null)
    {
      options.Endpoint = new Uri(tracing.Endpoint);
    }
  }
}
