using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared;

public class CSiRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;
  private readonly ILogger<CSiRootToSpeckleConverter> _logger;

  public CSiRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    ILogger<CSiRootToSpeckleConverter> logger
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _logger = logger;
  }

  public Base Convert(object target)
  {
    if (target is not ICSiWrapper wrapper)
    {
      throw new ValidationException($"Target object is not a CSiWrapper. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var objectConverter = _toSpeckle.ResolveConverter(type, true);

    Base result = objectConverter.Convert(target);
    result.applicationId = $"{wrapper.ObjectType}{wrapper.Name}";

    return result;
  }
}
