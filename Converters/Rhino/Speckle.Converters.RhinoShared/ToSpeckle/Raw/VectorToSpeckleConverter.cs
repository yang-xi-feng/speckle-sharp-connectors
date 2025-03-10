﻿using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class VectorToSpeckleConverter : ITypedConverter<RG.Vector3d, SOG.Vector>
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public VectorToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Vector3d object to a Speckle Vector object.
  /// </summary>
  /// <param name="target">The Rhino Vector3d object to convert.</param>
  /// <returns>The converted Speckle Vector object.</returns>
  public SOG.Vector Convert(RG.Vector3d target) =>
    new(target.X, target.Y, target.Z, _settingsStore.Current.SpeckleUnits);
}
