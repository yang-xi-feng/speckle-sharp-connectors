using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using RhinoObject = Rhino.DocObjects.RhinoObject;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

public abstract class RhinoObjectToSpeckleTopLevelConverter<TTopLevelIn, TInRaw, TOutRaw> : IToSpeckleTopLevelConverter
  where TTopLevelIn : RhinoObject
  where TInRaw : RG.GeometryBase
  where TOutRaw : Base
{
  public ITypedConverter<TInRaw, TOutRaw> Conversion { get; }

  protected RhinoObjectToSpeckleTopLevelConverter(ITypedConverter<TInRaw, TOutRaw> conversion)
  {
    Conversion = conversion;
  }

  // POC: IIndex would fix this as I would just request the type from `RhinoObject.Geometry` directly.
  protected abstract TInRaw GetTypedGeometry(TTopLevelIn input);

  public virtual Base Convert(object target)
  {
    var typedTarget = (TTopLevelIn)target;
    var typedGeometry = GetTypedGeometry(typedTarget);

    var result = Conversion.Convert(typedGeometry);

    // POC: Any common operations for all RhinoObjects should be done here, not on the specific implementer
    // Things like user-dictionaries and other user-defined metadata.
    if (!string.IsNullOrEmpty(typedTarget.Attributes.Name))
    {
      result["name"] = typedTarget.Attributes.Name;
    }

    return result;
  }
}
