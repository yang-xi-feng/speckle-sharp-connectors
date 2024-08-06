using Rhino;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class PolylineToSpeckleConverter
  : ITypedConverter<RG.Polyline, SOG.Polyline>,
    ITypedConverter<RG.PolylineCurve, SOG.Polyline>
{
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly ITypedConverter<RG.Interval, SOP.Interval> _intervalConverter;
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;

  public PolylineToSpeckleConverter(
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    IConversionContextStack<RhinoDoc, UnitSystem> contextStack,
    ITypedConverter<RG.Interval, SOP.Interval> intervalConverter
  )
  {
    _boxConverter = boxConverter;
    _contextStack = contextStack;
    _intervalConverter = intervalConverter;
  }

  /// <summary>
  /// Converts the given Rhino polyline to a Speckle polyline.
  /// </summary>
  /// <param name="target">The Rhino polyline to be converted.</param>
  /// <returns>The converted Speckle polyline.</returns>
  /// <remarks>⚠️ This conversion assumes domain interval is (0,LENGTH) as Rhino Polylines have no domain. If needed, you may want to use PolylineCurve conversion instead. </remarks>
  public SOG.Polyline Convert(RG.Polyline target)
  {
    var box = _boxConverter.Convert(new RG.Box(target.BoundingBox));

    var count = target.IsClosed ? target.Count - 1 : target.Count;
    List<double> points = new(count * 3);
    for (int i = 0; i < count; i++)
    {
      RG.Point3d pt = target[i];
      points.Add(pt.X);
      points.Add(pt.Y);
      points.Add(pt.Z);
    }

    return new SOG.Polyline(points, _contextStack.Current.SpeckleUnits)
    {
      bbox = box,
      length = target.Length,
      domain = new(0, target.Length),
      closed = target.IsClosed
    };
  }

  /// <summary>
  /// Converts the given Rhino PolylineCurve to a Speckle polyline.
  /// </summary>
  /// <param name="target">The Rhino PolylineCurve to be converted.</param>
  /// <returns>The converted Speckle polyline.</returns>
  /// <remarks>✅ This conversion respects the domain of the original PolylineCurve</remarks>
  public SOG.Polyline Convert(RG.PolylineCurve target)
  {
    var result = Convert(target.ToPolyline());
    result.domain = _intervalConverter.Convert(target.Domain);
    return result;
  }
}
