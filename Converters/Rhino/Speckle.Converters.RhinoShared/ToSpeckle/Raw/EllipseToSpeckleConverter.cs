﻿using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class EllipseToSpeckleConverter : ITypedConverter<RG.Ellipse, SOG.Ellipse>
{
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public EllipseToSpeckleConverter(
    ITypedConverter<RG.Plane, SOG.Plane> planeConverter,
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Ellipse to a Speckle Ellipse.
  /// </summary>
  /// <param name="target">The Rhino Ellipse to convert.</param>
  /// <returns>The converted Speckle Ellipse.</returns>
  /// <remarks>
  /// ⚠️ Rhino ellipses are not curves. The result is a mathematical representation of an ellipse that can be converted into NURBS for display.
  /// </remarks>
  public SOG.Ellipse Convert(RG.Ellipse target)
  {
    var nurbsCurve = target.ToNurbsCurve();
    return new()
    {
      plane = _planeConverter.Convert(target.Plane),
      firstRadius = target.Radius1,
      secondRadius = target.Radius2,
      units = _settingsStore.Current.SpeckleUnits,
      domain = SOP.Interval.UnitInterval,
      length = nurbsCurve.GetLength(),
      area = Math.PI * target.Radius1 * target.Radius2,
      bbox = _boxConverter.Convert(new RG.Box(nurbsCurve.GetBoundingBox(true)))
    };
  }
}
